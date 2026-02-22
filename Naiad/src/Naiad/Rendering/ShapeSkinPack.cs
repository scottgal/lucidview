using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MermaidSharp.Rendering.Skins;

namespace MermaidSharp.Rendering;

sealed class ShapeSkinPack(string name, IReadOnlyDictionary<string, ShapeSkinTemplate> shapes)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, ShapeSkinTemplate> Shapes { get; } = shapes;

    public bool TryGetShape(string key, out ShapeSkinTemplate template) =>
        Shapes.TryGetValue(key, out template!);
}

readonly record struct ShapeSkinTemplate(
    string PathData,
    double ViewBoxX,
    double ViewBoxY,
    double ViewBoxWidth,
    double ViewBoxHeight,
    string? InlineStyle = null,
    IReadOnlyList<ShapeSkinPathLayer>? Layers = null,
    string? DefsContent = null)
{
    public string BuildTransform(double x, double y, double width, double height)
    {
        var safeViewWidth = Math.Max(0.000001, ViewBoxWidth);
        var safeViewHeight = Math.Max(0.000001, ViewBoxHeight);
        var sx = width / safeViewWidth;
        var sy = height / safeViewHeight;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"translate({x:0.######},{y:0.######}) scale({sx:0.######},{sy:0.######}) translate({-ViewBoxX:0.######},{-ViewBoxY:0.######})");
    }
}

readonly record struct ShapeSkinPathLayer(
    string PathData,
    string? InlineStyle = null);

