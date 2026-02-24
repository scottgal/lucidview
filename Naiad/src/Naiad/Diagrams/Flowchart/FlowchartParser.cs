using System.Text.RegularExpressions;

namespace MermaidSharp.Diagrams.Flowchart;

public class FlowchartParser : IDiagramParser<FlowchartModel>
{
    public DiagramType DiagramType => DiagramType.Flowchart;

    static readonly Regex HeaderPattern =
        new(@"^(flowchart|graph)(?:\s+(TB|TD|BT|LR|RL))?\s*$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static readonly Regex SubgraphPattern =
        new(@"^subgraph\s+(.+)$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static readonly Regex DirectionPattern =
        new(@"^direction\s+(TB|TD|BT|LR|RL)$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static readonly Regex SupportedDirectivePattern =
        new(@"^(classDef|class|style|linkStyle|click)\b", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    // Quoted content parser: "content with special chars" → content with special chars
    // Used inside shape brackets to allow characters that would otherwise terminate the shape parser
    static readonly Parser<char, string> QuotedContent =
        Char('"')
            .Then(Token(c => c != '"').ManyString())
            .Before(Char('"'));

    // Helper: parse content inside a shape delimiter, supporting quoted strings
    static Parser<char, string> ShapeContent(char terminator) =>
        Try(QuotedContent).Or(Token(c => c != terminator).ManyString());

    static Parser<char, string> ShapeContent(string terminator)
    {
        var terminatorFirst = terminator[0];
        return Try(QuotedContent).Or(Token(c => c != terminatorFirst).ManyString());
    }

    // Node shape parsers - returns (label, shape)
    static readonly Parser<char, (string Label, NodeShape Shape)> DoubleCircleShape =
        String("(((")
            .Then(ShapeContent(")"))
            .Before(String(")))"))
            .Select(text => (text, NodeShape.DoubleCircle));

    static readonly Parser<char, (string Label, NodeShape Shape)> CircleShape =
        String("((")
            .Then(ShapeContent(")"))
            .Before(String("))"))
            .Select(text => (text, NodeShape.Circle));

    static readonly Parser<char, (string Label, NodeShape Shape)> StadiumShape =
        String("([")
            .Then(ShapeContent("]"))
            .Before(String("])"))
            .Select(text => (text, NodeShape.Stadium));

    static readonly Parser<char, (string Label, NodeShape Shape)> SubroutineShape =
        String("[[")
            .Then(ShapeContent("]"))
            .Before(String("]]"))
            .Select(text => (text, NodeShape.Subroutine));

    static readonly Parser<char, (string Label, NodeShape Shape)> CylinderShape =
        String("[(")
            .Then(ShapeContent(")"))
            .Before(String(")]"))
            .Select(text => (text, NodeShape.Cylinder));

    static readonly Parser<char, (string Label, NodeShape Shape)> HexagonShape =
        String("{{")
            .Then(ShapeContent("}"))
            .Before(String("}}"))
            .Select(text => (text, NodeShape.Hexagon));

    static readonly Parser<char, (string Label, NodeShape Shape)> DiamondShape =
        Char('{')
            .Then(ShapeContent('}'))
            .Before(Char('}'))
            .Select(text => (text, NodeShape.Diamond));

    static readonly Parser<char, (string Label, NodeShape Shape)> RoundedShape =
        Char('(')
            .Then(ShapeContent(')'))
            .Before(Char(')'))
            .Select(text => (text, NodeShape.RoundedRectangle));

    // Trapezoid: [/"text"/]
    static readonly Parser<char, (string Label, NodeShape Shape)> TrapezoidShape =
        String("[/")
            .Then(Try(QuotedContent).Or(Token(c => c != '"' && c != '/').ManyString()))
            .Before(String("/]"))
            .Select(text => (text, NodeShape.Trapezoid));

    // Trapezoid alt: [\"text"\]
    static readonly Parser<char, (string Label, NodeShape Shape)> TrapezoidAltShape =
        String("[\\")
            .Then(Try(QuotedContent).Or(Token(c => c != '"' && c != '\\').ManyString()))
            .Before(String("\\]"))
            .Select(text => (text, NodeShape.TrapezoidAlt));

    // Parallelogram: [/text/] - same delimiters but without quotes inside
    // Note: TrapezoidShape handles [/"text"/], this handles [/text/] without quotes
    static readonly Parser<char, (string Label, NodeShape Shape)> ParallelogramShape =
        String("[/")
            .Then(Token(c => c != '/').ManyString())
            .Before(String("/]"))
            .Select(text => (text, NodeShape.Parallelogram));

    // Parallelogram alt: [\text\]
    static readonly Parser<char, (string Label, NodeShape Shape)> ParallelogramAltShape =
        String("[\\")
            .Then(Token(c => c != '\\').ManyString())
            .Before(String("\\]"))
            .Select(text => (text, NodeShape.ParallelogramAlt));

    static readonly Parser<char, (string Label, NodeShape Shape)> RectangleShape =
        Char('[')
            .Then(ShapeContent(']'))
            .Before(Char(']'))
            .Select(text => (text, NodeShape.Rectangle));

    static readonly Parser<char, (string Label, NodeShape Shape)> AsymmetricShape =
        Char('>')
            .Then(ShapeContent(']'))
            .Before(Char(']'))
            .Select(text => (text, NodeShape.Asymmetric));

    static readonly Parser<char, (string Label, NodeShape Shape)> NodeShapeParser =
        OneOf(
            Try(DoubleCircleShape),
            Try(CircleShape),
            Try(StadiumShape),
            Try(SubroutineShape),
            Try(CylinderShape),
            Try(HexagonShape),
            Try(DiamondShape),
            Try(RoundedShape),
            Try(TrapezoidShape),
            Try(TrapezoidAltShape),
            Try(ParallelogramShape),
            Try(ParallelogramAltShape),
            Try(AsymmetricShape),
            RectangleShape
        );

    // Node parser: identifier optionally followed by shape
    static readonly Parser<char, Node> NodeParser =
        from id in CommonParsers.Identifier
        from shape in NodeShapeParser.Optional()
        select new Node
        {
            Id = id,
            Label = shape.HasValue ? StripQuotes(shape.Value.Label) : null,
            Shape = shape.HasValue ? shape.Value.Shape : NodeShape.Rectangle
        };

    /// <summary>
    /// Strip surrounding double quotes from labels (mermaid syntax for labels with special chars).
    /// </summary>
    static string? StripQuotes(string? label)
    {
        if (label is null) return null;
        label = label.Trim();
        if (label.Length >= 2 && label[0] == '"' && label[^1] == '"')
            return label[1..^1];
        return label;
    }

    // Arrow parsers
    static readonly Parser<char, (EdgeType Type, EdgeStyle Style)> ArrowTypeParser =
        OneOf(
            Try(String("<-->")).ThenReturn((EdgeType.BiDirectional, EdgeStyle.Solid)),
            Try(String("o--o")).ThenReturn((EdgeType.BiDirectionalCircle, EdgeStyle.Solid)),
            Try(String("x--x")).ThenReturn((EdgeType.BiDirectionalCross, EdgeStyle.Solid)),
            Try(String("-.->")).ThenReturn((EdgeType.DottedArrow, EdgeStyle.Dotted)),
            Try(String("-.-")).ThenReturn((EdgeType.Dotted, EdgeStyle.Dotted)),
            Try(String("==>")).ThenReturn((EdgeType.ThickArrow, EdgeStyle.Thick)),
            Try(String("===")).ThenReturn((EdgeType.Thick, EdgeStyle.Thick)),
            Try(String("--o")).ThenReturn((EdgeType.CircleEnd, EdgeStyle.Solid)),
            Try(String("--x")).ThenReturn((EdgeType.CrossEnd, EdgeStyle.Solid)),
            Try(String("-->")).ThenReturn((EdgeType.Arrow, EdgeStyle.Solid)),
            String("---").ThenReturn((EdgeType.Open, EdgeStyle.Solid))
        );

    // Edge label: |text|
    static readonly Parser<char, string> EdgeLabelParser =
        Char('|')
            .Then(Token(c => c != '|').ManyString())
            .Before(Char('|'));

    // Direction parser
    static readonly Parser<char, Direction> FlowchartDirection =
        OneOf(
            Try(String("TB")).ThenReturn(Direction.TopToBottom),
            Try(String("TD")).ThenReturn(Direction.TopToBottom),
            Try(String("BT")).ThenReturn(Direction.BottomToTop),
            Try(String("LR")).ThenReturn(Direction.LeftToRight),
            String("RL").ThenReturn(Direction.RightToLeft)
        );

    // Statement: A --> B --> C (chain of nodes with edges)
    static readonly Parser<char, (List<Node> Nodes, List<(EdgeType Type, EdgeStyle Style, string? Label)> Edges)> StatementParser =
        from first in NodeParser
        from rest in (
            from _1 in CommonParsers.InlineWhitespace
            from label1 in EdgeLabelParser.Optional()
            from _2 in CommonParsers.InlineWhitespace
            from arrow in ArrowTypeParser
            from _3 in CommonParsers.InlineWhitespace
            from label2 in EdgeLabelParser.Optional()
            from _4 in CommonParsers.InlineWhitespace
            from node in NodeParser
            select (node, arrow.Type, arrow.Style, label1.HasValue ? label1.Value : label2.HasValue ? label2.Value : null)
        ).Many()
        select (
            new List<Node>([first, .. rest.Select(r => r.node)]),
            rest.Select(r => (r.Type, r.Style, (string?)r.Item4)).ToList()
        );

    // Skip empty lines and comments
    static readonly Parser<char, Unit> SkipLine =
        CommonParsers.InlineWhitespace
            .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

    static readonly Parser<char, (List<Node> Nodes, List<(EdgeType Type, EdgeStyle Style, string? Label)> Edges)> StandaloneStatementParser =
        CommonParsers.InlineWhitespace
            .Then(StatementParser)
            .Before(CommonParsers.InlineWhitespace)
            .Before(End);

    public static Parser<char, FlowchartModel> Parser =>
        from _ in CommonParsers.InlineWhitespace
        from _keyword in Try(String("flowchart")).Or(String("graph"))
        from __ in CommonParsers.InlineWhitespace
        from direction in FlowchartDirection.Optional()
        from ___ in CommonParsers.InlineWhitespace
        from ____ in CommonParsers.LineEnd
        from statements in ParseStatements()
        select BuildModel(direction.GetValueOrDefault(Direction.TopToBottom), statements);

    static Parser<char, List<(List<Node> Nodes, List<(EdgeType Type, EdgeStyle Style, string? Label)> Edges)>> ParseStatements()
    {
        var statement =
            CommonParsers.InlineWhitespace
                .Then(StatementParser)
                .Before(CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd));

        var skipLine = SkipLine.ThenReturn((new List<Node>(), new List<(EdgeType, EdgeStyle, string?)>()));

        return Try(statement).Or(skipLine).Many()
            .Select(s => s.Where(x => x.Nodes.Count > 0).ToList());
    }

    static FlowchartModel BuildModel(Direction direction,
        List<(List<Node> Nodes, List<(EdgeType Type, EdgeStyle Style, string? Label)> Edges)> statements)
    {
        var model = new FlowchartModel { Direction = direction };
        var nodeDict = new Dictionary<string, Node>();

        foreach (var (nodes, edges) in statements)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];

                // Add or update node
                if (!nodeDict.TryGetValue(node.Id, out var existingNode))
                {
                    nodeDict[node.Id] = node;
                    model.Nodes.Add(node);
                }
                else if (node.Label != null && existingNode.Label == null)
                {
                    existingNode.Label = node.Label;
                    existingNode.Shape = node.Shape;
                }

                // Add edge to next node
                if (i < edges.Count)
                {
                    var edge = edges[i];
                    model.Edges.Add(new()
                    {
                        SourceId = nodes[i].Id,
                        TargetId = nodes[i + 1].Id,
                        Type = edge.Type,
                        LineStyle = edge.Style,
                        Label = StripQuotes(edge.Label)
                    });
                }
            }
        }

        return model;
    }

