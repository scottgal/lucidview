using System.Text;
using System.Security.Cryptography;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkdownViewer.Services;

public readonly record struct MarkdownFilePage(long FirstLine, int LineCount, bool SourceOnly);

/// <summary>
/// Uses Markdig's streaming block processor to choose complete top-level blocks for each page.
/// Oversized blocks keep their parser state but are displayed as bounded source fragments.
/// </summary>
public sealed class MarkdownFileIndex
{
    public const int TargetPageLines = 120;
    private const int MaxPageLines = 512;
    private const int MaxPageCharacters = 256 * 1024;
    private const int MaxReferenceCharacters = 256 * 1024;
    private static readonly MarkdownPipeline HeadingPipeline = CreateHeadingPipeline();
    private readonly List<(UInt128 Hash, long Line)> _headings = [];
    private readonly List<MarkdownFilePage> _pages = [];
    private readonly Dictionary<string, string> _references = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MarkdownFilePage> Pages => _pages;
    public string ReferenceContext { get; private set; } = "";
    public bool ReferencesLimited { get; private set; }
    public long HeadingIndexBytes => _headings.Count * 32L;

    public static async Task<MarkdownFileIndex> CreateAsync(IndexedTextFile file,
        CancellationToken cancellationToken = default, IProgress<long>? progress = null)
    {
        var result = new MarkdownFileIndex();
        var document = new MarkdownDocument();
        long firstLine = 0;
        long parserFirstLine = 0;
        // Fixed-size fingerprints avoid keeping every heading string and inline tree in memory.
        var identifiers = new HashSet<UInt128>();
        var nextSuffix = new Dictionary<UInt128, long>();
        var parsers = CreateParsers((_, block) =>
        {
            if (block is HeadingBlock heading) result.AddHeading(heading, parserFirstLine, identifiers, nextSuffix);
        });
        var processor = new BlockProcessor(document, parsers, null);
        var characters = 0;
        var sourceOnly = false;
        Block? oversizedBlock = null;
        var referenceCharacters = 0;

        void CollectReferences()
        {
            foreach (var (label, definition) in document.GetLinkReferenceDefinitions(false).Links)
            {
                if (result._references.ContainsKey(label)) continue;
                var title = string.IsNullOrEmpty(definition.Title) ? "" : " \"" + definition.Title.Replace("\"", "&quot;") + "\"";
                var reference = $"[{label}]: <{definition.Url?.Replace(">", "%3E")}>{title}";
                if (referenceCharacters + reference.Length > MaxReferenceCharacters)
                {
                    result.ReferencesLimited = true;
                    continue;
                }
                result._references.Add(label, reference);
                referenceCharacters += reference.Length;
            }
        }

        void AddPage(long endLine, bool literal)
        {
            if (endLine <= firstLine) return;
            result._pages.Add(new MarkdownFilePage(firstLine, checked((int)(endLine - firstLine)), literal));
            firstLine = endLine;
            characters = 0;
            sourceOnly = false;
            CollectReferences();
            document.GetLinkReferenceDefinitions(false).Links.Clear();
        }

        await foreach (var line in file.ReadLineRecordsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Markdig uses int line numbers. Restart only at complete top-level boundaries.
            processor.ProcessLine(new StringSlice(line.Text));
            var root = document.LastChild;
            if (root is LinkReferenceDefinitionGroup)
                root = document.Count > 1 ? document[document.Count - 2] : null;
            var rootLine = root is null ? line.Number : parserFirstLine + root.Line;

            if (oversizedBlock is not null && !ReferenceEquals(root, oversizedBlock))
            {
                // Finish the literal tail before starting a normal block on the same input line.
                AddPage(rootLine, true);
                oversizedBlock = null;
            }

            if (root is not null && rootLine > firstLine
                && (line.Number - firstLine >= TargetPageLines || characters + line.Text.Length > MaxPageCharacters))
                AddPage(rootLine, sourceOnly);

            characters += line.Text.Length + 1;
            sourceOnly |= line.IsTruncated;

            var rootIsActive = false;
            for (var active = processor.CurrentBlock; active is not null; active = active.Parent)
                if (ReferenceEquals(active, root)) { rootIsActive = true; break; }
            if (line.Number - firstLine + 1 >= TargetPageLines && root is not null && !rootIsActive)
            {
                AddPage(line.Number + 1, sourceOnly || oversizedBlock is not null);
                oversizedBlock = null;
                document = new MarkdownDocument();
                processor = new BlockProcessor(document, parsers, null);
                parserFirstLine = line.Number + 1;
            }
            else if (line.Number - firstLine + 1 >= MaxPageLines || characters >= MaxPageCharacters)
            {
                AddPage(line.Number + 1, true);
                oversizedBlock = root;
            }

            // Keep only the current top-level block. Its open stack retains the syntax state.
            while (document.Count > 1) document.RemoveAt(0);
            if (oversizedBlock is not null) TrimOversizedBlock(oversizedBlock);
            if (line.Number % 8192 == 0) progress?.Report(line.Number);
        }
        processor.Close(document);
        CollectReferences();
        AddPage(file.LineCount, sourceOnly || oversizedBlock is not null);
        result.ReferenceContext = string.Join('\n', result._references.Values);
        result._references.Clear();
        result._headings.Sort((a, b) =>
        {
            var compare = a.Hash.CompareTo(b.Hash);
            return compare == 0 ? a.Line.CompareTo(b.Line) : compare;
        });
        result._headings.TrimExcess();
        result._pages.TrimExcess();
        // The parser callbacks capture these sets; release their backing arrays before returning.
        identifiers.Clear();
        identifiers.TrimExcess();
        nextSuffix.Clear();
        nextSuffix.TrimExcess();
        progress?.Report(file.LineCount);
        return result;
    }

