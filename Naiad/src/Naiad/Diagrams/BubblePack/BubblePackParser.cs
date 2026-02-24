namespace MermaidSharp.Diagrams.BubblePack;

public class BubblePackParser : IDiagramParser<BubblePackModel>
{
    public DiagramType DiagramType => DiagramType.BubblePack;

    static readonly Parser<char, string> QuotedString =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    static readonly Parser<char, double> Number =
        from neg in Char('-').Optional()
        from digits in Digit.AtLeastOnceString()
        from dec in Char('.').Then(Digit.AtLeastOnceString()).Optional()
        select double.Parse((neg.HasValue ? "-" : "") + digits + (dec.HasValue ? "." + dec.Value : ""));

    static readonly Parser<char, string> CssClass =
        String(":::").Then(Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').AtLeastOnceString());

    record NodeLine(int Indent, string Label, double Value, string? CssClass);

    static readonly Parser<char, NodeLine> LineParser =
        from indent in CommonParsers.Indentation
        from label in QuotedString
        from value in (
            from _ in CommonParsers.InlineWhitespace
            from __ in Char(':')
            from ___ in CommonParsers.InlineWhitespace
            from v in Number
            select v
        ).Optional()
        from cssClass in (
            from _ in CommonParsers.InlineWhitespace
            from cls in CssClass
            select cls
        ).Optional()
        from _ in CommonParsers.InlineWhitespace
        from __ in CommonParsers.LineEnd
        select new NodeLine(indent, label, value.GetValueOrDefault(), cssClass.GetValueOrDefault());

    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    static Parser<char, NodeLine?> ContentItem =>
        Try(LineParser.Select(n => (NodeLine?)n))
            .Or(SkipLine.ThenReturn((NodeLine?)null));

    public static Parser<char, BubblePackModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(Try(CIString("bubblepack")), Try(CIString("bubble-pack")), CIString("bubble"))
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from lines in ContentItem.ManyThen(End)
        select BuildModel(lines.Item1.Where(l => l != null).Cast<NodeLine>().ToList());

    static BubblePackModel BuildModel(List<NodeLine> lines)
    {
        var model = new BubblePackModel();
        var stack = new Stack<(BubbleNode node, int indent)>();
        var idCounter = 0;

        foreach (var line in lines)
        {
            var node = new BubbleNode
            {
                Id = $"node_{idCounter++}",
                Label = line.Label,
                Value = line.Value,
                CssClass = line.CssClass
            };

            while (stack.Count > 0 && stack.Peek().indent >= line.Indent)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                model.RootNodes.Add(node);
            }
            else
            {
                stack.Peek().node.Children.Add(node);
            }

            stack.Push((node, line.Indent));
        }

        return model;
    }

    public Result<char, BubblePackModel> Parse(string input) => Parser.Parse(input);
}