static class ShapeSkinCatalog
{
    static readonly Regex ViewBoxPattern = new(
        "viewBox\\s*=\\s*[\"']\\s*(?<x>-?\\d+(?:\\.\\d+)?)\\s+(?<y>-?\\d+(?:\\.\\d+)?)\\s+(?<w>\\d+(?:\\.\\d+)?)\\s+(?<h>\\d+(?:\\.\\d+)?)\\s*[\"']",
        RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static readonly Regex WidthPattern = new("width\\s*=\\s*[\"'](?<w>[\\d.]+)", RegexOptions.IgnoreCase | RegexCompat.Compiled);
    static readonly Regex HeightPattern = new("height\\s*=\\s*[\"'](?<h>[\\d.]+)", RegexOptions.IgnoreCase | RegexCompat.Compiled);
    static readonly Regex DefsTagPattern = new(
        "<defs\\b[^>]*>(?<content>[\\s\\S]*?)</defs>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexCompat.Compiled);
    static readonly Regex PathTagPattern = new(
        "<path\\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexCompat.Compiled);
    static readonly Regex PathAttributePattern = new(
        "(?<name>[a-zA-Z_:][a-zA-Z0-9:._-]*)\\s*=\\s*(?<q>[\"'])(?<value>.*?)(?:\\k<q>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexCompat.Compiled);
    static readonly ConcurrentDictionary<string, ShapeSkinPack?> FilePackCache =
        new(StringComparer.OrdinalIgnoreCase);

    static readonly ConcurrentDictionary<string, ShapeSkinPack?> BuiltInPackCache =
        new(StringComparer.Ordinal);

    static readonly object PluginSync = new();
    static int PluginRevision;

    static readonly Dictionary<string, RegisteredPackEntry> RegisteredPacks =
        new(StringComparer.Ordinal);

    static readonly Dictionary<string, string> RegisteredPackAliases =
        new(StringComparer.Ordinal);

    static readonly IReadOnlyList<string> BuiltInPackNames =
    [
        "default",
        "daisyui",
        "material",
        "material3",
        "fluent2",
        "glass",
        "wireframe"
    ];

    static readonly IReadOnlyDictionary<string, string> BuiltInPackAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["default"] = "default",
            ["none"] = "default",
            ["daisy"] = "daisyui",
            ["daisyui"] = "daisyui",
            ["daisy-ui"] = "daisyui",
            ["material"] = "material",
            ["material2"] = "material",
            ["material-2"] = "material",
            ["material3"] = "material3",
            ["material-3"] = "material3",
            ["m3"] = "material3",
            ["fluent"] = "fluent2",
            ["fluent2"] = "fluent2",
            ["fluent-2"] = "fluent2",
            ["glass"] = "glass",
            ["wireframe"] = "wireframe",
            ["sketch"] = "wireframe",
            ["ui"] = "wireframe"
        };

    static readonly ShapeSkinPack DefaultPack = new(
        "default",
        new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal));

    static readonly IReadOnlyDictionary<string, string> EmbeddedArchiveResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["daisyui"] = "Naiad.daisyui.naiadskin",
            ["material"] = "Naiad.material.naiadskin",
            ["material3"] = "Naiad.material3.naiadskin",
            ["fluent2"] = "Naiad.fluent2.naiadskin",
            ["glass"] = "Naiad.glass.naiadskin",
            ["wireframe"] = "Naiad.wireframe.naiadskin"
        };

    readonly record struct RegisteredPackEntry(
        IDiagramSkinPackPlugin Plugin,
        ShapeSkinPack Pack,
        string CanonicalName);

    public static IReadOnlyList<string> GetBuiltInPackNames() => [.. BuiltInPackNames];

    public static IReadOnlyList<string> GetRegisteredPackNames()
    {
        lock (PluginSync)
        {
            return [.. RegisteredPacks.Values.Select(x => x.Plugin.Name)];
        }
    }

    public static IReadOnlyList<string> GetAvailablePackNames()
    {
        lock (PluginSync)
        {
            var names = new List<string>(BuiltInPackNames);
            foreach (var pluginName in RegisteredPacks.Values.Select(x => x.Plugin.Name))
            {
                if (names.Any(x => x.Equals(pluginName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                names.Add(pluginName);
            }

            return names;
        }
    }

    public static IReadOnlyCollection<IDiagramSkinPackPlugin> GetRegisteredPlugins()
    {
        lock (PluginSync)
        {
            return [.. RegisteredPacks.Values.Select(x => x.Plugin)];
        }
    }

    public static int GetRevision()
    {
        lock (PluginSync)
        {
            return PluginRevision;
        }
    }

    public static bool TryRegisterPlugin(IDiagramSkinPackPlugin plugin, out string? error)
    {
        try
        {
            RegisterPlugin(plugin);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void RegisterPlugin(IDiagramSkinPackPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var canonicalName = NormalizeKey(plugin.Name);
        if (string.IsNullOrWhiteSpace(canonicalName))
            throw new ArgumentException("Skin pack plugin name cannot be empty.", nameof(plugin));

        var shapes = new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal);
        foreach (var entry in plugin.Shapes)
        {
            var key = NormalizeKey(entry.Key);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var template = entry.Value;
            if (template.ViewBoxWidth <= 0 || template.ViewBoxHeight <= 0)
                continue;

            var layers = NormalizePluginLayers(template);
            if (layers.Count == 0)
                continue;

            var defs = SanitizeDefsContent(template.DefsContent);
            var normalizedTemplate = new ShapeSkinTemplate(
                layers[0].PathData,
                template.ViewBoxX,
                template.ViewBoxY,
                template.ViewBoxWidth,
                template.ViewBoxHeight,
                layers[0].InlineStyle,
                layers,
                defs);

            shapes[key] = NormalizeTemplateForShapeKey(key, normalizedTemplate);
        }

        if (shapes.Count == 0)
            throw new ArgumentException("Skin pack plugin does not provide any valid shapes.", nameof(plugin));

        var pack = new ShapeSkinPack(canonicalName, shapes);

        lock (PluginSync)
        {
            if (RegisteredPacks.TryGetValue(canonicalName, out var existing))
                RemoveAliases(existing.CanonicalName);

            RegisteredPacks[canonicalName] = new RegisteredPackEntry(plugin, pack, canonicalName);
            AddAlias(canonicalName, canonicalName);
            foreach (var alias in plugin.Aliases)
            {
                var normalizedAlias = NormalizeKey(alias);
                if (string.IsNullOrWhiteSpace(normalizedAlias))
                    continue;
                AddAlias(normalizedAlias, canonicalName);
            }

            PluginRevision++;
        }
    }

    public static bool UnregisterPlugin(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return false;

        var key = NormalizeKey(pluginName);
        lock (PluginSync)
        {
            if (!RegisteredPackAliases.TryGetValue(key, out var canonicalName))
                canonicalName = key;

            if (!RegisteredPacks.Remove(canonicalName, out _))
                return false;

            RemoveAliases(canonicalName);
            PluginRevision++;
            return true;
        }
    }

    public static bool TryResolveShapeTemplate(
        RenderOptions options,
        string diagramKey,
        string shapeKey,
        out ShapeSkinTemplate template)
    {
        template = default;

        var pack = ResolvePack(options);
        if (pack is null || pack.Shapes.Count == 0)
            return false;

        var diagramScopedKey = NormalizeKey($"{diagramKey}.{shapeKey}");
        var globalKey = NormalizeKey(shapeKey);

        return pack.TryGetShape(diagramScopedKey, out template) ||
               pack.TryGetShape(globalKey, out template);
    }

    static ShapeSkinPack? ResolvePack(RenderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SkinPack))
            return null;

        var packKey = NormalizeKey(options.SkinPack);
        if (TryResolveRegisteredPack(packKey, out var registeredPack))
            return registeredPack;

        if (TryResolveBuiltInPack(packKey, out var builtInPack))
            return builtInPack;

        if (!options.AllowFileSystemSkinPacks)
            return null;

        var source = ResolveFileSystemSource(options.SkinPack, options.SkinPackBaseDirectory);
        if (source is null)
            return null;

        return FilePackCache.GetOrAdd(source, LoadFileSystemPack);
    }

    static bool TryResolveRegisteredPack(string packKey, out ShapeSkinPack? pack)
    {
        pack = null;
        lock (PluginSync)
        {
            if (!RegisteredPackAliases.TryGetValue(packKey, out var canonicalName))
                return false;

            if (!RegisteredPacks.TryGetValue(canonicalName, out var entry))
                return false;

            pack = entry.Pack;
            return true;
        }
    }

    static bool TryResolveBuiltInPack(string packKey, out ShapeSkinPack? pack)
    {
        pack = null;

        if (!BuiltInPackAliases.TryGetValue(packKey, out var canonicalPackKey))
            return false;

        if (canonicalPackKey.Equals("default", StringComparison.Ordinal))
        {
            pack = DefaultPack;
            return true;
        }

        pack = BuiltInPackCache.GetOrAdd(canonicalPackKey, LoadBuiltInPack);
        return true;
    }

    static ShapeSkinPack? LoadBuiltInPack(string packKey)
    {
        if (EmbeddedArchiveResources.TryGetValue(packKey, out var resourceName))
        {
            var embeddedPack = LoadPackFromEmbeddedResource(resourceName, packKey);
            if (embeddedPack is not null)
                return embeddedPack;
        }

        // Graceful fallback in case embedded resources are unavailable for legacy pack names.
        return packKey switch
        {
            "daisyui" => CreateDaisyUiPack(),
            "material" => CreateMaterialPack(),
            _ => null
        };
    }

    static ShapeSkinPack? LoadPackFromEmbeddedResource(string resourceName, string packName)
    {
        try
        {
            var assembly = typeof(ShapeSkinCatalog).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;

            return LoadPackFromArchiveStream(stream, packName);
        }
        catch
        {
            return null;
        }
    }

    static string? ResolveFileSystemSource(string source, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var resolved = Path.IsPathRooted(source)
            ? Path.GetFullPath(source)
            : Path.GetFullPath(Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), source));

        return resolved;
    }

    static ShapeSkinPack? LoadFileSystemPack(string source)
    {
        try
        {
            if (Directory.Exists(source))
                return LoadPackFromDirectory(source);

            if (File.Exists(source))
                return LoadPackFromArchive(source);
        }
        catch
        {
            // Invalid pack source; caller will fall back to default geometry.
        }

        return null;
    }

    static ShapeSkinPack? LoadPackFromDirectory(string directoryPath)
    {
        var shapes = new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.svg", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directoryPath, file);
            var key = NormalizeKey(Path.ChangeExtension(relative, null)!.Replace('\\', '.').Replace('/', '.'));
            var content = File.ReadAllText(file);
            if (TryParseTemplate(content, out var template))
                shapes[key] = NormalizeTemplateForShapeKey(key, template);
        }

        return shapes.Count == 0
            ? null
            : new ShapeSkinPack(Path.GetFileName(directoryPath), shapes);
    }

