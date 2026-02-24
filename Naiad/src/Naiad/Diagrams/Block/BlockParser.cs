namespace MermaidSharp.Diagrams.Block;

public class BlockParser : IDiagramParser<BlockModel>
{
    public DiagramType DiagramType => DiagramType.Block;

    // Identifier
    static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').AtLeastOnceString();

    // Label content (text inside shape brackets)
    static readonly Parser<char, string> LabelContent =
        Token(c => c != '"' && c != ']' && c != ')' && c != '}').ManyString();

    // Quoted label
    static readonly Parser<char, string> QuotedLabel =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    // Columns: columns N
    static readonly Parser<char, int> ColumnsParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("columns")
        from ___ in CommonParsers.RequiredWhitespace
        from num in Digit.AtLeastOnceString().Select(int.Parse)
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select num;

    // Rectangle shape: ["label"] or [label]
    static readonly Parser<char, (string label, BlockShape shape)> RectangleShape =
        from _ in Char('[')
        from label in QuotedLabel.Or(LabelContent)
        from __ in Char(']')
        select (label.Trim(), BlockShape.Rectangle);

    // Rounded shape: ("label") or (label)
    static readonly Parser<char, (string label, BlockShape shape)> RoundedShape =
        from _ in Char('(')
        from label in QuotedLabel.Or(Token(c => c != ')').ManyString())
        from __ in Char(')')
        select (label.Trim(), BlockShape.Rounded);

    // Stadium shape: (["label"]) or ([label])
    static readonly Parser<char, (string label, BlockShape shape)> StadiumShape =
        from _ in String("([")
        from label in QuotedLabel.Or(Token(c => c != ']').ManyString())
        from __ in String("])")
        select (label.Trim(), BlockShape.Stadium);

    // Circle shape: (("label")) or ((label))
    static readonly Parser<char, (string label, BlockShape shape)> CircleShape =
        from _ in String("((")
        from label in QuotedLabel.Or(Token(c => c != ')').ManyString())
        from __ in String("))")
        select (label.Trim(), BlockShape.Circle);

    // Diamond shape: {"label"} or {label}
    static readonly Parser<char, (string label, BlockShape shape)> DiamondShape =
        from _ in Char('{')
        from notDouble in Lookahead(AnyCharExcept('{'))
        from label in QuotedLabel.Or(Token(c => c != '}').ManyString())
        from __ in Char('}')
        select (label.Trim(), BlockShape.Diamond);

    // Hexagon shape: {{"label"}} or {{label}}
    static readonly Parser<char, (string label, BlockShape shape)> HexagonShape =
        from _ in String("{{")
        from label in QuotedLabel.Or(Token(c => c != '}').ManyString())
        from __ in String("}}")
        select (label.Trim(), BlockShape.Hexagon);

    // Shape parser (order matters - more specific first)
    static readonly Parser<char, (string label, BlockShape shape)> ShapeParser =
        OneOf(
            Try(StadiumShape),
            Try(CircleShape),
            Try(HexagonShape),
            Try(DiamondShape),
            Try(RoundedShape),
            Try(RectangleShape)
        );

    // Span: :N
    static readonly Parser<char, int> SpanParser =
        from _ in Char(':')
        from num in Digit.AtLeastOnceString().Select(int.Parse)
        select num;

    // Block element: id["label"]:2
    static readonly Parser<char, BlockElement> ElementParser =
        from id in Identifier
        from shape in ShapeParser.Optional()
        from span in SpanParser.Optional()
        select new BlockElement
        {
            Id = id,
            Label = shape.HasValue ? shape.Value.label : id,
            Shape = shape.HasValue ? shape.Value.shape : BlockShape.Rectangle,
            Span = span.GetValueOrDefault(1)
        };

    // Inline required whitespace (spaces/tabs only, not newlines)
    static readonly Parser<char, Unit> InlineRequiredWhitespace =
        Token(c => c is ' ' or '\t').SkipAtLeastOnce();

    // Elements on a line (space separated, same line only)
    static readonly Parser<char, List<BlockElement>> ElementsLineParser =
        from _ in CommonParsers.InlineWhitespace
        from elements in ElementParser.SeparatedAtLeastOnce(InlineRequiredWhitespace)
        from __ in CommonParsers.InlineWhitespace
        from ___ in CommonParsers.LineEnd
        select elements.ToList();

    // Arrow: -->
    static readonly Parser<char, Unit> ArrowParser =
        String("-->").ThenReturn(Unit.Value);

    // Arrow line: a --> b --> c (chain of identifiers connected by arrows)
    // Must use Try() because 'a' could also be parsed as an element identifier
    static readonly Parser<char, List<BlockEdge>> ArrowLineParser =
        Try(
            from _ in CommonParsers.InlineWhitespace
            from first in Identifier
            from rest in (
                from __ in CommonParsers.InlineWhitespace
                from ___ in ArrowParser
                from ____ in CommonParsers.InlineWhitespace
                from id in Identifier
                select id
            ).AtLeastOnce()
            from _____ in CommonParsers.InlineWhitespace
            from ______ in CommonParsers.LineEnd
            select BuildEdgeChain(first, rest.ToList())
        );

    static List<BlockEdge> BuildEdgeChain(string first, List<string> rest)
    {
        var edges = new List<BlockEdge>();
        var prev = first;
        foreach (var next in rest)
        {
            edges.Add(new BlockEdge { FromId = prev, ToId = next });
            prev = next;
        }
        return edges;
    }

    // Skip line (comments, empty lines)
    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    // Content item
    static Parser<char, object?> ContentItem =>
        OneOf(
            Try(ColumnsParser.Select(c => (object?)("columns", c))),
            ArrowLineParser.Select(e => (object?)("edges", e)),
            Try(ElementsLineParser.Select(e => (object?)("elements", e))),
            SkipLine.ThenReturn((object?)null)
        );

    public static Parser<char, BlockModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(CIString("block-beta"), CIString("block"))
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from result in ContentItem.ManyThen(End)
        select BuildModel(result.Item1.Where(c => c != null).ToList());

    static BlockModel BuildModel(List<object?> content)
    {
        var model = new BlockModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case ("columns", int columns):
                    model.Columns = columns;
                    break;

                case ("elements", List<BlockElement> elements):
                    model.Elements.AddRange(elements);
                    break;

                case ("edges", List<BlockEdge> edges):
                    model.Edges.AddRange(edges);
                    break;
            }
        }

        return model;
    }

    public Result<char, BlockModel> Parse(string input) => Parser.Parse(input);
}
