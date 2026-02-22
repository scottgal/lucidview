using System.Xml.Linq;

namespace MermaidSharp.Diagrams.Bpmn;

/// <summary>
/// Parses BPMN 2.0 XML into a FlowchartModel for rendering via the existing flowchart pipeline.
/// Supports processes, lanes, pools, tasks, events, gateways, and sequence/message flows.
/// </summary>
public static class BpmnParser
{
    static readonly XNamespace Bpmn20 = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    public static FlowchartModel Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new MermaidParseException("BPMN XML has no root element");

        // Detect namespace: either explicit bpmn: prefix or default namespace
        var ns = DetectNamespace(root);

        var model = new FlowchartModel { Direction = Direction.LeftToRight };
        var nodeDict = new Dictionary<string, Node>(StringComparer.Ordinal);
        var subgraphs = new List<Subgraph>();

        // Parse collaboration (pools)
        var collaboration = root.Element(ns + "collaboration");
        var poolProcessMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (collaboration != null)
        {
            foreach (var participant in collaboration.Elements(ns + "participant"))
            {
                var id = participant.Attribute("id")?.Value;
                var name = participant.Attribute("name")?.Value;
                var processRef = participant.Attribute("processRef")?.Value;
                if (id == null) continue;

                if (processRef != null)
                    poolProcessMap[processRef] = id;

                subgraphs.Add(new Subgraph
                {
                    Id = id,
                    Title = name ?? id
                });
            }

            // Parse message flows between pools
            foreach (var msgFlow in collaboration.Elements(ns + "messageFlow"))
            {
                var edgeId = msgFlow.Attribute("id")?.Value;
                var sourceRef = msgFlow.Attribute("sourceRef")?.Value;
                var targetRef = msgFlow.Attribute("targetRef")?.Value;
                var label = msgFlow.Attribute("name")?.Value;
                if (sourceRef == null || targetRef == null) continue;

                model.Edges.Add(new Edge
                {
                    SourceId = sourceRef,
                    TargetId = targetRef,
                    Label = label,
                    Type = EdgeType.DottedArrow,
                    LineStyle = EdgeStyle.Dotted
                });
            }
        }

        // Parse each process
        foreach (var process in root.Elements(ns + "process"))
        {
            var processId = process.Attribute("id")?.Value ?? "process";
            var poolSubgraph = poolProcessMap.TryGetValue(processId, out var poolId)
                ? subgraphs.Find(s => s.Id == poolId)
                : null;

            // Parse lane sets
            var laneSubgraphs = new List<Subgraph>();
            foreach (var laneSet in process.Elements(ns + "laneSet"))
            {
                foreach (var lane in laneSet.Elements(ns + "lane"))
                {
                    var laneId = lane.Attribute("id")?.Value ?? $"lane_{laneSubgraphs.Count}";
                    var laneName = lane.Attribute("name")?.Value ?? laneId;
                    var laneSg = new Subgraph { Id = laneId, Title = laneName };

                    foreach (var flowNodeRef in lane.Elements(ns + "flowNodeRef"))
                    {
                        var refId = flowNodeRef.Value.Trim();
                        if (!string.IsNullOrEmpty(refId))
                            laneSg.NodeIds.Add(refId);
                    }

                    laneSubgraphs.Add(laneSg);
                }
            }

            // Parse flow nodes: tasks, events, gateways, data objects
            ParseFlowNodes(process, ns, nodeDict, model);

            // Parse sequence flows
            foreach (var seqFlow in process.Elements(ns + "sequenceFlow"))
            {
                var sourceRef = seqFlow.Attribute("sourceRef")?.Value;
                var targetRef = seqFlow.Attribute("targetRef")?.Value;
                var label = seqFlow.Attribute("name")?.Value;
                if (sourceRef == null || targetRef == null) continue;

                // Check for condition expression → label
                if (string.IsNullOrEmpty(label))
                {
                    var condition = seqFlow.Element(ns + "conditionExpression");
                    if (condition != null)
                    {
                        var condText = condition.Value.Trim();
                        if (!string.IsNullOrEmpty(condText) && condText.Length < 50)
                            label = condText;
                    }
                }

                model.Edges.Add(new Edge
                {
                    SourceId = sourceRef,
                    TargetId = targetRef,
                    Label = label,
                    Type = EdgeType.Arrow,
                    LineStyle = EdgeStyle.Solid
                });
            }

            // Wire up lane subgraphs
            if (laneSubgraphs.Count > 0)
            {
                if (poolSubgraph != null)
                {
                    foreach (var laneSg in laneSubgraphs)
                        poolSubgraph.NestedSubgraphs.Add(laneSg);
                }
                else
                {
                    foreach (var laneSg in laneSubgraphs)
                        model.Subgraphs.Add(laneSg);
                }
            }

            // If pool subgraph has no lanes, assign all nodes to the pool
            if (poolSubgraph != null && laneSubgraphs.Count == 0)
            {
                foreach (var node in nodeDict.Values)
                    poolSubgraph.NodeIds.Add(node.Id);
            }
        }

