namespace MermaidSharp.Diagrams.ParallelCoords;

public class ParallelCoordsParser : IDiagramParser<ParallelCoordsModel>
{
    public DiagramType DiagramType => DiagramType.ParallelCoords;

    static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').AtLeastOnceString();

    static readonly Parser<char, double> Number =
        from neg in Char('-').Optional()
        from digits in Digit.AtLeastOnceString()
        from dec in Char('.').Then(Digit.AtLeastOnceString()).Optional()
        select double.Parse((neg.HasValue ? "-" : "") + digits + (dec.HasValue ? "." + dec.Value : ""));

    static readonly Parser<char, string> QuotedString =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    static readonly Parser<char, string> QuotedLabel =
        Char('[').Then(Char('"')).Then(Token(c => c != '"').ManyString()).Before(Char('"')).Before(Char(']'));

    static readonly Parser<char, List<string>> AxisList =
        from _ in CommonParsers.InlineWhitespace
        from axes in Identifier.SeparatedAtLeastOnce(
            CommonParsers.InlineWhitespace.Then(Char(',').Optional()).Then(CommonParsers.InlineWhitespace))
        select axes.ToList();

    static readonly Parser<char, List<double>> ValueList =
        Char('{')
            .Then(CommonParsers.InlineWhitespace)
            .Then(Number.SeparatedAtLeastOnce(
                CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)))
            .Before(CommonParsers.InlineWhitespace)
            .Before(Char('}'))
            .Select(v => v.ToList());

    static readonly Parser<char, ParallelDataset> DatasetParser =
        from name in QuotedString
        from label in QuotedLabel.Optional()
        from values in ValueList
        from cssClass in CommonParsers.CssClass.Optional()
        select new ParallelDataset
        {
            Name = label.GetValueOrDefault() ?? name
        }.WithValues(values);

    static readonly Parser<char, List<ParallelAxis>> AxisLineParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("axis")
        from ___ in CommonParsers.RequiredWhitespace
        from axes in AxisList
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select axes.Select((a, i) => new ParallelAxis { Id = a, Label = a, Index = i }).ToList();

    static readonly Parser<char, ParallelDataset> DatasetLineParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("dataset")
        from ___ in CommonParsers.RequiredWhitespace
        from dataset in DatasetParser
        from ____ in CommonParsers.InlineWhitespace
        from _____ in CommonParsers.LineEnd
        select dataset;

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
            Try(AxisLineParser.Select(a => (object?)(ItemType.Axis, a))),
            Try(DatasetLineParser.Select(d => (object?)(ItemType.Dataset, d))),
            SkipLine.ThenReturn((object?)null)
        );

    enum ItemType { Title, Axis, Dataset }

    public static Parser<char, ParallelCoordsModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(Try(CIString("parallelcoords")), CIString("parallel-coords"))
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from result in ContentItem.ManyThen(End)
        select BuildModel(result.Item1.Where(c => c != null).ToList());

    static ParallelCoordsModel BuildModel(List<object?> content)
    {
        var model = new ParallelCoordsModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case (ItemType.Title, string title):
                    model.Title = title;
                    break;

                case (ItemType.Axis, List<ParallelAxis> axes):
                    foreach (var axis in axes)
                        model.Axes.Add(axis);
                    break;

                case (ItemType.Dataset, ParallelDataset dataset):
                    model.Datasets.Add(dataset);
                    break;
            }
        }

        return model;
    }

    public Result<char, ParallelCoordsModel> Parse(string input) => Parser.Parse(input);
}

static class ParallelDatasetExtensions
{
    public static ParallelDataset WithValues(this ParallelDataset dataset, List<double> values)
    {
        foreach (var v in values)
            dataset.Values.Add(v);
        return dataset;
    }
}
