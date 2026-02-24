namespace MermaidSharp.Diagrams.Voronoi;

public class VoronoiParser : IDiagramParser<VoronoiModel>
{
    public DiagramType DiagramType => DiagramType.Voronoi;

    static readonly Parser<char, double> Number =
        from neg in Char('-').Optional()
        from digits in Digit.AtLeastOnceString()
        from dec in Char('.').Then(Digit.AtLeastOnceString()).Optional()
        select double.Parse((neg.HasValue ? "-" : "") + digits + (dec.HasValue ? "." + dec.Value : ""));

    static readonly Parser<char, string> QuotedString =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    static readonly Parser<char, VoronoiSite> SiteWithCoords =
        from label in QuotedString
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("at")
        from ___ in CommonParsers.RequiredWhitespace
        from x in Number
        from ____ in CommonParsers.InlineWhitespace
        from _____ in Char(',')
        from ______ in CommonParsers.InlineWhitespace
        from y in Number
        select new VoronoiSite
        {
            Id = label,
            Label = label,
            X = x,
            Y = y
        };

    static readonly Parser<char, VoronoiSite> SiteWithWeight =
        from label in QuotedString
        from _ in CommonParsers.InlineWhitespace
        from __ in Char(':')
        from ___ in CommonParsers.InlineWhitespace
        from weight in Number
        select new VoronoiSite
        {
            Id = label,
            Label = label,
            Weight = weight
        };

    static readonly Parser<char, VoronoiSite> SimpleSite =
        from label in QuotedString
        select new VoronoiSite
        {
            Id = label,
            Label = label,
            Weight = 1
        };

    static readonly Parser<char, VoronoiSite> SiteLineParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(Try(CIString("site")), Try(CIString("point")), CIString("node"))
        from ___ in CommonParsers.RequiredWhitespace
        from site in OneOf(Try(SiteWithCoords), Try(SiteWithWeight), SimpleSite)
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select site;

    static readonly Parser<char, string> TitleParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("title")
        from ___ in CommonParsers.RequiredWhitespace
        from title in Token(c => c != '\r' && c != '\n').ManyString()
        from ____ in CommonParsers.LineEnd
        select title.Trim();

    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    static Parser<char, object?> ContentItem =>
        OneOf(
            Try(TitleParser.Select(t => (object?)(ItemType.Title, t))),
            Try(SiteLineParser.Select(s => (object?)(ItemType.Site, s))),
            SkipLine.ThenReturn((object?)null)
        );

    enum ItemType { Title, Site }

    public static Parser<char, VoronoiModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("voronoi")
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from result in ContentItem.ManyThen(End)
        select BuildModel(result.Item1.Where(c => c != null).ToList());

    static VoronoiModel BuildModel(List<object?> content)
    {
        var model = new VoronoiModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case (ItemType.Title, string title):
                    model.Title = title;
                    break;

                case (ItemType.Site, VoronoiSite site):
                    model.Sites.Add(site);
                    break;
            }
        }

        return model;
    }

    public Result<char, VoronoiModel> Parse(string input) => Parser.Parse(input);
}
