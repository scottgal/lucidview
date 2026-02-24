using System.Globalization;
using System.Text;

namespace MermaidSharp.Formats;

public class TlpParser
{
    public static TlpGraph Parse(string input)
    {
        var tokenizer = new TlpTokenizer(input);
        var tokens = tokenizer.Tokenize();

        if (tokens.Count == 0 || !IsOpeningParen(tokens[0]))
            throw new FormatException("TLP file must start with (");

        var index = 0;
        var graph = ParseSExpression(tokens, ref index);

        return BuildGraph(graph);
    }

    static bool IsOpeningParen(Token t) => t.Type == TokenType.Paren && t.Value == "(";
    static bool IsClosingParen(Token t) => t.Type == TokenType.Paren && t.Value == ")";

    static List<object> ParseSExpression(List<Token> tokens, ref int index)
    {
        if (!IsOpeningParen(tokens[index]))
            throw new FormatException($"Expected '(' at position {index}");
        index++;

        var result = new List<object>(16);
        while (index < tokens.Count && !IsClosingParen(tokens[index]))
        {
            if (IsOpeningParen(tokens[index]))
                result.Add(ParseSExpression(tokens, ref index));
            else
            {
                result.Add(tokens[index].Value);
                index++;
            }
        }

        if (index >= tokens.Count)
            throw new FormatException("Unexpected end of file - missing ')'");
        index++;

        return result;
    }

    static TlpGraph BuildGraph(List<object> sexpr)
    {
        var graph = new TlpGraph();

        if (sexpr.Count == 0 || !sexpr[0].Equals("tlp"))
            throw new FormatException("Root expression must be (tlp \"version\" ...)");

        if (sexpr.Count > 1 && sexpr[1] is string version)
            graph.Version = version.Trim('"');

        for (var i = 2; i < sexpr.Count; i++)
        {
            if (sexpr[i] is List<object> expr)
                ProcessTopLevel(graph, expr);
        }

        return graph;
    }

    static void ProcessTopLevel(TlpGraph graph, List<object> expr)
    {
        if (expr.Count == 0) return;

        var command = expr[0] as string;
        switch (command)
        {
            case "nodes":
                ProcessNodes(graph, expr);
                break;
            case "nb_nodes":
                break;
            case "edge":
                ProcessEdge(graph, expr);
                break;
            case "cluster":
                graph.Clusters.Add(ProcessCluster(expr));
                break;
            case "property":
                ProcessProperty(graph, expr);
                break;
            case "date":
                if (expr.Count > 1) graph.Date = expr[1]?.ToString()?.Trim('"');
                break;
            case "author":
                if (expr.Count > 1) graph.Author = expr[1]?.ToString()?.Trim('"');
                break;
            case "comments":
                if (expr.Count > 1) graph.Comments = expr[1]?.ToString()?.Trim('"');
                break;
        }
    }

    static void ProcessNodes(TlpGraph graph, List<object> expr)
    {
        for (var i = 1; i < expr.Count; i++)
        {
            if (expr[i] is not string s) continue;

            var dotIdx = s.IndexOf("..");
            if (dotIdx >= 0)
            {
                if (int.TryParse(s.AsSpan(0, dotIdx), out var start) &&
                    int.TryParse(s.AsSpan(dotIdx + 2), out var end))
                {
                    var count = end - start + 1;
                    graph.Nodes.Capacity = Math.Max(graph.Nodes.Capacity, graph.Nodes.Count + count);
                    for (var n = start; n <= end; n++)
                        graph.Nodes.Add(new TlpNode { Id = n });
                }
            }
            else if (int.TryParse(s, out var id))
            {
                graph.Nodes.Add(new TlpNode { Id = id });
            }
        }
    }

    static void ProcessEdge(TlpGraph graph, List<object> expr)
    {
        if (expr.Count < 4) return;
        if (int.TryParse(expr[1]?.ToString(), out var id) &&
            int.TryParse(expr[2]?.ToString(), out var source) &&
            int.TryParse(expr[3]?.ToString(), out var target))
        {
            graph.Edges.Add(new TlpEdge { Id = id, Source = source, Target = target });
        }
    }