        // Add pool-level subgraphs to model
        foreach (var sg in subgraphs)
        {
            if (!model.Subgraphs.Contains(sg))
                model.Subgraphs.Add(sg);
        }

        // Add all nodes to model
        foreach (var node in nodeDict.Values)
            model.Nodes.Add(node);

        // Set parent IDs on nodes for subgraph membership
        SetNodeParents(model);

        return model;
    }

    static void ParseFlowNodes(XElement process, XNamespace ns,
        Dictionary<string, Node> nodeDict, FlowchartModel model)
    {
        foreach (var element in process.Elements())
        {
            var localName = element.Name.LocalName;
            var id = element.Attribute("id")?.Value;
            if (id == null) continue;

            var name = element.Attribute("name")?.Value;

            var (shape, labelPrefix) = MapElementToShape(localName);
            if (shape == null) continue;

            var label = name;
            if (!string.IsNullOrEmpty(labelPrefix) && string.IsNullOrEmpty(name))
                label = labelPrefix;

            if (!nodeDict.ContainsKey(id))
            {
                nodeDict[id] = new Node
                {
                    Id = id,
                    Label = label,
                    Shape = shape.Value
                };
            }
        }
    }

    static (NodeShape? Shape, string? LabelPrefix) MapElementToShape(string localName) =>
        localName switch
        {
            // Events
            "startEvent" => (NodeShape.Circle, null),
            "endEvent" => (NodeShape.DoubleCircle, null),
            "intermediateThrowEvent" => (NodeShape.Circle, null),
            "intermediateCatchEvent" => (NodeShape.Circle, null),
            "boundaryEvent" => (NodeShape.Circle, null),

            // Tasks
            "task" => (NodeShape.RoundedRectangle, null),
            "userTask" => (NodeShape.RoundedRectangle, null),
            "serviceTask" => (NodeShape.RoundedRectangle, null),
            "scriptTask" => (NodeShape.RoundedRectangle, null),
            "businessRuleTask" => (NodeShape.RoundedRectangle, null),
            "sendTask" => (NodeShape.RoundedRectangle, null),
            "receiveTask" => (NodeShape.RoundedRectangle, null),
            "manualTask" => (NodeShape.RoundedRectangle, null),
            "callActivity" => (NodeShape.RoundedRectangle, null),
            "subProcess" => (NodeShape.RoundedRectangle, null),

            // Gateways
            "exclusiveGateway" => (NodeShape.Diamond, null),
            "parallelGateway" => (NodeShape.Diamond, "+"),
            "inclusiveGateway" => (NodeShape.Diamond, "\u25CB"),   // ○
            "eventBasedGateway" => (NodeShape.Diamond, "\u2B21"),  // ⬡
            "complexGateway" => (NodeShape.Diamond, "*"),

            // Data
            "dataObjectReference" => (NodeShape.Document, null),
            "dataStoreReference" => (NodeShape.Cylinder, null),
            "dataObject" => (NodeShape.Document, null),

            // Ignore non-visual elements
            _ => (null, null)
        };

    static XNamespace DetectNamespace(XElement root)
    {
        // Check for explicit bpmn: prefix
        var bpmnNs = root.GetNamespaceOfPrefix("bpmn");
        if (bpmnNs != null) return bpmnNs;

        // Check default namespace
        if (root.Name.Namespace == Bpmn20) return Bpmn20;
        if (root.Name.Namespace != XNamespace.None) return root.Name.Namespace;

        // Fallback: try without namespace (unprefixed elements)
        return XNamespace.None;
    }

    static void SetNodeParents(FlowchartModel model)
    {
        void SetParents(Subgraph sg)
        {
            foreach (var nodeId in sg.NodeIds)
            {
                if (model.Nodes.Find(n => n.Id == nodeId) is { } node)
                    node.ParentId = sg.Id;
            }

            foreach (var nested in sg.NestedSubgraphs)
                SetParents(nested);
        }

        foreach (var sg in model.Subgraphs)
            SetParents(sg);
    }
}
