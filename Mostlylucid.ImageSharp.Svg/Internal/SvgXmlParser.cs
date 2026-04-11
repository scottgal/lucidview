using System.IO;
using System.Xml;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Hand-rolled SVG parser built on <see cref="XmlReader"/>. Produces an
/// <see cref="SvgNode"/> tree without using XML serializers, reflection, or
/// type descriptors — so the result is fully AOT/trim safe.
/// </summary>
/// <remarks>
/// We deliberately ignore xml namespaces (every &lt;svg&gt; element we care
/// about is in the SVG namespace anyway) and DTDs. The parser strips
/// comments, processing instructions, and CDATA wrappers — only element
/// hierarchy + attributes + inline text are preserved.
/// </remarks>
internal static class SvgXmlParser
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
        XmlResolver = null,
    };

    public static SvgNode Parse(string svgXml)
    {
        using var stringReader = new StringReader(svgXml);
        using var xmlReader = XmlReader.Create(stringReader, ReaderSettings);
        return Read(xmlReader);
    }

    public static SvgNode Parse(Stream svgStream)
    {
        using var xmlReader = XmlReader.Create(svgStream, ReaderSettings);
        return Read(xmlReader);
    }

    private static SvgNode Read(XmlReader reader)
    {
        SvgNode? root = null;
        var stack = new Stack<SvgNode>();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var node = new SvgNode(reader.LocalName);
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                        {
                            // Strip namespace prefixes — we don't need them
                            // for rendering and they make lookups awkward.
                            node.Attributes[reader.LocalName] = reader.Value;
                        }
                        reader.MoveToElement();
                    }

                    if (stack.Count == 0)
                        root = node;
                    else
                        stack.Peek().Children.Add(node);

                    if (!reader.IsEmptyElement)
                        stack.Push(node);
                    break;
                }

                case XmlNodeType.EndElement:
                    if (stack.Count > 0)
                        stack.Pop();
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    if (stack.Count > 0)
                    {
                        var top = stack.Peek();
                        top.Text = top.Text is null
                            ? reader.Value
                            : top.Text + reader.Value;
                    }
                    break;
            }
        }

        return root ?? throw new InvalidDataException("SVG document had no root element.");
    }
}
