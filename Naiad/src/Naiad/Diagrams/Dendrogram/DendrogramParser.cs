namespace MermaidSharp.Diagrams.Dendrogram;

public class DendrogramParser : IDiagramParser<DendrogramModel>
{
    public DiagramType DiagramType => DiagramType.Dendrogram;

    static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').AtLeastOnceString();

    static readonly Parser<char, double> Number =
        from neg in Char('-').Optional()
        from digits in Digit.AtLeastOnceString()
        from dec in Char('.').Then(Digit.AtLeastOnceString()).Optional()
        select double.Parse((neg.HasValue ? "-" : "") + digits + (dec.HasValue ? "." + dec.Value : ""));

    static readonly Parser<char, string> QuotedString =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    static readonly Parser<char, DendrogramLeaf> LeafItem =
        from label in QuotedString
        select new DendrogramLeaf { Id = label, Label = label };

    static readonly Parser<char, List<DendrogramLeaf>> LeafLineParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("leaf")
        from ___ in CommonParsers.RequiredWhitespace
        from leaves in LeafItem.SeparatedAtLeastOnce(
            CommonParsers.InlineWhitespace.Then(Char(',').Optional()).Then(CommonParsers.InlineWhitespace))
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select leaves.ToList();

    static readonly Parser<char, DendrogramMerge> MergeItem =
        from left in Identifier.Or(QuotedString)
        from _ in CommonParsers.InlineWhitespace
        from __ in Char('-')
        from ___ in CommonParsers.InlineWhitespace
        from right in Identifier.Or(QuotedString)
        from ____ in CommonParsers.InlineWhitespace
        from _____ in Char(':')
        from ______ in CommonParsers.InlineWhitespace
        from height in Number
        select new DendrogramMerge
        {
            Left = left,
            Right = right,
            Height = height
        };

    static readonly Parser<char, DendrogramMerge> MergeLineParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(Try(CIString("merge")), Try(CIString("link")))
        from ___ in CommonParsers.RequiredWhitespace
        from merge in MergeItem
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select merge;

    static readonly Parser<char, string> TitleParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("title")
        from ___ in CommonParsers.RequiredWhitespace
        from title in Token(c => c != '\r' && c != '\n').ManyString()
        from ____ in CommonParsers.LineEnd
        select title.Trim();

    static readonly Parser<char, bool> DirectionParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("direction")
        from ___ in CommonParsers.RequiredWhitespace
        from dir in OneOf(Try(CIString("horizontal")), CIString("vertical"))
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select dir.Equals("horizontal", StringComparison.OrdinalIgnoreCase);

    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    static Parser<char, object?> ContentItem =>
        OneOf(
            Try(TitleParser.Select(t => (object?)(ItemType.Title, t))),
            Try(DirectionParser.Select(d => (object?)(ItemType.Direction, d))),
            Try(LeafLineParser.Select(l => (object?)(ItemType.Leaf, l))),
            Try(MergeLineParser.Select(m => (object?)(ItemType.Merge, m))),
            SkipLine.ThenReturn((object?)null)
        );

    enum ItemType { Title, Direction, Leaf, Merge }

    public static Parser<char, DendrogramModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("dendrogram")
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from result in ContentItem.ManyThen(End)
        select BuildModel(result.Item1.Where(c => c != null).ToList());

    static DendrogramModel BuildModel(List<object?> content)
    {
        var model = new DendrogramModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case (ItemType.Title, string title):
                    model.Title = title;
                    break;

                case (ItemType.Direction, bool horizontal):
                    model.Horizontal = horizontal;
                    break;

                case (ItemType.Leaf, List<DendrogramLeaf> leaves):
                    foreach (var leaf in leaves)
                        model.Leaves.Add(leaf);
                    break;

                case (ItemType.Merge, DendrogramMerge merge):
                    model.Merges.Add(merge);
                    break;
            }
        }

        return model;
    }

    public Result<char, DendrogramModel> Parse(string input) => Parser.Parse(input);
}
