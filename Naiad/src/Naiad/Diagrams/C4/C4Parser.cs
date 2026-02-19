namespace MermaidSharp.Diagrams.C4;

public class C4Parser : IDiagramParser<C4Model>
{
    public DiagramType DiagramType => DiagramType.C4Context;

    // Wrapper for boundary/deployment blocks with nested content
    sealed record BoundaryBlock(C4Boundary Boundary, List<object?> Nested);

    // Identifier
    static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').AtLeastOnceString();

    // Quoted string
    static readonly Parser<char, string> QuotedString =
        Char('"').Then(Token(c => c != '"').ManyString()).Before(Char('"'));

    // Rest of line
    static readonly Parser<char, string> RestOfLine =
        Token(c => c != '\r' && c != '\n').ManyString();

    // Title: title My Diagram
    static readonly Parser<char, string> TitleParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("title")
        from ___ in CommonParsers.RequiredWhitespace
        from title in RestOfLine
        from ____ in CommonParsers.LineEnd
        select title.Trim();

    // Person(id, "label", "description")
    static readonly Parser<char, C4Element> PersonParser =
        from _ in CommonParsers.InlineWhitespace
        from type in OneOf(Try(CIString("Person_Ext")), CIString("Person"))
        from __ in Char('(')
        from id in Identifier
        from ___ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from desc in Try(
            CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
            .Then(QuotedString)
        ).Optional()
        from ____ in Char(')')
        from _____ in CommonParsers.InlineWhitespace
        from ______ in CommonParsers.LineEnd
        select new C4Element
        {
            Id = id,
            Label = label,
            Description = desc.GetValueOrDefault(),
            Type = C4ElementType.Person,
            IsExternal = type.Contains("Ext", StringComparison.OrdinalIgnoreCase)
        };

    // System(id, "label", "description") or System_Ext
    static readonly Parser<char, C4Element> SystemParser =
        from _ in CommonParsers.InlineWhitespace
        from type in OneOf(Try(CIString("System_Ext")), CIString("System"))
        from __ in Char('(')
        from id in Identifier
        from ___ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from desc in Try(
            CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
            .Then(QuotedString)
        ).Optional()
        from ____ in Char(')')
        from _____ in CommonParsers.InlineWhitespace
        from ______ in CommonParsers.LineEnd
        select new C4Element
        {
            Id = id,
            Label = label,
            Description = desc.GetValueOrDefault(),
            Type = C4ElementType.System,
            IsExternal = type.Contains("Ext", StringComparison.OrdinalIgnoreCase)
        };

    // Optional quoted string with comma prefix
    static readonly Parser<char, string> OptionalQuotedArg =
        CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
            .Then(QuotedString);

    // Container(id, "label", "tech", "description") or Container_Ext
    static readonly Parser<char, C4Element> ContainerParser =
        from _ in CommonParsers.InlineWhitespace
        from type in OneOf(
            Try(CIString("Container_Ext")),
            Try(CIString("ContainerDb_Ext")),
            Try(CIString("ContainerQueue_Ext")),
            Try(CIString("ContainerDb")),
            Try(CIString("ContainerQueue")),
            CIString("Container"))
        from __ in Char('(')
        from id in Identifier
        from ___ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from tech in Try(OptionalQuotedArg).Optional()
        from desc in Try(OptionalQuotedArg).Optional()
        from ____ in Char(')')
        from _____ in CommonParsers.InlineWhitespace
        from ______ in CommonParsers.LineEnd
        select new C4Element
        {
            Id = id,
            Label = label,
            Technology = tech.GetValueOrDefault(),
            Description = desc.GetValueOrDefault(),
            Type = type.Contains("Db", StringComparison.OrdinalIgnoreCase) ? C4ElementType.ContainerDb :
                   type.Contains("Queue", StringComparison.OrdinalIgnoreCase) ? C4ElementType.ContainerQueue :
                   C4ElementType.Container,
            IsExternal = type.Contains("Ext", StringComparison.OrdinalIgnoreCase)
        };

    // Component(id, "label", "tech", "description") or ComponentDb
    static readonly Parser<char, C4Element> ComponentParser =
        from _ in CommonParsers.InlineWhitespace
        from type in OneOf(
            Try(CIString("ComponentDb_Ext")),
            Try(CIString("ComponentDb")),
            Try(CIString("Component_Ext")),
            CIString("Component"))
        from __ in Char('(')
        from id in Identifier
        from ___ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from tech in Try(OptionalQuotedArg).Optional()
        from desc in Try(OptionalQuotedArg).Optional()
        from ____ in Char(')')
        from _____ in CommonParsers.InlineWhitespace
        from ______ in CommonParsers.LineEnd
        select new C4Element
        {
            Id = id,
            Label = label,
            Technology = tech.GetValueOrDefault(),
            Description = desc.GetValueOrDefault(),
            Type = type.Contains("Db", StringComparison.OrdinalIgnoreCase) ? C4ElementType.ContainerDb :
                   C4ElementType.Component,
            IsExternal = type.Contains("Ext", StringComparison.OrdinalIgnoreCase)
        };

