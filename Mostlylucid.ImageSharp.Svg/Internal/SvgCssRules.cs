using System.Collections.Generic;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Minimal CSS rule store. Parses <c>&lt;style&gt;</c> block contents into a
/// per-class property dictionary, ignoring everything that isn't a simple
/// class selector. This is enough for mermaid output, which scopes all of
/// its presentation under a small handful of CSS classes.
/// </summary>
/// <remarks>
/// We deliberately do <b>not</b> implement a full CSS parser. Specifically,
/// the following are intentionally out of scope:
/// <list type="bullet">
///   <item>Cascade specificity (everything is treated as one bucket).</item>
///   <item>Pseudo-classes, attribute selectors, descendant combinators.</item>
///   <item>@-rules, media queries, keyframes.</item>
///   <item>Variables, calc(), filter functions.</item>
/// </list>
/// Selectors are reduced to their <i>last</i> class name token, so
/// <c>#mermaid-svg .flow-node-shape</c> becomes a lookup on
/// <c>flow-node-shape</c>. Mermaid's CSS rules are flat enough that this
/// gives the right answer for every selector it emits.
/// </remarks>
internal sealed class SvgCssRules
{
    private readonly Dictionary<string, Dictionary<string, string>> _byClass =
        new(StringComparer.Ordinal);

    public bool IsEmpty => _byClass.Count == 0;

    /// <summary>
    /// Look up the merged property bag for a class name. Returns
    /// <c>null</c> if no rules target that class.
    /// </summary>
    public IReadOnlyDictionary<string, string>? GetClassProperties(string className)
        => _byClass.TryGetValue(className, out var props) ? props : null;

    /// <summary>
    /// Append the contents of a single &lt;style&gt; block. Multiple style
    /// blocks accumulate into the same store.
    /// </summary>
    public void Parse(string css)
    {
        if (string.IsNullOrWhiteSpace(css)) return;

        var span = css.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            // Skip whitespace and CSS comments.
            i = SkipNoise(span, i);
            if (i >= span.Length) break;

            // Read selector list (everything up to '{').
            var selStart = i;
            while (i < span.Length && span[i] != '{') i++;
            if (i >= span.Length) break;
            var selectorList = span[selStart..i].ToString();
            i++; // consume '{'

            // Read declaration block (everything up to matching '}').
            var declStart = i;
            while (i < span.Length && span[i] != '}') i++;
            if (i > span.Length) break;
            var declBlock = span[declStart..Math.Min(i, span.Length)].ToString();
            if (i < span.Length) i++; // consume '}'

            var props = ParseDeclarations(declBlock);
            if (props.Count == 0) continue;

            foreach (var rawSelector in selectorList.Split(','))
            {
                var className = ExtractTrailingClass(rawSelector);
                if (className == null) continue;
                if (!_byClass.TryGetValue(className, out var bucket))
                {
                    bucket = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _byClass[className] = bucket;
                }
                foreach (var (k, v) in props)
                    bucket[k] = v;
            }
        }
    }

    private static int SkipNoise(ReadOnlySpan<char> span, int i)
    {
        while (i < span.Length)
        {
            if (char.IsWhiteSpace(span[i])) { i++; continue; }
            // CSS /* comment */
            if (i + 1 < span.Length && span[i] == '/' && span[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < span.Length && !(span[i] == '*' && span[i + 1] == '/'))
                    i++;
                if (i + 1 < span.Length) i += 2;
                continue;
            }
            break;
        }
        return i;
    }

    private static Dictionary<string, string> ParseDeclarations(string block)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in block.Split(';'))
        {
            var trimmed = raw.AsSpan().Trim();
            if (trimmed.IsEmpty) continue;
            var idx = trimmed.IndexOf(':');
            if (idx <= 0 || idx >= trimmed.Length - 1) continue;
            var key = trimmed[..idx].Trim().ToString();
            var value = trimmed[(idx + 1)..].Trim().ToString();
            // Strip CSS comments inside values.
            var commentIdx = value.IndexOf("/*", StringComparison.Ordinal);
            if (commentIdx >= 0) value = value[..commentIdx].Trim();
            if (key.Length == 0 || value.Length == 0) continue;
            dict[key] = value;
        }
        return dict;
    }

    /// <summary>
    /// Reduce a selector chain like <c>#mermaid-svg .flow-node-shape</c> to
    /// its trailing class token (<c>flow-node-shape</c>). Returns null when
    /// the selector doesn't end with a class.
    /// </summary>
    private static string? ExtractTrailingClass(string selector)
    {
        var s = selector.Trim();
        if (s.Length == 0) return null;

        // Walk back from the end to the last whitespace or '>' (descendant /
        // child combinator boundary), then take everything from the last '.'
        // forward as the class name.
        var i = s.Length - 1;
        var lastBoundary = -1;
        while (i >= 0)
        {
            if (s[i] == ' ' || s[i] == '\t' || s[i] == '>' || s[i] == '+' || s[i] == '~')
            {
                lastBoundary = i;
                break;
            }
            i--;
        }

        var token = lastBoundary >= 0 ? s[(lastBoundary + 1)..] : s;
        var dotIdx = token.IndexOf('.');
        if (dotIdx < 0) return null;
        var className = token[(dotIdx + 1)..];
        // Drop trailing pseudo-classes / attribute clauses we don't support.
        var stopIdx = className.IndexOfAny([':', '[', '.', '#']);
        if (stopIdx >= 0) className = className[..stopIdx];
        return className.Length > 0 ? className : null;
    }
}