    public long? FindHeading(string identifier)
    {
        var hash = Fingerprint(identifier);
        var low = 0;
        var high = _headings.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_headings[middle].Hash < hash) low = middle + 1;
            else high = middle;
        }
        return low < _headings.Count && _headings[low].Hash == hash ? _headings[low].Line : null;
    }

    private void AddHeading(HeadingBlock heading, long parserFirstLine, HashSet<UInt128> identifiers,
        Dictionary<UInt128, long> nextSuffix)
    {
        var content = string.Join('\n', Enumerable.Range(0, heading.Lines.Count)
            .Select(i => heading.Lines.Lines[i].ToString()));
        var markdown = heading.IsSetext ? content + "\n======" : "# " + content;
        var parsed = Markdown.Parse(markdown, HeadingPipeline).OfType<HeadingBlock>().FirstOrDefault();
        if (parsed?.Inline is null) return;
        var id = parsed.TryGetAttributes()?.Id;
        if (id is null)
        {
            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer) { EnableHtmlForInline = false, EnableHtmlEscape = false };
            renderer.Render(parsed.Inline);
            var baseId = LinkHelper.Urilize(writer.ToString(), true);
            if (baseId.Length == 0) baseId = "section";
            id = baseId;
            var baseHash = Fingerprint(id);
            if (!identifiers.Add(baseHash))
            {
                var suffix = nextSuffix.GetValueOrDefault(baseHash, 1);
                do { id = baseId + "-" + suffix++; } while (!identifiers.Add(Fingerprint(id)));
                nextSuffix[baseHash] = suffix;
            }
        }
        _headings.Add((Fingerprint(id), parserFirstLine + heading.Line));
    }

    private static UInt128 Fingerprint(string identifier)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(identifier), hash);
        return (UInt128)BitConverter.ToUInt64(hash[..8]) << 64 | BitConverter.ToUInt64(hash[8..16]);
    }

    private static MarkdownPipeline CreateHeadingPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions();
        var autoIdentifiers = builder.Extensions.OfType<AutoIdentifierExtension>().Single();
        builder.Extensions.Remove(autoIdentifiers);
        return builder.Build();
    }

    public MarkdownFilePage FindPage(long line)
    {
        var low = 0;
        var high = _pages.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (_pages[middle].FirstLine <= line) low = middle;
            else high = middle - 1;
        }
        return _pages[low];
    }

    private static void TrimOversizedBlock(Block block)
    {
        if (block is LeafBlock leaf)
        {
            // Retain the last line for setext/table recognition without keeping the giant block.
            while (leaf.Lines.Count > 1) leaf.Lines.RemoveAt(0);
        }
        else if (block is ContainerBlock container)
        {
            while (container.Count > 1) container.RemoveAt(0);
            if (container.LastChild is { } child) TrimOversizedBlock(child);
        }
    }

    private static BlockParserList CreateParsers(Action<BlockProcessor, Block> headingClosed)
    {
        // Block recognition needs these extensions; auto identifiers and inline transforms
        // would retain document-wide state that is unnecessary for locating page boundaries.
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseGridTables()
            .UseDefinitionLists().UseCustomContainers().UseMathematics();
        builder.Build(); // Extensions install their block parsers while building.
        builder.BlockParsers.Find<HeadingBlockParser>()!.Closed += (processor, block) => headingClosed(processor, block);
        builder.BlockParsers.FindExact<ParagraphBlockParser>()!.Closed += (processor, block) => headingClosed(processor, block);
        return new BlockParserList(builder.BlockParsers);
    }
}