    public Result<char, FlowchartModel> Parse(string input)
    {
        var lines = SplitLines(input);
        if (!TryGetHeader(lines, out var header, out var headerIndex))
        {
            return Parser.Parse(input);
        }

        var prepared = PrepareInput(lines, header, headerIndex);
        var result = Parser.Parse(prepared.CleanInput);
        if (!result.Success)
        {
            return result;
        }

        ApplySubgraphMetadata(result.Value, prepared.Subgraphs);
        ApplyStyles(result.Value, prepared.Styles);
        return result;
    }

    static (string CleanInput, List<ParsedSubgraph> Subgraphs, List<(string NodeId, Style Style)> Styles) PrepareInput(List<string> lines, string header, int headerIndex)
    {
        var normalizedLines = new List<string> { header };
        var subgraphs = new List<ParsedSubgraph>();
        var styles = new List<(string NodeId, Style Style)>();
        var classDefs = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
        var classAssignments = new List<(string NodeIds, string ClassName)>();
        var subgraphStack = new Stack<ParsedSubgraph>();
        var usedSubgraphIds = new HashSet<string>(StringComparer.Ordinal);
        var subgraphCounter = 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryParseSubgraphLine(trimmed, usedSubgraphIds, ref subgraphCounter, out var parsedSubgraph))
            {
                if (subgraphStack.Count > 0)
                {
                    parsedSubgraph.ParentId = subgraphStack.Peek().Id;
                    subgraphStack.Peek().NestedSubgraphIds.Add(parsedSubgraph.Id);
                }

                subgraphs.Add(parsedSubgraph);
                subgraphStack.Push(parsedSubgraph);
                continue;
            }

