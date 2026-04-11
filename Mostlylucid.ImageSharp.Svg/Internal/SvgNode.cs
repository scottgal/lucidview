using System.Collections.Generic;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Minimal SVG element AST. Each node owns the literal attribute values
/// (strings) parsed from XML; numeric coercion happens at draw time. Keeping
/// the model attribute-bag-shaped lets us add support for new elements
/// without redesigning the tree.
/// </summary>
internal sealed class SvgNode
{
    public string Name { get; }
    public Dictionary<string, string> Attributes { get; } =
        new(StringComparer.Ordinal);
    public List<SvgNode> Children { get; } = new();
    public string? Text { get; set; }

    public SvgNode(string name)
    {
        Name = name;
    }

    public string? Get(string attr)
        => Attributes.TryGetValue(attr, out var v) ? v : null;
}