    static TlpCluster ProcessCluster(List<object> expr)
    {
        var cluster = new TlpCluster { Id = int.Parse(expr[1]?.ToString() ?? "0") };

        for (var i = 2; i < expr.Count; i++)
        {
            if (expr[i] is not List<object> sub || sub.Count == 0) continue;

            var cmd = sub[0] as string;
            switch (cmd)
            {
                case "nodes":
                    ProcessClusterNodes(cluster, sub);
                    break;
                case "edges":
                    ProcessClusterEdges(cluster, sub);
                    break;
                case "cluster":
                    cluster.SubClusters.Add(ProcessCluster(sub));
                    break;
            }
        }

        return cluster;
    }

    static void ProcessClusterNodes(TlpCluster cluster, List<object> expr)
    {
        for (var i = 1; i < expr.Count; i++)
        {
            if (expr[i] is not string s) continue;

            var dotIdx = s.IndexOf("..");
            if (dotIdx >= 0)
            {
                if (int.TryParse(s.AsSpan(0, dotIdx), out var start) &&
                    int.TryParse(s.AsSpan(dotIdx + 2), out var end))
                {
                    for (var n = start; n <= end; n++)
                        cluster.NodeIds.Add(n);
                }
            }
            else if (int.TryParse(s, out var id))
            {
                cluster.NodeIds.Add(id);
            }
        }
    }

    static void ProcessClusterEdges(TlpCluster cluster, List<object> expr)
    {
        for (var i = 1; i < expr.Count; i++)
        {
            if (expr[i] is string s && int.TryParse(s, out var id))
                cluster.EdgeIds.Add(id);
        }
    }

    static void ProcessProperty(TlpGraph graph, List<object> expr)
    {
        if (expr.Count < 4) return;

        var name = expr[3]?.ToString()?.Trim('"') ?? "";
        var prop = new TlpProperty { Name = name, Type = expr[2]?.ToString() ?? "string" };

        for (var i = 4; i < expr.Count; i++)
        {
            if (expr[i] is not List<object> sub || sub.Count == 0) continue;

            var cmd = sub[0] as string;
            switch (cmd)
            {
                case "default":
                    if (sub.Count > 1) prop.DefaultNodeValue = sub[1]?.ToString()?.Trim('"');
                    if (sub.Count > 2) prop.DefaultEdgeValue = sub[2]?.ToString()?.Trim('"');
                    break;
                case "node":
                    if (sub.Count > 2 && int.TryParse(sub[1]?.ToString(), out var nodeId))
                        prop.NodeValues[nodeId] = ParseValue(prop.Type, sub[2]?.ToString()?.Trim('"') ?? "");
                    break;
                case "edge":
                    if (sub.Count > 2 && int.TryParse(sub[1]?.ToString(), out var edgeId))
                        prop.EdgeValues[edgeId] = ParseValue(prop.Type, sub[2]?.ToString()?.Trim('"') ?? "");
                    break;
            }
        }

        graph.Properties[name] = prop;
    }

    static object ParseValue(string type, string value) =>
        type switch
        {
            "bool" => value.Equals("true", StringComparison.OrdinalIgnoreCase),
            "int" => int.TryParse(value, out var i) ? i : 0,
            "double" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0,
            "color" => ParseColor(value),
            "size" => ParseSize(value),
            "layout" => ParseCoord(value),
            _ => value
        };

    public static (double R, double G, double B, double A) ParseColor(string value)
    {
        var start = value.IndexOf('(');
        var end = value.IndexOf(')');
        if (start < 0 || end < 0) return (0, 0, 0, 255);

        var span = value.AsSpan(start + 1, end - start - 1);
        Span<Range> ranges = stackalloc Range[4];
        var count = SplitByChar(span, ',', ranges);

        if (count >= 4 &&
            double.TryParse(span[ranges[0]], out var r) &&
            double.TryParse(span[ranges[1]], out var g) &&
            double.TryParse(span[ranges[2]], out var b) &&
            double.TryParse(span[ranges[3]], out var a))
        {
            return (r, g, b, a);
        }
        return (0, 0, 0, 255);
    }