    // Rel(from, to, "label", "tech")
    static readonly Parser<char, C4Relationship> RelParser =
        from _ in CommonParsers.InlineWhitespace
        from __ in OneOf(
            Try(CIString("Rel_D")), Try(CIString("Rel_U")),
            Try(CIString("Rel_L")), Try(CIString("Rel_R")),
            Try(CIString("Rel_Back")), Try(CIString("Rel_Neighbor")),
            CIString("Rel"))
        from ___ in Char('(')
        from fromId in Identifier
        from ____ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from toId in Identifier
        from label in Try(OptionalQuotedArg).Optional()
        from tech in Try(OptionalQuotedArg).Optional()
        from _____ in Char(')')
        from ______ in CommonParsers.InlineWhitespace
        from _______ in CommonParsers.LineEnd
        select new C4Relationship
        {
            From = fromId,
            To = toId,
            Label = label.GetValueOrDefault(),
            Technology = tech.GetValueOrDefault()
        };

    // Skip line (comments, empty lines)
    static readonly Parser<char, Unit> SkipLine =
        Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
            .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

    // Closing brace for boundary/deployment blocks
    static readonly Parser<char, Unit> ClosingBrace =
        CommonParsers.InlineWhitespace
            .Then(Char('}'))
            .Then(CommonParsers.InlineWhitespace)
            .Then(CommonParsers.LineEnd);

    // Boundary block: Container_Boundary, System_Boundary, Enterprise_Boundary
    static Parser<char, BoundaryBlock> BoundaryBlockParser =>
        from _ in CommonParsers.InlineWhitespace
        from boundaryType in OneOf(
            Try(CIString("Container_Boundary")).ThenReturn(C4BoundaryType.Container),
            Try(CIString("System_Boundary")).ThenReturn(C4BoundaryType.System),
            CIString("Enterprise_Boundary").ThenReturn(C4BoundaryType.Enterprise))
        from __ in Char('(')
        from id in Identifier
        from ___ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from ____ in Char(')')
        from _____ in CommonParsers.InlineWhitespace
        from ______ in Char('{')
        from _______ in CommonParsers.InlineWhitespace
        from ________ in CommonParsers.LineEnd
        from items in Rec(() => ContentItem).Many()
        from _________ in ClosingBrace
        select new BoundaryBlock(
            new C4Boundary { Id = id, Label = label, Type = boundaryType },
            items.Where(c => c != null).ToList());

    // Deployment_Node block (supports nesting via recursive ContentItem)
    static Parser<char, BoundaryBlock> DeploymentNodeParser =>
        from _ in CommonParsers.InlineWhitespace
        from __ in CIString("Deployment_Node")
        from ___ in Char('(')
        from id in Identifier
        from ____ in CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)
        from label in QuotedString
        from _____ in Char(')')
        from ______ in CommonParsers.InlineWhitespace
        from _______ in Char('{')
        from ________ in CommonParsers.InlineWhitespace
        from _________ in CommonParsers.LineEnd
        from items in Rec(() => ContentItem).Many()
        from __________ in ClosingBrace
        select new BoundaryBlock(
            new C4Boundary { Id = id, Label = label, Type = C4BoundaryType.Deployment },
            items.Where(c => c != null).ToList());

    // Content item
    static Parser<char, object?> ContentItem =>
        OneOf(
            Try(TitleParser.Select(t => (object?)("title", t))),
            Try(BoundaryBlockParser.Select(b => (object?)b)),
            Try(DeploymentNodeParser.Select(b => (object?)b)),
            Try(PersonParser.Select(e => (object?)("element", e))),
            Try(SystemParser.Select(e => (object?)("element", e))),
            Try(ContainerParser.Select(e => (object?)("element", e))),
            Try(ComponentParser.Select(e => (object?)("element", e))),
            Try(RelParser.Select(r => (object?)("rel", r))),
            SkipLine.ThenReturn((object?)null)
        );

    // Diagram type header
    static readonly Parser<char, C4DiagramType> DiagramTypeParser =
        OneOf(
            Try(CIString("C4Context")).ThenReturn(C4DiagramType.Context),
            Try(CIString("C4Container")).ThenReturn(C4DiagramType.Container),
            Try(CIString("C4Component")).ThenReturn(C4DiagramType.Component),
            Try(CIString("C4Deployment")).ThenReturn(C4DiagramType.Deployment)
        );

    public static Parser<char, C4Model> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from type in DiagramTypeParser
        from __ in CommonParsers.InlineWhitespace
        from ___ in CommonParsers.LineEnd
        from result in ContentItem.ManyThen(End)
        select BuildModel(type, result.Item1.Where(c => c != null).ToList());

    static C4Model BuildModel(C4DiagramType type, List<object?> content)
    {
        var model = new C4Model { Type = type };
        ProcessContent(content, model, null);
        return model;
    }

    static void ProcessContent(List<object?> content, C4Model model, C4Boundary? parentBoundary)
    {
        foreach (var item in content)
        {
            switch (item)
            {
                case ("title", string title):
                    model.Title = title;
                    break;

                case ("element", C4Element element):
                    if (parentBoundary != null)
                    {
                        element.BoundaryId = parentBoundary.Id;
                        parentBoundary.ElementIds.Add(element.Id);
                    }
                    model.Elements.Add(element);
                    break;

                case ("rel", C4Relationship rel):
                    model.Relationships.Add(rel);
                    break;

                case BoundaryBlock block:
                    model.Boundaries.Add(block.Boundary);
                    ProcessContent(block.Nested, model, block.Boundary);
                    break;
            }
        }
    }

    public Result<char, C4Model> Parse(string input) => Parser.Parse(input);
}