            if (trimmed.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                if (subgraphStack.Count > 0)
                {
                    subgraphStack.Pop();
                }

                continue;
            }

            if (subgraphStack.Count > 0 && TryParseDirectionLine(trimmed, out var subgraphDirection))
            {
                subgraphStack.Peek().Direction = subgraphDirection;
                continue;
            }

            if (SupportedDirectivePattern.IsMatch(trimmed))
            {
                if (TryParseStyleDirective(trimmed, out var styleNodeId, out var parsedStyle))
                {
                    styles.Add((styleNodeId, parsedStyle));
                }
                else if (TryParseClassDefDirective(trimmed, out var className, out var classStyle))
                {
                    classDefs[className] = classStyle;
                }
                else if (TryParseClassAssignment(trimmed, out var nodeIds, out var assignedClass))
                {
                    classAssignments.Add((nodeIds, assignedClass));
                }
                continue;
            }

            // Expand & (fan-out) syntax: "A --> B & C & D" becomes multiple statements
            var expandedLines = ExpandAmpersandSyntax(trimmed);
            foreach (var expandedLine in expandedLines)
            {
                if (!TryParseStatement(expandedLine, out var statement))
                {
                    continue;
                }

                normalizedLines.Add(expandedLine);

                if (subgraphStack.Count == 0)
                {
                    continue;
                }

                var currentSubgraph = subgraphStack.Peek();
                foreach (var node in statement.Nodes)
                {
                    currentSubgraph.NodeIds.Add(node.Id);
                }
            }
        }

        // Resolve class assignments: expand "class A,B,C frontend" into individual node styles
        foreach (var (nodeIds, assignedClassName) in classAssignments)
        {
            if (!classDefs.TryGetValue(assignedClassName, out var classStyle)) continue;
            foreach (var nodeId in nodeIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                styles.Add((nodeId, classStyle));
            }
        }

        var cleanInput = string.Join("\n", normalizedLines) + "\n";
        return (cleanInput, subgraphs, styles);
    }

    static void ApplySubgraphMetadata(FlowchartModel model, List<ParsedSubgraph> parsedSubgraphs)
    {
        if (parsedSubgraphs.Count == 0)
        {
            return;
        }

        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var subgraphById = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        var subgraphNodeIds = new HashSet<string>(parsedSubgraphs.Select(s => s.Id), StringComparer.Ordinal);

        foreach (var parsedSubgraph in parsedSubgraphs)
        {
            var subgraph = new Subgraph
            {
                Id = parsedSubgraph.Id,
                Title = parsedSubgraph.Title,
                Direction = parsedSubgraph.Direction
            };

            foreach (var nodeId in parsedSubgraph.NodeIds)
            {
                if (!nodeById.TryGetValue(nodeId, out var node))
                {
                    continue;
                }

                node.ParentId = parsedSubgraph.Id;
                if (!subgraph.NodeIds.Contains(nodeId))
                {
                    subgraph.NodeIds.Add(nodeId);
                }
            }

            subgraphById[parsedSubgraph.Id] = subgraph;
        }

        foreach (var parsedSubgraph in parsedSubgraphs)
        {
            var subgraph = subgraphById[parsedSubgraph.Id];
            if (parsedSubgraph.ParentId is null || !subgraphById.TryGetValue(parsedSubgraph.ParentId, out var parent))
            {
                model.Subgraphs.Add(subgraph);
                continue;
            }

            parent.NestedSubgraphs.Add(subgraph);
        }

        // Resolve subgraph-to-subgraph edges: when an edge references a subgraph ID,
        // reroute it to the first node in that subgraph so it actually renders.
        // Also remove any synthetic "nodes" that were created for subgraph IDs.
        foreach (var edge in model.Edges)
        {
            if (subgraphById.TryGetValue(edge.SourceId, out var srcSg) && srcSg.NodeIds.Count > 0)
            {
                edge.SourceId = srcSg.NodeIds[0];
            }
            if (subgraphById.TryGetValue(edge.TargetId, out var tgtSg) && tgtSg.NodeIds.Count > 0)
            {
                edge.TargetId = tgtSg.NodeIds[0];
            }
        }

        // Remove nodes that are actually subgraph IDs (they were created as plain nodes
        // when parsing statements like "SubgraphA --> SubgraphB") but only if they
        // don't have an explicit label (meaning they're just implicit edge endpoints)
        model.Nodes.RemoveAll(n => subgraphNodeIds.Contains(n.Id) && n.Label == null);
    }

    static bool TryGetHeader(List<string> lines, out string header, out int headerIndex)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            var match = HeaderPattern.Match(trimmed);
            if (!match.Success)
            {
                break;
            }

            var keyword = match.Groups[1].Value.ToLowerInvariant();
            var direction = match.Groups[2].Value.ToUpperInvariant();
            header = string.IsNullOrEmpty(direction) ? keyword : $"{keyword} {direction}";
            headerIndex = i;
            return true;
        }

        header = string.Empty;
        headerIndex = -1;
        return false;
    }

    static bool TryParseSubgraphLine(string line, HashSet<string> usedSubgraphIds, ref int subgraphCounter, out ParsedSubgraph parsedSubgraph)
    {
        var match = SubgraphPattern.Match(line);
        if (!match.Success)
        {
            parsedSubgraph = default!;
            return false;
        }

        var body = match.Groups[1].Value.Trim();
        var (candidateId, title) = ParseSubgraphBody(body);

        if (string.IsNullOrWhiteSpace(candidateId))
        {
            candidateId = $"subgraph_{subgraphCounter++}";
        }

        candidateId = SanitizeIdentifier(candidateId);
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            candidateId = $"subgraph_{subgraphCounter++}";
        }

        var finalId = candidateId;
        var duplicateCounter = 1;
        while (!usedSubgraphIds.Add(finalId))
        {
            finalId = $"{candidateId}_{duplicateCounter++}";
        }

        parsedSubgraph = new(finalId, title);
        return true;
    }

    static (string Id, string Title) ParseSubgraphBody(string body)
    {
        if (body.EndsWith(']') && body.Contains('[', StringComparison.Ordinal))
        {
            var bracketIndex = body.IndexOf('[', StringComparison.Ordinal);
            var id = body[..bracketIndex].Trim();
            var title = body.Substring(bracketIndex + 1, body.Length - bracketIndex - 2).Trim();
            // Strip surrounding quotes from title (e.g., subgraph Wave10["Label"])
            title = title.Trim('"');
            return (id, string.IsNullOrWhiteSpace(title) ? id : title);
        }

        if (body.StartsWith('"') && body.EndsWith('"') && body.Length > 1)
        {
            var title = body[1..^1].Trim();
            return (string.Empty, title);
        }

        var parts = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (parts.Length == 1)
        {
            return (parts[0], parts[0]);
        }

        return (parts[0], parts[1].Trim());
    }

    static bool TryParseDirectionLine(string line, out Direction direction)
    {
        var match = DirectionPattern.Match(line);
        if (!match.Success)
        {
            direction = Direction.TopToBottom;
            return false;
        }

        direction = match.Groups[1].Value.ToUpperInvariant() switch
        {
            "TB" or "TD" => Direction.TopToBottom,
            "BT" => Direction.BottomToTop,
            "LR" => Direction.LeftToRight,
            "RL" => Direction.RightToLeft,
            _ => Direction.TopToBottom
        };
        return true;
    }

    /// <summary>
    /// Expand mermaid ampersand fan-out syntax into multiple edge statements.
    /// For example, "A --&gt; B &amp; C" expands to ["A --&gt; B", "A --&gt; C"].
    /// Lines without ampersand are returned as-is.
    /// </summary>
    static List<string> ExpandAmpersandSyntax(string line)
    {
        // Quick check: if no &, return as-is
        if (!line.Contains('&'))
            return [line];

        // Find the first arrow in the line
        var arrowPatterns = new[] { "<-->", "o--o", "x--x", "-.->", "-.-", "==>", "===", "--o", "--x", "-->", "---" };
        string? arrow = null;
        var arrowIdx = -1;
        foreach (var ap in arrowPatterns)
        {
            var idx = line.IndexOf(ap, StringComparison.Ordinal);
            if (idx >= 0 && (arrowIdx < 0 || idx < arrowIdx))
            {
                arrow = ap;
                arrowIdx = idx;
            }
        }

        if (arrow == null || arrowIdx < 0)
            return [line]; // No arrow found, return as-is

        var leftPart = line[..arrowIdx].Trim();
        var rightPart = line[(arrowIdx + arrow.Length)..].Trim();

        // Check for edge labels: |label| on either side
        var label = "";
        if (rightPart.StartsWith('|'))
        {
            var endPipe = rightPart.IndexOf('|', 1);
            if (endPipe > 0)
            {
                label = rightPart[..(endPipe + 1)];
                rightPart = rightPart[(endPipe + 1)..].Trim();
            }
        }

        var leftNodes = SplitAmpersand(leftPart);

        // Separate the immediate right-side nodes from any chain continuation.
        // "W --> FLOOR --> FINAL" should split into right nodes ["W"] and chain tail " --> FLOOR --> FINAL".
        // Without this, "S1 & S2 --> W --> FLOOR" would create duplicate W→FLOOR edges.
        var chainTail = "";
        var rightNodesRaw = rightPart;
        if (leftNodes.Count > 1 || rightPart.Contains('&'))
        {
            // Find where the chain continues (next arrow after the immediate right nodes)
            var immediateEnd = FindChainTailStart(rightPart, arrowPatterns);
            if (immediateEnd >= 0)
            {
                rightNodesRaw = rightPart[..immediateEnd].Trim();
                chainTail = rightPart[immediateEnd..];
            }
        }

        var rightNodes = SplitAmpersand(rightNodesRaw);

        var result = new List<string>();
        foreach (var left in leftNodes)
        {
            foreach (var right in rightNodes)
            {
                result.Add($"{left.Trim()} {arrow}{label} {right.Trim()}");
            }
        }

        // Emit chain tail once, connected from the last right-side node.
        // e.g. for "S1 & S2 --> W --> FLOOR --> FINAL", emit "W --> FLOOR --> FINAL" once.
        if (chainTail.Length > 0 && rightNodes.Count > 0)
        {
            var lastRight = rightNodes[^1].Trim();
            var tailLine = $"{lastRight} {chainTail.TrimStart()}";
            // Recursively expand in case the tail also contains &
            result.AddRange(ExpandAmpersandSyntax(tailLine));
        }

        return result.Count > 0 ? result : [line];
    }

    /// <summary>
    /// Find the start index of a chain continuation in the right part of an &amp;-expanded edge.
    /// Skips past the immediate node(s) to find the next arrow.
    /// Returns -1 if no chain tail exists.
    /// </summary>
    static int FindChainTailStart(string rightPart, string[] arrowPatterns)
    {
        // Skip past the immediate node (handle brackets, quotes)
        var i = 0;
        var depth = 0;
        var inQuote = false;
        var foundNode = false;

        while (i < rightPart.Length)
        {
            var c = rightPart[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote)
            {
                if (c is '[' or '(' or '{') { depth++; foundNode = true; }
                else if (c is ']' or ')' or '}') depth--;
                else if (depth == 0 && c == '&')
                {
                    // Still in the immediate right nodes, skip past & groups
                    i++;
                    continue;
                }
                else if (depth == 0 && foundNode && c == ' ')
                {
                    // After closing a bracket, look for an arrow
                    foreach (var ap in arrowPatterns)
                    {
                        var remaining = rightPart.AsSpan(i);
                        var trimmedStart = 0;
                        while (trimmedStart < remaining.Length && remaining[trimmedStart] == ' ')
                            trimmedStart++;
                        if (trimmedStart < remaining.Length &&
                            remaining[trimmedStart..].StartsWith(ap.AsSpan(), StringComparison.Ordinal))
                        {
                            return i + trimmedStart;
                        }
                    }
                }
                else if (depth == 0 && !foundNode && !char.IsWhiteSpace(c))
                {
                    foundNode = true;
                }
            }
            i++;
        }

        // Also check for bare identifiers (no brackets) followed by arrows
        // e.g. "W --> FLOOR" where W has no brackets
        if (depth == 0)
        {
            foreach (var ap in arrowPatterns)
            {
                // Find the arrow, but skip the very start (that's the node itself)
                var searchStart = 1;
                while (searchStart < rightPart.Length)
                {
                    var idx = rightPart.IndexOf(ap, searchStart, StringComparison.Ordinal);
                    if (idx < 0) break;
                    // Make sure this isn't inside brackets
                    var d = 0;
                    var q = false;
                    for (var j = 0; j < idx; j++)
                    {
                        var ch = rightPart[j];
                        if (ch == '"') q = !q;
                        else if (!q)
                        {
                            if (ch is '[' or '(' or '{') d++;
                            else if (ch is ']' or ')' or '}') d--;
                        }
                    }
                    if (d == 0 && !q)
                        return idx;
                    searchStart = idx + 1;
                }
            }
        }

        return -1;
    }

    static List<string> SplitAmpersand(string part)
    {
        // Split on & but respect brackets and quotes
        var results = new List<string>();
        var depth = 0;
        var inQuote = false;
        var start = 0;

        for (var i = 0; i < part.Length; i++)
        {
            var c = part[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote)
            {
                if (c is '[' or '(' or '{') depth++;
                else if (c is ']' or ')' or '}') depth--;
                else if (c == '&' && depth == 0)
                {
                    results.Add(part[start..i]);
                    start = i + 1;
                }
            }
        }
        results.Add(part[start..]);
        return results.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    static bool TryParseStatement(string line,
        out (List<Node> Nodes, List<(EdgeType Type, EdgeStyle Style, string? Label)> Edges) statement)
    {
        var result = StandaloneStatementParser.Parse(line);
        if (!result.Success)
        {
            statement = default;
            return false;
        }

        statement = result.Value;
        return true;
    }

    static List<string> SplitLines(string input)
    {
        var normalized = input.Replace("\r\n", "\n", StringComparison.Ordinal);
        var semicolonExpanded = ExpandSemicolonStatements(normalized);
        return semicolonExpanded.Split('\n').ToList();
    }

    static string ExpandSemicolonStatements(string input)
    {
        if (!input.Contains(';', StringComparison.Ordinal))
        {
            return input;
        }

        var lines = input.Split('\n');
        var output = new List<string>(lines.Length * 2);

        foreach (var line in lines)
        {
            if (!line.Contains(';', StringComparison.Ordinal))
            {
                output.Add(line);
                continue;
            }

            var segments = SplitBySemicolonOutsideQuotes(line);
            var added = false;
            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                output.Add(trimmed);
                added = true;
            }

            if (!added)
            {
                output.Add(string.Empty);
            }
        }

        return string.Join('\n', output);
    }

    static IEnumerable<string> SplitBySemicolonOutsideQuotes(string input)
    {
        var current = new StringBuilder();
        var inDoubleQuote = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(ch);
                continue;
            }

            if (ch == ';' && !inDoubleQuote)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        yield return current.ToString();
    }

    static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }

    static readonly Regex StyleDirectivePattern =
        new(@"^style\s+(\S+)\s+(.+)$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static bool TryParseStyleDirective(string line, out string nodeId, out Style style)
    {
        var match = StyleDirectivePattern.Match(line);
        if (!match.Success)
        {
            nodeId = string.Empty;
            style = default!;
            return false;
        }

        nodeId = match.Groups[1].Value;
        style = ParseStyleProperties(match.Groups[2].Value);
        return true;
    }

    static readonly Regex ClassDefPattern =
        new(@"^classDef\s+(\S+)\s+(.+)$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static bool TryParseClassDefDirective(string line, out string className, out Style style)
    {
        var match = ClassDefPattern.Match(line);
        if (!match.Success)
        {
            className = string.Empty;
            style = default!;
            return false;
        }

        className = match.Groups[1].Value;
        style = ParseStyleProperties(match.Groups[2].Value);
        return true;
    }

    static readonly Regex ClassAssignmentPattern =
        new(@"^class\s+(\S+)\s+(\S+)$", RegexOptions.IgnoreCase | RegexCompat.Compiled);

    static readonly Regex CssColorPattern =
        new(
            @"^(?:#[0-9a-fA-F]{3,8}|rgba?\(\s*[\d.\s,%]+\)|hsla?\(\s*[\d.\s,%]+\)|[a-zA-Z][a-zA-Z0-9-]{0,31})$",
            RegexOptions.CultureInvariant | RegexCompat.Compiled);

    static readonly Regex DashArrayPattern =
        new(@"^[0-9.,\s-]{1,64}$", RegexOptions.CultureInvariant | RegexCompat.Compiled);

    static readonly Regex FontFamilyPattern =
        new(@"^[a-zA-Z0-9 ,_-]{1,80}$", RegexOptions.CultureInvariant | RegexCompat.Compiled);

    static readonly Regex FontWeightPattern =
        new(@"^(?:normal|bold|bolder|lighter|[1-9]00)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexCompat.Compiled);

    static bool TryParseClassAssignment(string line, out string nodeIds, out string className)
    {
        var match = ClassAssignmentPattern.Match(line);
        if (!match.Success)
        {
            nodeIds = string.Empty;
            className = string.Empty;
            return false;
        }

        nodeIds = match.Groups[1].Value;
        className = match.Groups[2].Value;
        return true;
    }

    static Style ParseStyleProperties(string properties)
    {
        var style = new Style();
        foreach (var prop in properties.Split(',', StringSplitOptions.TrimEntries))
        {
            var colonIdx = prop.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = prop[..colonIdx].Trim().ToLowerInvariant();
            var value = prop[(colonIdx + 1)..].Trim();

            switch (key)
            {
                case "fill":
                    style.Fill = SanitizeColorToken(value);
                    break;
                case "stroke":
                    style.Stroke = SanitizeColorToken(value);
                    break;
                case "stroke-width" when double.TryParse(value.TrimEnd('p', 'x'), out var sw):
                    style.StrokeWidth = sw; break;
                case "stroke-dasharray":
                    style.StrokeDasharray = SanitizeDashArray(value);
                    break;
                case "color":
                    style.TextColor = SanitizeColorToken(value);
                    break;
                case "font-family":
                    style.FontFamily = SanitizeFontFamily(value);
                    break;
                case "font-size" when double.TryParse(value.TrimEnd('p', 'x'), out var fs):
                    style.FontSize = fs; break;
                case "font-weight":
                    style.FontWeight = SanitizeFontWeight(value);
                    break;
            }
        }

        return style;
    }

    static string? SanitizeColorToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(['"', '\'', '<', '>', ';']) >= 0)
            return null;

        if (trimmed.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("expression", StringComparison.OrdinalIgnoreCase))
            return null;

        return CssColorPattern.IsMatch(trimmed) ? trimmed : null;
    }

    static string? SanitizeDashArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return DashArrayPattern.IsMatch(trimmed) ? trimmed : null;
    }

    static string? SanitizeFontFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return FontFamilyPattern.IsMatch(trimmed) ? trimmed : null;
    }

    static string? SanitizeFontWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return FontWeightPattern.IsMatch(trimmed) ? trimmed : null;
    }

    static void ApplyStyles(FlowchartModel model, List<(string NodeId, Style Style)> styles)
    {
        if (styles.Count == 0) return;

        var nodeById = model.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var (nodeId, style) in styles)
        {
            if (nodeById.TryGetValue(nodeId, out var node))
            {
                // Merge style properties onto the node's existing style
                if (style.Fill != null) node.Style.Fill = style.Fill;
                if (style.Stroke != null) node.Style.Stroke = style.Stroke;
                if (style.StrokeWidth.HasValue) node.Style.StrokeWidth = style.StrokeWidth;
                if (style.StrokeDasharray != null) node.Style.StrokeDasharray = style.StrokeDasharray;
                if (style.TextColor != null) node.Style.TextColor = style.TextColor;
                if (style.FontFamily != null) node.Style.FontFamily = style.FontFamily;
                if (style.FontSize.HasValue) node.Style.FontSize = style.FontSize;
                if (style.FontWeight != null) node.Style.FontWeight = style.FontWeight;
            }
        }
    }

    sealed class ParsedSubgraph(string id, string title)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public Direction Direction { get; set; } = Direction.TopToBottom;
        public string? ParentId { get; set; }
        public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);
        public List<string> NestedSubgraphIds { get; } = [];
    }
}
