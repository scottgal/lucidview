namespace MermaidSharp.Formats;

public class TlpGraph
{
    public string Version { get; set; } = "2.3";
    public List<TlpNode> Nodes { get; } = [];
    public List<TlpEdge> Edges { get; } = [];
    public List<TlpCluster> Clusters { get; } = [];
    public Dictionary<string, TlpProperty> Properties { get; } = new();
    public string? Author { get; set; }
    public string? Date { get; set; }
    public string? Comments { get; set; }
}

public class TlpNode
{
    public required int Id { get; init; }
    public Dictionary<string, object> Properties { get; } = new();
}

public class TlpEdge
{
    public required int Id { get; init; }
    public required int Source { get; init; }
    public required int Target { get; init; }
    public Dictionary<string, object> Properties { get; } = new();
}

public class TlpCluster
{
    public required int Id { get; init; }
    public string? Name { get; set; }
    public List<int> NodeIds { get; } = [];
    public List<int> EdgeIds { get; } = [];
    public List<TlpCluster> SubClusters { get; } = [];
}

public class TlpProperty
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? DefaultNodeValue { get; set; }
    public string? DefaultEdgeValue { get; set; }
    public Dictionary<int, object> NodeValues { get; } = new();
    public Dictionary<int, object> EdgeValues { get; } = new();
}

public enum TlpPropertyType
{
    Bool,
    Color,
    Double,
    Int,
    Layout,
    Size,
    String,
    VectorBool,
    VectorColor,
    VectorCoord,
    VectorDouble,
    VectorInt,
    VectorSize,
    VectorString
}