    public static (double W, double H, double D) ParseSize(string value)
    {
        var start = value.IndexOf('(');
        var end = value.IndexOf(')');
        if (start < 0 || end < 0) return (1, 1, 1);

        var span = value.AsSpan(start + 1, end - start - 1);
        Span<Range> ranges = stackalloc Range[3];
        var count = SplitByChar(span, ',', ranges);

        if (count >= 3 &&
            double.TryParse(span[ranges[0]], out var w) &&
            double.TryParse(span[ranges[1]], out var h) &&
            double.TryParse(span[ranges[2]], out var d))
        {
            return (w, h, d);
        }
        return (1, 1, 1);
    }

    public static (double X, double Y, double Z) ParseCoord(string value)
    {
        var start = value.IndexOf('(');
        var end = value.IndexOf(')');
        if (start < 0 || end < 0) return (0, 0, 0);

        var span = value.AsSpan(start + 1, end - start - 1);
        Span<Range> ranges = stackalloc Range[3];
        var count = SplitByChar(span, ',', ranges);

        if (count >= 3 &&
            double.TryParse(span[ranges[0]], out var x) &&
            double.TryParse(span[ranges[1]], out var y) &&
            double.TryParse(span[ranges[2]], out var z))
        {
            return (x, y, z);
        }
        return (0, 0, 0);
    }

    static int SplitByChar(ReadOnlySpan<char> span, char delimiter, Span<Range> ranges)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i < span.Length && count < ranges.Length; i++)
        {
            if (span[i] == delimiter)
            {
                ranges[count++] = new Range(start, i);
                start = i + 1;
            }
        }
        if (count < ranges.Length)
            ranges[count++] = new Range(start, span.Length);
        return count;
    }
}

enum TokenType { Paren, String, Number, Identifier }

record Token(TokenType Type, string Value);

class TlpTokenizer
{
    readonly string _input;
    int _pos;

    public TlpTokenizer(string input)
    {
        _input = input;
        _pos = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>(256);

        while (_pos < _input.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _input.Length) break;

            var c = _input[_pos];

            if (c == '(' || c == ')')
            {
                tokens.Add(new Token(TokenType.Paren, c.ToString()));
                _pos++;
            }
            else if (c == '"')
            {
                tokens.Add(new Token(TokenType.String, ReadString()));
            }
            else if (char.IsDigit(c) || (c == '-' && _pos + 1 < _input.Length && char.IsDigit(_input[_pos + 1])))
            {
                tokens.Add(new Token(TokenType.Number, ReadNumber()));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var ident = ReadIdentifier();
                tokens.Add(double.TryParse(ident, out _)
                    ? new Token(TokenType.Number, ident)
                    : new Token(TokenType.Identifier, ident));
            }
            else
            {
                _pos++;
            }
        }

        return tokens;
    }

    void SkipWhitespaceAndComments()
    {
        while (_pos < _input.Length)
        {
            if (char.IsWhiteSpace(_input[_pos]))
            {
                _pos++;
            }
            else if (_input[_pos] == ';')
            {
                while (_pos < _input.Length && _input[_pos] != '\n')
                    _pos++;
            }
            else
            {
                break;
            }
        }
    }

    string ReadString()
    {
        _pos++;
        var start = _pos;
        while (_pos < _input.Length && _input[_pos] != '"')
        {
            if (_input[_pos] == '\\') _pos++;
            _pos++;
        }
        var result = _input.Substring(start, _pos - start);
        _pos++;
        return result;
    }

    string ReadNumber()
    {
        var start = _pos;
        if (_input[_pos] == '-') _pos++;
        while (_pos < _input.Length && (char.IsDigit(_input[_pos]) || _input[_pos] == '.'))
            _pos++;
        return _input.Substring(start, _pos - start);
    }

    string ReadIdentifier()
    {
        var start = _pos;
        while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_' || _input[_pos] == '.' || _input[_pos] == '-'))
            _pos++;
        return _input.Substring(start, _pos - start);
    }
}