    static ShapeSkinPack? LoadPackFromArchive(string archivePath)
    {
        var extension = Path.GetExtension(archivePath);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".naiadskin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var stream = File.OpenRead(archivePath);
        return LoadPackFromArchiveStream(stream, Path.GetFileNameWithoutExtension(archivePath));
    }

    static ShapeSkinPack? LoadPackFromArchiveStream(Stream stream, string packName)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var shapes = new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) ||
                !entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            var content = reader.ReadToEnd();
            var key = NormalizeKey(Path.ChangeExtension(entry.FullName, null)!.Replace('\\', '.').Replace('/', '.'));

            if (TryParseTemplate(content, out var template))
                shapes[key] = NormalizeTemplateForShapeKey(key, template);
        }

        return shapes.Count == 0
            ? null
            : new ShapeSkinPack(packName, shapes);
    }

    static bool TryParseTemplate(string svgContent, out ShapeSkinTemplate template)
    {
        template = default;
        if (string.IsNullOrWhiteSpace(svgContent))
            return false;

        var bodyContent = StripDefsBlocks(svgContent);
        var layers = ParsePathLayers(bodyContent);
        if (layers.Count == 0)
            return false;

        var defsContent = ExtractDefsContent(svgContent);
        var viewBox = ParseViewBox(svgContent);
        var primaryLayer = layers[0];
        template = new(
            PathData: primaryLayer.PathData,
            ViewBoxX: viewBox.x,
            ViewBoxY: viewBox.y,
            ViewBoxWidth: viewBox.w,
            ViewBoxHeight: viewBox.h,
            InlineStyle: primaryLayer.InlineStyle,
            Layers: layers,
            DefsContent: defsContent);
        return true;
    }

    static string StripDefsBlocks(string svgContent) =>
        DefsTagPattern.Replace(svgContent, string.Empty);

    static string? ExtractDefsContent(string svgContent)
    {
        var fragments = new List<string>();
        foreach (Match match in DefsTagPattern.Matches(svgContent))
        {
            var content = match.Groups["content"].Value;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            fragments.Add(content.Trim());
        }

        if (fragments.Count == 0)
            return null;

        return SanitizeDefsContent(string.Join(string.Empty, fragments));
    }

    static List<ShapeSkinPathLayer> ParsePathLayers(string svgContent)
    {
        var layers = new List<ShapeSkinPathLayer>();
        foreach (Match match in PathTagPattern.Matches(svgContent))
        {
            var pathAttributes = ParsePathAttributes(match.Groups["attrs"].Value);
            if (!pathAttributes.TryGetValue("d", out var pathData))
                continue;

            pathData = pathData.Trim();
            if (string.IsNullOrEmpty(pathData))
                continue;

            layers.Add(new ShapeSkinPathLayer(
                PathData: System.Net.WebUtility.HtmlDecode(pathData),
                InlineStyle: BuildInlineStyle(pathAttributes)));
        }

        return layers;
    }

    static Dictionary<string, string> ParsePathAttributes(string attributeSegment)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PathAttributePattern.Matches(attributeSegment))
        {
            var name = match.Groups["name"].Value.Trim();
            var value = System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value);
            if (string.IsNullOrEmpty(name))
                continue;

            attributes[name] = value;
        }

        return attributes;
    }

    static string? BuildInlineStyle(IReadOnlyDictionary<string, string> attributes)
    {
        var declarations = new List<string>(11);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (attributes.TryGetValue("style", out var rawStyle) &&
            !string.IsNullOrWhiteSpace(rawStyle))
        {
            foreach (var declaration in rawStyle.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = declaration.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex >= declaration.Length - 1)
                    continue;

                var property = declaration[..separatorIndex].Trim();
                var value = declaration[(separatorIndex + 1)..].Trim();
                if (!IsAllowedStyleProperty(property))
                    continue;

                var safeValue = SanitizeStyleValue(value);
                if (safeValue is null)
                    continue;

                declarations.Add($"{property}:{safeValue}");
                seen.Add(property);
            }
        }

        AddAttributeStyle("fill", "fill");
        AddAttributeStyle("stroke", "stroke");
        AddAttributeStyle("stroke-width", "stroke-width");
        AddAttributeStyle("stroke-dasharray", "stroke-dasharray");
        AddAttributeStyle("opacity", "opacity");
        AddAttributeStyle("fill-opacity", "fill-opacity");
        AddAttributeStyle("stroke-opacity", "stroke-opacity");
        AddAttributeStyle("stroke-linejoin", "stroke-linejoin");
        AddAttributeStyle("stroke-linecap", "stroke-linecap");
        AddAttributeStyle("filter", "filter");
        AddAttributeStyle("clip-path", "clip-path");

        return declarations.Count == 0
            ? null
            : SecurityValidator.SanitizeCss(string.Join(";", declarations));

        void AddAttributeStyle(string attributeName, string cssProperty)
        {
            if (seen.Contains(cssProperty))
                return;
            if (!attributes.TryGetValue(attributeName, out var value))
                return;

            var safeValue = SanitizeStyleValue(value);
            if (safeValue is null)
                return;

            declarations.Add($"{cssProperty}:{safeValue}");
            seen.Add(cssProperty);
        }
    }

    static bool IsAllowedStyleProperty(string property) =>
        property.Equals("fill", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke-width", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("opacity", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke-opacity", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke-linejoin", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("stroke-linecap", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("filter", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("clip-path", StringComparison.OrdinalIgnoreCase);

    static string? SanitizeStyleValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(['<', '>', ';']) >= 0)
            return null;

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("javascript:", StringComparison.Ordinal) ||
            lower.Contains("expression", StringComparison.Ordinal))
        {
            return null;
        }

        var urlIndex = lower.IndexOf("url(", StringComparison.Ordinal);
        if (urlIndex >= 0)
        {
            if (!IsSafeFragmentReference(trimmed))
                return null;
        }

        return trimmed;
    }

    static bool IsSafeFragmentReference(string value)
    {
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        return Regex.IsMatch(
            normalized,
            @"^url\(#[-a-zA-Z0-9_:.]+\)$",
            RegexOptions.CultureInvariant);
    }

    static List<ShapeSkinPathLayer> NormalizePluginLayers(SkinShapeTemplate template)
    {
        var layers = new List<ShapeSkinPathLayer>();
        if (template.Layers is { Count: > 0 })
        {
            foreach (var layer in template.Layers)
            {
                var pathData = layer.PathData?.Trim();
                if (string.IsNullOrWhiteSpace(pathData))
                    continue;

                layers.Add(new ShapeSkinPathLayer(pathData, SanitizeInlineStyle(layer.InlineStyle)));
            }
        }

        if (layers.Count == 0 && !string.IsNullOrWhiteSpace(template.PathData))
        {
            layers.Add(new ShapeSkinPathLayer(
                template.PathData.Trim(),
                SanitizeInlineStyle(template.InlineStyle)));
        }

        return layers;
    }

    static string? SanitizeInlineStyle(string? rawStyle)
    {
        if (string.IsNullOrWhiteSpace(rawStyle))
            return null;

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["style"] = rawStyle
        };

        return BuildInlineStyle(attributes);
    }

    static string? SanitizeDefsContent(string? defsContent)
    {
        if (string.IsNullOrWhiteSpace(defsContent))
            return null;

        var trimmed = defsContent.Trim();
        if (Regex.IsMatch(trimmed, @"<\s*/?\s*(script|foreignObject|iframe|object|embed)\b", RegexOptions.IgnoreCase))
            return null;
        if (Regex.IsMatch(trimmed, @"\bon\w+\s*=", RegexOptions.IgnoreCase))
            return null;
        if (trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (trimmed.Contains("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }

    static ShapeSkinTemplate NormalizeTemplateForShapeKey(string shapeKey, ShapeSkinTemplate template)
    {
        var layers = (template.Layers is { Count: > 0 }
            ? template.Layers
            : [new ShapeSkinPathLayer(template.PathData, template.InlineStyle)])
            .Where(x => !string.IsNullOrWhiteSpace(x.PathData))
            .ToList();

        if (layers.Count == 0)
            return template;

        var defs = SanitizeDefsContent(template.DefsContent);
        if (!string.IsNullOrWhiteSpace(defs))
        {
            (defs, layers) = NamespaceDefsAndLayerStyles(shapeKey, defs!, layers);
        }

        return template with
        {
            PathData = layers[0].PathData,
            InlineStyle = layers[0].InlineStyle,
            Layers = layers,
            DefsContent = defs
        };
    }

    static (string defsContent, List<ShapeSkinPathLayer> layers) NamespaceDefsAndLayerStyles(
        string shapeKey,
        string defsContent,
        List<ShapeSkinPathLayer> layers)
    {
        var idPattern = new Regex(
            @"\bid\s*=\s*([""'])(?<id>[-a-zA-Z0-9_:.]+)\1",
            RegexOptions.IgnoreCase | RegexCompat.Compiled);

        var shapePrefix = Regex.Replace(shapeKey, @"[^-a-zA-Z0-9_:.]+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrEmpty(shapePrefix))
            shapePrefix = "shape";

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in idPattern.Matches(defsContent))
        {
            var oldId = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(oldId) || map.ContainsKey(oldId))
                continue;

            map[oldId] = $"skin-{shapePrefix}-{oldId}";
        }

        if (map.Count == 0)
            return (defsContent, layers);

        var rewrittenDefs = defsContent;
        foreach (var (oldId, newId) in map)
        {
            var idLiteralPattern = $@"\bid\s*=\s*([""']){Regex.Escape(oldId)}\1";
            rewrittenDefs = Regex.Replace(
                rewrittenDefs,
                idLiteralPattern,
                m => $"id={m.Groups[1].Value}{newId}{m.Groups[1].Value}",
                RegexOptions.IgnoreCase);
            rewrittenDefs = ReplaceFragmentReference(rewrittenDefs, oldId, newId);
            rewrittenDefs = ReplaceHrefReference(rewrittenDefs, oldId, newId);
        }

        var rewrittenLayers = new List<ShapeSkinPathLayer>(layers.Count);
        foreach (var layer in layers)
        {
            var style = layer.InlineStyle;
            if (!string.IsNullOrWhiteSpace(style))
            {
                foreach (var (oldId, newId) in map)
                    style = ReplaceFragmentReference(style!, oldId, newId);
            }

            rewrittenLayers.Add(layer with { InlineStyle = style });
        }

        return (rewrittenDefs, rewrittenLayers);
    }

    static string ReplaceFragmentReference(string input, string oldId, string newId) =>
        Regex.Replace(
            input,
            $@"url\(\s*#\s*{Regex.Escape(oldId)}\s*\)",
            $"url(#{newId})",
            RegexOptions.IgnoreCase);

    static string ReplaceHrefReference(string input, string oldId, string newId) =>
        Regex.Replace(
            input,
            $@"(?<attr>xlink:href|href)\s*=\s*([""'])\s*#\s*{Regex.Escape(oldId)}\s*\2",
            m => $"{m.Groups["attr"].Value}={m.Groups[2].Value}#{newId}{m.Groups[2].Value}",
            RegexOptions.IgnoreCase);

    static (double x, double y, double w, double h) ParseViewBox(string svgContent)
    {
        var viewBox = ViewBoxPattern.Match(svgContent);
        if (viewBox.Success &&
            TryParseDouble(viewBox.Groups["x"].Value, out var x) &&
            TryParseDouble(viewBox.Groups["y"].Value, out var y) &&
            TryParseDouble(viewBox.Groups["w"].Value, out var w) &&
            TryParseDouble(viewBox.Groups["h"].Value, out var h) &&
            w > 0 && h > 0)
        {
            return (x, y, w, h);
        }

        var width = 100d;
        var height = 100d;

        var widthMatch = WidthPattern.Match(svgContent);
        if (widthMatch.Success && TryParseDouble(widthMatch.Groups["w"].Value, out var parsedWidth) && parsedWidth > 0)
            width = parsedWidth;

        var heightMatch = HeightPattern.Match(svgContent);
        if (heightMatch.Success && TryParseDouble(heightMatch.Groups["h"].Value, out var parsedHeight) && parsedHeight > 0)
            height = parsedHeight;

        return (0, 0, width, height);
    }

    static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    static string NormalizeKey(string value)
    {
        var key = value.Trim().ToLowerInvariant()
            .Replace('\\', '.')
            .Replace('/', '.')
            .Replace(' ', '-')
            .Replace('_', '-');

        while (key.Contains("..", StringComparison.Ordinal))
            key = key.Replace("..", ".", StringComparison.Ordinal);
        while (key.Contains("--", StringComparison.Ordinal))
            key = key.Replace("--", "-", StringComparison.Ordinal);

        return key.Trim('.');
    }

    static void AddAlias(string alias, string canonicalName) => RegisteredPackAliases[alias] = canonicalName;

    static void RemoveAliases(string canonicalName)
    {
        var aliases = RegisteredPackAliases
            .Where(x => x.Value.Equals(canonicalName, StringComparison.Ordinal))
            .Select(x => x.Key)
            .ToArray();

        foreach (var alias in aliases)
            RegisteredPackAliases.Remove(alias);
    }

    static ShapeSkinPack CreateDaisyUiPack()
    {
        const double vbWidth = 100;
        const double vbHeight = 60;
        var shapes = new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal)
        {
            ["rounded-rectangle"] = new("M14 0H86Q100 0 100 14V46Q100 60 86 60H14Q0 60 0 46V14Q0 0 14 0Z", 0, 0, vbWidth, vbHeight),
            ["rectangle"] = new("M10 0H90Q100 0 100 10V50Q100 60 90 60H10Q0 60 0 50V10Q0 0 10 0Z", 0, 0, vbWidth, vbHeight),
            ["stadium"] = new("M30 0H70Q100 0 100 30Q100 60 70 60H30Q0 60 0 30Q0 0 30 0Z", 0, 0, vbWidth, vbHeight),
            ["diamond"] = new("M50 0L100 30L50 60L0 30Z", 0, 0, vbWidth, vbHeight),
            ["hexagon"] = new("M18 0H82L100 30L82 60H18L0 30Z", 0, 0, vbWidth, vbHeight),
            ["cylinder"] = new("M0 8A50 8 0 0 1 100 8V52A50 8 0 0 1 0 52V8ZM0 8A50 8 0 0 0 100 8", 0, 0, vbWidth, vbHeight),
            ["circle"] = new("M50 0A30 30 0 1 1 50 60A30 30 0 1 1 50 0Z", 0, 0, vbWidth, vbHeight)
        };

        return new ShapeSkinPack("daisyui", shapes);
    }

    static ShapeSkinPack CreateMaterialPack()
    {
        const double vbWidth = 100;
        const double vbHeight = 60;
        var shapes = new Dictionary<string, ShapeSkinTemplate>(StringComparer.Ordinal)
        {
            ["rectangle"] = new("M4 0H96Q100 0 100 4V56Q100 60 96 60H4Q0 60 0 56V4Q0 0 4 0Z", 0, 0, vbWidth, vbHeight),
            ["rounded-rectangle"] = new("M8 0H92Q100 0 100 8V52Q100 60 92 60H8Q0 60 0 52V8Q0 0 8 0Z", 0, 0, vbWidth, vbHeight),
            ["stadium"] = new("M26 0H74Q100 0 100 30Q100 60 74 60H26Q0 60 0 30Q0 0 26 0Z", 0, 0, vbWidth, vbHeight),
            ["diamond"] = new("M50 0L100 30L50 60L0 30Z", 0, 0, vbWidth, vbHeight),
            ["hexagon"] = new("M16 0H84L100 30L84 60H16L0 30Z", 0, 0, vbWidth, vbHeight),
            ["cylinder"] = new("M0 7A50 7 0 0 1 100 7V53A50 7 0 0 1 0 53V7ZM0 7A50 7 0 0 0 100 7", 0, 0, vbWidth, vbHeight),
            ["circle"] = new("M50 0A30 30 0 1 1 50 60A30 30 0 1 1 50 0Z", 0, 0, vbWidth, vbHeight)
        };

        return new ShapeSkinPack("material", shapes);
    }
}
