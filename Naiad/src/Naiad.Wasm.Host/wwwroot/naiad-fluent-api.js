import { NaiadClient } from "./naiad-client.js";

export const Direction = {
  TopToBottom: "TB",
  BottomToTop: "BT",
  LeftToRight: "LR",
  RightToLeft: "RL"
};

export const NodeShape = {
  Rectangle: "Rectangle",
  RoundedRect: "RoundedRect",
  Stadium: "Stadium",
  Subroutine: "Subroutine",
  Cylinder: "Cylinder",
  Circle: "Circle",
  DoubleCircle: "DoubleCircle",
  Asymmetric: "Asymmetric",
  Diamond: "Diamond",
  Hexagon: "Hexagon",
  Parallelogram: "Parallelogram",
  ParallelogramAlt: "ParallelogramAlt",
  Document: "Document",
  Trapezoid: "Trapezoid",
  TriangleUp: "TriangleUp",
  TriangleDown: "TriangleDown",
  Hourglass: "Hourglass",
  Text: "Text"
};

export const EdgeType = {
  Arrow: "Arrow",
  Open: "Open",
  DottedArrow: "DottedArrow",
  Dotted: "Dotted",
  ThickArrow: "ThickArrow",
  Thick: "Thick",
  CircleEnd: "CircleEnd",
  CrossEnd: "CrossEnd",
  BiDirectional: "BiDirectional",
  BiDirectionalCircle: "BiDirectionalCircle",
  BiDirectionalCross: "BiDirectionalCross"
};

export const DiagramType = {
  Flowchart: "Flowchart",
  Sequence: "Sequence",
  Class: "Class",
  State: "State",
  EntityRelationship: "EntityRelationship",
  UserJourney: "UserJourney",
  Gantt: "Gantt",
  Pie: "Pie",
  Quadrant: "Quadrant",
  Requirement: "Requirement",
  GitGraph: "GitGraph",
  Mindmap: "Mindmap",
  Timeline: "Timeline",
  C4: "C4",
  Sankey: "Sankey",
  Block: "Block",
  Packet: "Packet",
  Architecture: "Architecture",
  Xychart: "Xychart",
  Treemap: "Treemap"
};

export class FlowchartNode {
  id;
  text = null;
  shape = NodeShape.Rectangle;
  cssClass = null;
  link = null;
  tooltip = null;
  metadata = null;

  constructor(id, text = null, shape = NodeShape.Rectangle) {
    this.id = id;
    this.text = text;
    this.shape = shape;
  }
}

export class FlowchartEdge {
  from;
  to;
  type = EdgeType.Arrow;
  label = null;
  minLength = 1;

  constructor(from, to, type = EdgeType.Arrow, label = null) {
    this.from = from;
    this.to = to;
    this.type = type;
    this.label = label;
  }
}

export class FlowchartSubgraph {
  id;
  title;
  direction = null;
  nodes = [];
  edges = [];
  subgraphs = [];
  cssClass = null;

  constructor(id, title) {
    this.id = id;
    this.title = title;
  }
}

export class FlowchartClassDef {
  name;
  properties = new Map();

  constructor(name) {
    this.name = name;
  }
}

export class FlowchartDiagram {
  direction = Direction.TopToBottom;
  nodes = [];
  edges = [];
  subgraphs = [];
  classDefs = [];
  classAssignments = [];
  #client = null;
  #renderOptions = null;

  constructor(direction = Direction.TopToBottom) {
    this.direction = direction;
  }

  toMermaid(options = null) {
    return serializeFlowchart(this, options);
  }

  async renderSvg(client = null, renderOptions = null) {
    const naiadClient = client || this.#client || await getDefaultClient();
    const mermaid = this.toMermaid();
    return naiadClient.renderSvg(mermaid, renderOptions || this.#renderOptions);
  }

  async renderToElement(element, client = null, renderOptions = null) {
    const svg = await this.renderSvg(client, renderOptions);
    if (typeof element === "string") {
      element = document.querySelector(element);
    }
    if (element) {
      element.innerHTML = sanitizeSvgForDom(svg);
    }
    return svg;
  }

  async toReactFlow(client = null, renderOptions = null) {
    const naiadClient = client || this.#client || await getDefaultClient();
    const mermaid = this.toMermaid();
    return naiadClient.renderReactFlow(mermaid, renderOptions || this.#renderOptions);
  }

  async renderReactFlowTo(element, options = null, client = null, renderOptions = null) {
    const reactFlowData = await this.toReactFlow(client, renderOptions);
    if (typeof element === "string") {
      element = document.querySelector(element);
    }
    if (element && window.React && window.xyflowReact) {
      renderReactFlowComponent(element, reactFlowData, options);
    }
    return reactFlowData;
  }

  withClient(client) {
    this.#client = client;
    return this;
  }

  withRenderOptions(options) {
    this.#renderOptions = options;
    return this;
  }

  findNode(id) {
    return this.nodes.find(n => n.id === id) || null;
  }

  findEdgesFrom(nodeId) {
    return this.edges.filter(e => e.from === nodeId);
  }

  findEdgesTo(nodeId) {
    return this.edges.filter(e => e.to === nodeId);
  }

  findSubgraph(id) {
    return this.subgraphs.find(s => s.id === id) || null;
  }
}

export class FlowchartBuilder {
  #nodes = new Map();
  #nodeOrder = [];
  #edges = [];
  #subgraphs = [];
  #classDefs = new Map();
  #classDefOrder = [];
  #classAssignments = [];
  #direction = Direction.TopToBottom;
  #title = null;
  #client = null;
  #renderOptions = null;

  constructor(direction = Direction.TopToBottom) {
    this.#direction = direction;
  }

  direction(dir) {
    this.#direction = dir;
    return this;
  }

  title(value) {
    this.#title = value;
    return this;
  }

  node(id, text = null, shape = NodeShape.Rectangle) {
    if (!this.#nodes.has(id)) {
      this.#nodeOrder.push(id);
    }
    this.#nodes.set(id, new FlowchartNode(id, text, shape));
    return this;
  }

  nodeWithConfig(id, config) {
    if (!this.#nodes.has(id)) {
      this.#nodeOrder.push(id);
    }
    const node = new FlowchartNode(id, config.text ?? null, config.shape ?? NodeShape.Rectangle);
    node.cssClass = config.cssClass ?? null;
    node.link = config.link ?? null;
    node.tooltip = config.tooltip ?? null;
    node.metadata = config.metadata ?? null;
    this.#nodes.set(id, node);
    return this;
  }

  nodes(...nodeDefs) {
    for (const def of nodeDefs) {
      if (typeof def === "string") {
        this.node(def);
      } else if (Array.isArray(def)) {
        this.node(def[0], def[1], def[2]);
      } else {
        this.nodeWithConfig(def.id, def);
      }
    }
    return this;
  }

  edge(from, to, type = EdgeType.Arrow, label = null) {
    this.#edges.push(new FlowchartEdge(from, to, type, label));
    return this;
  }

  edgeWithConfig(from, to, config) {
    const edge = new FlowchartEdge(from, to, config.type ?? EdgeType.Arrow, config.label ?? null);
    edge.minLength = config.minLength ?? 1;
    this.#edges.push(edge);
    return this;
  }

  connect(...connections) {
    for (const conn of connections) {
      if (typeof conn === "string") {
        const parts = conn.split(/\s*->\s*|\s*-->\s*/);
        if (parts.length === 2) {
          this.edge(parts[0].trim(), parts[1].trim());
        }
      } else if (Array.isArray(conn)) {
        this.edge(conn[0], conn[1], conn[2] ?? EdgeType.Arrow, conn[3] ?? null);
      } else {
        this.edgeWithConfig(conn.from, conn.to, conn);
      }
    }
    return this;
  }

  subgraph(id, title, configure) {
    const builder = new SubgraphBuilder(id, title);
    configure(builder);
    this.#subgraphs.push(builder.build());
    return this;
  }

  classDef(name, properties) {
    if (!this.#classDefs.has(name)) {
      this.#classDefOrder.push(name);
    }
    const classDef = new FlowchartClassDef(name);
    if (typeof properties === "string") {
      const pairs = properties.split(",").map(p => p.trim().split(":").map(s => s.trim()));
      for (const [key, value] of pairs) {
        if (key && value) classDef.properties.set(key, value);
      }
    } else {
      for (const [key, value] of Object.entries(properties)) {
        classDef.properties.set(key, String(value));
      }
    }
    this.#classDefs.set(name, classDef);
    return this;
  }

  class(nodeId, ...classNames) {
    for (const className of classNames) {
      if (className) {
        this.#classAssignments.push({ targetId: nodeId, className });
      }
    }
    return this;
  }

  style(nodeId, styles) {
    const styleStr = typeof styles === "string"
      ? styles
      : Object.entries(styles).map(([k, v]) => `${k}:${v}`).join(",");
    return this.classDef(`__style_${nodeId}`, styleStr).class(nodeId, `__style_${nodeId}`);
  }

  withClient(client) {
    this.#client = client;
    return this;
  }

  withRenderOptions(options) {
    this.#renderOptions = options;
    return this;
  }

  build() {
    const diagram = new FlowchartDiagram(this.#direction);
    diagram.nodes = this.#nodeOrder.map(id => this.#nodes.get(id));
    diagram.edges = [...this.#edges];
    diagram.subgraphs = [...this.#subgraphs];
    diagram.classDefs = this.#classDefOrder.map(name => this.#classDefs.get(name));
    diagram.classAssignments = [...this.#classAssignments];
    if (this.#client) {
      diagram.withClient(this.#client);
    }
    if (this.#renderOptions) {
      diagram.withRenderOptions(this.#renderOptions);
    }
    return diagram;
  }

  async buildAndRender(element, options = null) {
    const diagram = this.build();
    await diagram.renderToElement(element, null, options);
    return diagram;
  }

  async buildAndRenderReactFlow(element, options = null, renderOptions = null) {
    const diagram = this.build();
    const data = await diagram.renderReactFlowTo(element, options, null, renderOptions);
    return { diagram, data };
  }

  async buildAndToReactFlow(renderOptions = null) {
    const diagram = this.build();
    const data = await diagram.toReactFlow(null, renderOptions);
    return { diagram, data };
  }
}

export class SubgraphBuilder {
  #id;
  #title;
  #direction = null;
  #nodes = new Map();
  #nodeOrder = [];
  #edges = [];
  #subgraphs = [];

  constructor(id, title) {
    this.#id = id;
    this.#title = title;
  }

  direction(dir) {
    this.#direction = dir;
    return this;
  }

  node(id, text = null, shape = NodeShape.Rectangle) {
    if (!this.#nodes.has(id)) {
      this.#nodeOrder.push(id);
    }
    this.#nodes.set(id, new FlowchartNode(id, text, shape));
    return this;
  }

  edge(from, to, type = EdgeType.Arrow, label = null) {
    this.#edges.push(new FlowchartEdge(from, to, type, label));
    return this;
  }

  subgraph(id, title, configure) {
    const builder = new SubgraphBuilder(id, title);
    configure(builder);
    this.#subgraphs.push(builder.build());
    return this;
  }

  build() {
    const subgraph = new FlowchartSubgraph(this.#id, this.#title);
    subgraph.direction = this.#direction;
    subgraph.nodes = this.#nodeOrder.map(id => this.#nodes.get(id));
    subgraph.edges = [...this.#edges];
    subgraph.subgraphs = [...this.#subgraphs];
    return subgraph;
  }
}

export class SequenceParticipant {
  actor;
  name = null;
  type = "participant";

  constructor(actor, name = null) {
    this.actor = actor;
    this.name = name;
  }
}

export class SequenceMessage {
  from;
  to;
  text;
  type = "solid";
  label = null;

  constructor(from, to, text, type = "solid") {
    this.from = from;
    this.to = to;
    this.text = text;
    this.type = type;
  }
}

export class SequenceDiagram {
  participants = [];
  messages = [];
  notes = [];
  loops = [];
  alts = [];
  opts = [];
  parallels = [];
  autNumber = false;
  #client = null;
  #renderOptions = null;

  toMermaid(options = null) {
    return serializeSequence(this, options);
  }

  async renderSvg(client = null, renderOptions = null) {
    const naiadClient = client || this.#client || await getDefaultClient();
    const mermaid = this.toMermaid();
    return naiadClient.renderSvg(mermaid, renderOptions || this.#renderOptions);
  }

  withClient(client) {
    this.#client = client;
    return this;
  }

  withRenderOptions(options) {
    this.#renderOptions = options;
    return this;
  }
}

export class SequenceBuilder {
  #participants = [];
  #messages = [];
  #notes = [];
  #loops = [];
  #alts = [];
  #opts = [];
  #parallels = [];
  #autNumber = false;
  #client = null;
  #renderOptions = null;

  participant(actor, name = null) {
    this.#participants.push(new SequenceParticipant(actor, name));
    return this;
  }

  actor(actor, name = null) {
    const p = new SequenceParticipant(actor, name);
    p.type = "actor";
    this.#participants.push(p);
    return this;
  }

  message(from, to, text, type = "solid") {
    this.#messages.push(new SequenceMessage(from, to, text, type));
    return this;
  }

  arrow(from, to, text) {
    return this.message(from, to, text, "solid");
  }

  dottedArrow(from, to, text) {
    return this.message(from, to, text, "dotted");
  }

  note(position, actor, text) {
    this.#notes.push({ position, actor, text });
    return this;
  }

  noteOver(actors, text) {
    this.#notes.push({ position: "over", actors: Array.isArray(actors) ? actors : [actors], text });
    return this;
  }

  loop(condition, configure) {
    const builder = new SequenceLoopBuilder(condition);
    configure(builder);
    this.#loops.push(builder.build());
    return this;
  }

  alt(conditions, configure) {
    const builder = new SequenceAltBuilder();
    for (const [condition, block] of Object.entries(conditions)) {
      builder.branch(condition, block);
    }
    if (configure) configure(builder);
    this.#alts.push(builder.build());
    return this;
  }

  opt(condition, configure) {
    const builder = new SequenceOptBuilder(condition);
    configure(builder);
    this.#opts.push(builder.build());
    return this;
  }

  par(...branches) {
    const builder = new SequenceParBuilder();
    for (const [label, configure] of branches) {
      builder.branch(label, configure);
    }
    this.#parallels.push(builder.build());
    return this;
  }

  autNumber(enabled = true) {
    this.#autNumber = enabled;
    return this;
  }

  withClient(client) {
    this.#client = client;
    return this;
  }

  withRenderOptions(options) {
    this.#renderOptions = options;
    return this;
  }

  build() {
    const diagram = new SequenceDiagram();
    diagram.participants = [...this.#participants];
    diagram.messages = [...this.#messages];
    diagram.notes = [...this.#notes];
    diagram.loops = [...this.#loops];
    diagram.alts = [...this.#alts];
    diagram.opts = [...this.#opts];
    diagram.parallels = [...this.#parallels];
    diagram.autNumber = this.#autNumber;
    if (this.#client) diagram.withClient(this.#client);
    if (this.#renderOptions) diagram.withRenderOptions(this.#renderOptions);
    return diagram;
  }
}

class SequenceLoopBuilder {
  #condition;
  #messages = [];

  constructor(condition) {
    this.#condition = condition;
  }

  message(from, to, text, type = "solid") {
    this.#messages.push(new SequenceMessage(from, to, text, type));
    return this;
  }

  build() {
    return { condition: this.#condition, messages: [...this.#messages] };
  }
}

class SequenceAltBuilder {
  #branches = [];

  branch(condition, configure) {
    const messages = [];
    const builder = {
      message: (from, to, text, type = "solid") => {
        messages.push(new SequenceMessage(from, to, text, type));
        return builder;
      }
    };
    if (configure) configure(builder);
    this.#branches.push({ condition, messages });
    return this;
  }

  build() {
    return { branches: [...this.#branches] };
  }
}

class SequenceOptBuilder {
  #condition;
  #messages = [];

  constructor(condition) {
    this.#condition = condition;
  }

  message(from, to, text, type = "solid") {
    this.#messages.push(new SequenceMessage(from, to, text, type));
    return this;
  }

  build() {
    return { condition: this.#condition, messages: [...this.#messages] };
  }
}

class SequenceParBuilder {
  #branches = [];

  branch(label, configure) {
    const messages = [];
    const builder = {
      message: (from, to, text, type = "solid") => {
        messages.push(new SequenceMessage(from, to, text, type));
        return builder;
      }
    };
    if (configure) configure(builder);
    this.#branches.push({ label, messages });
    return this;
  }

  build() {
    return { branches: [...this.#branches] };
  }
}

class ParseResult {
  success;
  value = null;
  error = null;
  diagnostics = [];

  constructor(success, value = null, error = null, diagnostics = []) {
    this.success = success;
    this.value = value;
    this.error = error;
    this.diagnostics = diagnostics;
  }

  static ok(value, diagnostics = []) {
    return new ParseResult(true, value, null, diagnostics);
  }

  static fail(error, diagnostics = []) {
    return new ParseResult(false, null, error, diagnostics);
  }
}

class SerializeResult {
  success;
  mermaid = null;
  error = null;

  constructor(success, mermaid = null, error = null) {
    this.success = success;
    this.mermaid = mermaid;
    this.error = error;
  }

  static ok(mermaid) {
    return new SerializeResult(true, mermaid, null);
  }

  static fail(error) {
    return new SerializeResult(false, null, error);
  }
}

let defaultClient = null;
let defaultClientPromise = null;

async function getDefaultClient() {
  if (defaultClient) return defaultClient;
  if (!defaultClientPromise) {
    defaultClientPromise = (async () => {
      const client = new NaiadClient();
      await client.init();
      defaultClient = client;
      return client;
    })();
  }
  return defaultClientPromise;
}

export class NaiadFluent {
  static Direction = Direction;
  static NodeShape = NodeShape;
  static EdgeType = EdgeType;
  static DiagramType = DiagramType;

  static flowchart(direction = Direction.TopToBottom, configure = null) {
    const builder = new FlowchartBuilder(direction);
    if (configure) configure(builder);
    return builder;
  }

  static sequence(configure = null) {
    const builder = new SequenceBuilder();
    if (configure) configure(builder);
    return builder;
  }

  static async client() {
    return getDefaultClient();
  }

  static async detectDiagramType(source) {
    const client = await getDefaultClient();
    return client.detectDiagramType(source);
  }

  static async parse(source) {
    const client = await getDefaultClient();
    const type = client.detectDiagramType(source);

    if (type === "Flowchart") {
      return parseFlowchartSource(source);
    }
    if (type === "Sequence") {
      return parseSequenceSource(source);
    }

    return ParseResult.ok({ type, source });
  }

  static async render(mermaid, options = null) {
    const client = await getDefaultClient();
    return client.renderSvg(mermaid, options);
  }

  static async renderToElement(element, mermaid, options = null) {
    const svg = await this.render(mermaid, options);
    if (typeof element === "string") {
      element = document.querySelector(element);
    }
    if (element) {
      element.innerHTML = sanitizeSvgForDom(svg);
    }
    return svg;
  }

  static trySerialize(diagram, options = null) {
    try {
      const mermaid = diagram.toMermaid(options);
      return SerializeResult.ok(mermaid);
    } catch (error) {
      return SerializeResult.fail(error.message);
    }
  }

  static fromMermaid(source) {
    const lines = source.trim().split("\n");
    const firstLine = lines[0].trim().toLowerCase();

    if (firstLine.startsWith("flowchart") || firstLine.startsWith("graph")) {
      return parseFlowchartSource(source);
    }
    if (firstLine.startsWith("sequencediagram")) {
      return parseSequenceSource(source);
    }

    return ParseResult.fail("Unsupported diagram type");
  }
}

function serializeFlowchart(diagram, options = null) {
  const indent = options?.indent ?? "    ";
  const nl = options?.newLine ?? "\n";
  const lines = [];

  lines.push(`flowchart ${diagram.direction}${nl}`);

  for (const node of diagram.nodes) {
    lines.push(`${indent}${serializeNode(node)}${nl}`);
  }

  for (const subgraph of diagram.subgraphs) {
    appendSubgraph(lines, subgraph, 1, indent, nl);
  }

  for (const edge of diagram.edges) {
    lines.push(`${indent}${serializeEdge(edge)}${nl}`);
  }

  for (const classDef of diagram.classDefs) {
    const props = Array.from(classDef.properties.entries())
      .map(([k, v]) => `${k}:${v}`)
      .join(",");
    lines.push(`${indent}classDef ${classDef.name} ${props}${nl}`);
  }

  for (const assignment of diagram.classAssignments) {
    lines.push(`${indent}class ${assignment.targetId} ${assignment.className}${nl}`);
  }

  return lines.join("").trimEnd();
}

function serializeNode(node) {
  if (!node.text) return node.id;
  const label = quoteText(node.text);
  return `${node.id}${shapeToSyntax(node.shape, label)}`;
}

function shapeToSyntax(shape, label) {
  switch (shape) {
    case NodeShape.RoundedRect: return `(${label})`;
    case NodeShape.Stadium: return `([${label}])`;
    case NodeShape.Subroutine: return `[[${label}]]`;
    case NodeShape.Cylinder: return `[(${label})]`;
    case NodeShape.Circle: return `((${label}))`;
    case NodeShape.DoubleCircle: return `(((${label})))`;
    case NodeShape.Asymmetric: return `>${label}]`;
    case NodeShape.Diamond: return `{${label}}`;
    case NodeShape.Hexagon: return `{{${label}}}`;
    case NodeShape.Parallelogram: return `[/${label}/]`;
    case NodeShape.ParallelogramAlt: return `[\\${label}\\]`;
    case NodeShape.Trapezoid: return `[/${label}\\]`;
    case NodeShape.Document: return `[${label}]`;
    case NodeShape.TriangleUp: return `((${label}))`;
    case NodeShape.TriangleDown: return `((${label}))`;
    case NodeShape.Hourglass: return `((${label}))`;
    case NodeShape.Text: return `${label}`;
    default: return `[${label}]`;
  }
}

function serializeEdge(edge) {
  const arrow = edgeTypeToArrow(edge.type);
  if (!edge.label) {
    return `${edge.from} ${arrow} ${edge.to}`;
  }
  return `${edge.from} ${arrow}|${escapeEdgeLabel(edge.label)}| ${edge.to}`;
}

function edgeTypeToArrow(type) {
  switch (type) {
    case EdgeType.Arrow: return "-->";
    case EdgeType.Open: return "---";
    case EdgeType.DottedArrow: return "-.->";
    case EdgeType.Dotted: return "-.-";
    case EdgeType.ThickArrow: return "==>";
    case EdgeType.Thick: return "===";
    case EdgeType.CircleEnd: return "--o";
    case EdgeType.CrossEnd: return "--x";
    case EdgeType.BiDirectional: return "<-->";
    case EdgeType.BiDirectionalCircle: return "o--o";
    case EdgeType.BiDirectionalCross: return "x--x";
    default: return "-->";
  }
}

function appendSubgraph(lines, subgraph, level, indent, nl) {
  const currentIndent = indent.repeat(level);
  const childIndent = indent.repeat(level + 1);

  lines.push(`${currentIndent}subgraph ${subgraph.id}${nl}`);

  if (subgraph.direction) {
    lines.push(`${childIndent}direction ${subgraph.direction}${nl}`);
  }

  for (const node of subgraph.nodes) {
    lines.push(`${childIndent}${serializeNode(node)}${nl}`);
  }

  for (const nested of subgraph.subgraphs) {
    appendSubgraph(lines, nested, level + 1, indent, nl);
  }

  for (const edge of subgraph.edges) {
    lines.push(`${childIndent}${serializeEdge(edge)}${nl}`);
  }

  lines.push(`${currentIndent}end${nl}`);
}

function quoteText(text) {
  return `"${text.replace(/"/g, '\\"')}"`;
}

function escapeEdgeLabel(label) {
  return label.replace(/\|/g, "\\|");
}

function serializeSequence(diagram, options = null) {
  const indent = options?.indent ?? "    ";
  const nl = options?.newLine ?? "\n";
  const lines = [];

  lines.push(`sequenceDiagram${nl}`);

  if (diagram.autNumber) {
    lines.push(`${indent}autonumber${nl}`);
  }

  for (const p of diagram.participants) {
    const keyword = p.type === "actor" ? "actor" : "participant";
    if (p.name) {
      lines.push(`${indent}${keyword} ${p.actor} as ${quoteText(p.name)}${nl}`);
    } else {
      lines.push(`${indent}${keyword} ${p.actor}${nl}`);
    }
  }

  for (const note of diagram.notes) {
    if (note.actors) {
      lines.push(`${indent}Note over ${note.actors.join(",")}: ${note.text}${nl}`);
    } else {
      lines.push(`${indent}Note ${note.position} of ${note.actor}: ${note.text}${nl}`);
    }
  }

  for (const loop of diagram.loops) {
    lines.push(`${indent}loop ${loop.condition}${nl}`);
    for (const msg of loop.messages) {
      lines.push(`${indent}${indent}${serializeMessage(msg)}${nl}`);
    }
    lines.push(`${indent}end${nl}`);
  }

  for (const alt of diagram.alts) {
    let first = true;
    for (const branch of alt.branches) {
      const keyword = first ? "alt" : "else";
      lines.push(`${indent}${keyword} ${branch.condition}${nl}`);
      for (const msg of branch.messages) {
        lines.push(`${indent}${indent}${serializeMessage(msg)}${nl}`);
      }
      first = false;
    }
    lines.push(`${indent}end${nl}`);
  }

  for (const opt of diagram.opts) {
    lines.push(`${indent}opt ${opt.condition}${nl}`);
    for (const msg of opt.messages) {
      lines.push(`${indent}${indent}${serializeMessage(msg)}${nl}`);
    }
    lines.push(`${indent}end${nl}`);
  }

  for (const par of diagram.parallels) {
    lines.push(`${indent}par${nl}`);
    for (let i = 0; i < par.branches.length; i++) {
      const branch = par.branches[i];
      if (i > 0) lines.push(`${indent}and${nl}`);
      lines.push(`${indent}${indent}${branch.label}${nl}`);
      for (const msg of branch.messages) {
        lines.push(`${indent}${indent}${indent}${serializeMessage(msg)}${nl}`);
      }
    }
    lines.push(`${indent}end${nl}`);
  }

  for (const msg of diagram.messages) {
    lines.push(`${indent}${serializeMessage(msg)}${nl}`);
  }

  return lines.join("").trimEnd();
}

function serializeMessage(msg) {
  const arrow = msg.type === "dotted" ? "-->>" : "->>";
  return `${msg.from}${arrow}${msg.to}: ${msg.text}`;
}

function parseFlowchartSource(source) {
  const diagram = new FlowchartDiagram();
  const lines = source.split("\n");

  let currentSection = "header";
  let currentSubgraphNodes = [];
  let subgraphDepth = 0;

  for (let line of lines) {
    line = line.trim();
    if (!line || line.startsWith("%%")) continue;

    if (line.match(/^flowchart\s+(TB|BT|LR|RL)/i) || line.match(/^graph\s+(TB|BT|LR|RL)/i)) {
      const dir = line.split(/\s+/)[1]?.toUpperCase();
      diagram.direction = dir || Direction.TopToBottom;
      currentSection = "body";
      continue;
    }

    if (line.startsWith("subgraph")) {
      subgraphDepth++;
      continue;
    }

    if (line === "end" && subgraphDepth > 0) {
      subgraphDepth--;
      continue;
    }

    const nodeMatch = line.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*([(\[{<>/\\]+.*?[)\]}>/\\]+)?$/);
    if (nodeMatch && !line.includes("-->") && !line.includes("---") && !line.includes("-.-") && !line.includes("===")) {
      const id = nodeMatch[1];
      const shapePart = nodeMatch[2];

      let text = null;
      let shape = NodeShape.Rectangle;

      if (shapePart) {
        const parsed = parseNodeShape(shapePart);
        text = parsed.text;
        shape = parsed.shape;
      }

      const node = new FlowchartNode(id, text, shape);
      diagram.nodes.push(node);
      continue;
    }

    const edgeMatch = line.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*(-[-.>o=x]+)\s*(?:\|([^|]+)\|)?\s*([A-Za-z_][A-Za-z0-9_]*)$/);
    if (edgeMatch) {
      const from = edgeMatch[1];
      const arrowStr = edgeMatch[2];
      const label = edgeMatch[3] || null;
      const to = edgeMatch[4];

      const type = parseEdgeType(arrowStr);
      diagram.edges.push(new FlowchartEdge(from, to, type, label));
    }
  }

  return ParseResult.ok(diagram);
}

function parseNodeShape(shapePart) {
  let text = "";
  let shape = NodeShape.Rectangle;

  const rectMatch = shapePart.match(/^\[(.+)\]$/);
  const roundMatch = shapePart.match(/^\((.+)\)$/);
  const stadiumMatch = shapePart.match(/^\(\[(.+)\]\)$/);
  const subMatch = shapePart.match(/^\[\[(.+)\]\]$/);
  const cylMatch = shapePart.match(/^\[\((.+)\)\]$/);
  const circleMatch = shapePart.match(/^\(\((.+)\)\)$/);
  const dblCircleMatch = shapePart.match(/^\(\(\((.+)\)\)\)$/);
  const asymMatch = shapePart.match(/^>(.+)\]$/);
  const diamondMatch = shapePart.match(/^\{(.+)\}$/);
  const hexMatch = shapePart.match(/^\{\{(.+)\}\}$/);
  const paraMatch = shapePart.match(/^\[\/(.+)\/\]$/);
  const paraAltMatch = shapePart.match(/^\[\\(.+)\\\]$/);

  if (stadiumMatch) { text = stadiumMatch[1]; shape = NodeShape.Stadium; }
  else if (subMatch) { text = subMatch[1]; shape = NodeShape.Subroutine; }
  else if (cylMatch) { text = cylMatch[1]; shape = NodeShape.Cylinder; }
  else if (dblCircleMatch) { text = dblCircleMatch[1]; shape = NodeShape.DoubleCircle; }
  else if (circleMatch) { text = circleMatch[1]; shape = NodeShape.Circle; }
  else if (roundMatch) { text = roundMatch[1]; shape = NodeShape.RoundedRect; }
  else if (asymMatch) { text = asymMatch[1]; shape = NodeShape.Asymmetric; }
  else if (hexMatch) { text = hexMatch[1]; shape = NodeShape.Hexagon; }
  else if (diamondMatch) { text = diamondMatch[1]; shape = NodeShape.Diamond; }
  else if (paraMatch) { text = paraMatch[1]; shape = NodeShape.Parallelogram; }
  else if (paraAltMatch) { text = paraAltMatch[1]; shape = NodeShape.ParallelogramAlt; }
  else if (rectMatch) { text = rectMatch[1]; shape = NodeShape.Rectangle; }

  text = text.replace(/^"(.*)"$/, "$1").replace(/\\"/g, '"');

  return { text, shape };
}

function parseEdgeType(arrowStr) {
  if (arrowStr === "-->") return EdgeType.Arrow;
  if (arrowStr === "---") return EdgeType.Open;
  if (arrowStr === "-.->") return EdgeType.DottedArrow;
  if (arrowStr === "-.-") return EdgeType.Dotted;
  if (arrowStr === "==>") return EdgeType.ThickArrow;
  if (arrowStr === "===") return EdgeType.Thick;
  if (arrowStr === "--o") return EdgeType.CircleEnd;
  if (arrowStr === "--x") return EdgeType.CrossEnd;
  if (arrowStr === "<-->") return EdgeType.BiDirectional;
  if (arrowStr === "o--o") return EdgeType.BiDirectionalCircle;
  if (arrowStr === "x--x") return EdgeType.BiDirectionalCross;
  return EdgeType.Arrow;
}

function parseSequenceSource(source) {
  const diagram = new SequenceDiagram();
  const lines = source.split("\n");

  for (let line of lines) {
    line = line.trim();
    if (!line || line.startsWith("%%")) continue;

    if (line.toLowerCase() === "sequencediagram") continue;

    const participantMatch = line.match(/^participant\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s+as\s+(.+))?$/i);
    if (participantMatch) {
      diagram.participants.push(new SequenceParticipant(
        participantMatch[1],
        participantMatch[2]?.replace(/^"(.*)"$/, "$1") || null
      ));
      continue;
    }

    const actorMatch = line.match(/^actor\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s+as\s+(.+))?$/i);
    if (actorMatch) {
      const p = new SequenceParticipant(
        actorMatch[1],
        actorMatch[2]?.replace(/^"(.*)"$/, "$1") || null
      );
      p.type = "actor";
      diagram.participants.push(p);
      continue;
    }

    const msgMatch = line.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*(-[-]>|-->>)\s*([A-Za-z_][A-Za-z0-9_]*):?\s*(.*)$/);
    if (msgMatch) {
      const type = msgMatch[2] === "-->>" ? "dotted" : "solid";
      diagram.messages.push(new SequenceMessage(msgMatch[1], msgMatch[3], msgMatch[4], type));
    }
  }

  return ParseResult.ok(diagram);
}

export class EventEmitter {
  #listeners = new Map();

  on(event, callback) {
    if (!this.#listeners.has(event)) {
      this.#listeners.set(event, new Set());
    }
    this.#listeners.get(event).add(callback);
    return () => this.off(event, callback);
  }

  off(event, callback) {
    this.#listeners.get(event)?.delete(callback);
  }

  emit(event, detail = null) {
    const callbacks = this.#listeners.get(event);
    if (callbacks) {
      for (const cb of callbacks) {
        try {
          cb(detail);
        } catch (e) {
          console.error(`Error in event handler for ${event}:`, e);
        }
      }
    }
  }

  once(event, callback) {
    const wrapper = (detail) => {
      this.off(event, wrapper);
      callback(detail);
    };
    return this.on(event, wrapper);
  }
}

export class InteractiveDiagram extends EventEmitter {
  #element = null;
  #diagram = null;
  #svg = null;
  #hoveredNode = null;
  #selectedNodes = new Set();
  #options = {};

  constructor(element, diagram, options = {}) {
    super();
    this.#element = typeof element === "string" ? document.querySelector(element) : element;
    this.#diagram = diagram;
    this.#options = options;
  }

  async render() {
    const svg = await this.#diagram.renderSvg(this.#options.client, this.#options.renderOptions);
    this.#element.innerHTML = sanitizeSvgForDom(svg);
    this.#svg = this.#element.querySelector("svg");
    this.#setupInteractivity();
    this.emit("rendered", { svg });
    return svg;
  }

  #setupInteractivity() {
    if (!this.#svg) return;

    const nodes = this.#svg.querySelectorAll(".node, .flowchart-node, .actor");
    nodes.forEach(node => {
      const nodeId = node.id || node.getAttribute("data-id") || node.querySelector("text")?.textContent;

      node.style.cursor = "pointer";

      node.addEventListener("mouseenter", (e) => {
        this.#hoveredNode = nodeId;
        this.emit("node:hover", { nodeId, element: node, event: e });
      });

      node.addEventListener("mouseleave", (e) => {
        this.#hoveredNode = null;
        this.emit("node:leave", { nodeId, element: node, event: e });
      });

      node.addEventListener("click", (e) => {
        const selected = this.#selectedNodes.has(nodeId);
        if (e.ctrlKey || e.metaKey) {
          if (selected) {
            this.#selectedNodes.delete(nodeId);
          } else {
            this.#selectedNodes.add(nodeId);
          }
        } else {
          this.#selectedNodes.clear();
          this.#selectedNodes.add(nodeId);
        }
        this.emit("node:click", { nodeId, element: node, selected: this.#selectedNodes.has(nodeId), event: e });
        this.emit("selection:changed", { selectedNodes: Array.from(this.#selectedNodes) });
      });

      node.addEventListener("dblclick", (e) => {
        this.emit("node:dblclick", { nodeId, element: node, event: e });
      });
    });

    const edges = this.#svg.querySelectorAll(".edge, .flowchart-link, path.edge");
    edges.forEach(edge => {
      const edgeId = edge.id || edge.getAttribute("data-id");

      edge.style.cursor = "pointer";

      edge.addEventListener("mouseenter", (e) => {
        this.emit("edge:hover", { edgeId, element: edge, event: e });
      });

      edge.addEventListener("mouseleave", (e) => {
        this.emit("edge:leave", { edgeId, element: edge, event: e });
      });

      edge.addEventListener("click", (e) => {
        this.emit("edge:click", { edgeId, element: edge, event: e });
      });
    });
  }

  getSelectedNodes() {
    return Array.from(this.#selectedNodes);
  }

  selectNode(nodeId, addToSelection = false) {
    if (!addToSelection) {
      this.#selectedNodes.clear();
    }
    this.#selectedNodes.add(nodeId);
    this.emit("selection:changed", { selectedNodes: Array.from(this.#selectedNodes) });
  }

  clearSelection() {
    this.#selectedNodes.clear();
    this.emit("selection:changed", { selectedNodes: [] });
  }

  highlightNode(nodeId, highlight = true) {
    const node = this.#svg?.querySelector(`#${nodeId}, [data-id="${nodeId}"]`);
    if (node) {
      if (highlight) {
        node.classList.add("naiad-highlighted");
        this.emit("node:highlight", { nodeId });
      } else {
        node.classList.remove("naiad-highlighted");
        this.emit("node:unhighlight", { nodeId });
      }
    }
  }

  getDiagram() {
    return this.#diagram;
  }

  updateDiagram(newDiagram) {
    this.#diagram = newDiagram;
    return this.render();
  }
}

export function createInteractiveFlowchart(element, options = {}) {
  const builder = new FlowchartBuilder(options.direction ?? Direction.TopToBottom);
  const interactive = new InteractiveDiagram(element, null, options);

  const proxy = new Proxy(builder, {
    get(target, prop) {
      if (prop === "buildAndRender") {
        return async () => {
          const diagram = target.build();
          interactive.updateDiagram(diagram);
          await interactive.render();
          return interactive;
        };
      }
      if (prop === "on" || prop === "off" || prop === "emit" || prop === "once") {
        return interactive[prop].bind(interactive);
      }
      if (prop === "getSelectedNodes" || prop === "selectNode" || prop === "clearSelection" || prop === "highlightNode") {
        return interactive[prop].bind(interactive);
      }
      return target[prop].bind(target);
    }
  });

  return proxy;
}

export function transformToReactFlowFormat(reactFlowData) {
  if (reactFlowData.error) {
    throw new Error(reactFlowData.error);
  }

  const nodes = reactFlowData.nodes.map(node => ({
    id: node.id,
    position: { x: node.position.x, y: node.position.y },
    data: { label: node.data.label },
    type: node.type || "default",
    style: {
      width: node.width,
      height: node.height,
      ...(node.style || {})
    },
    parentNode: node.parentNode || undefined,
    extent: node.extent
  }));

  const edges = reactFlowData.edges.map(edge => ({
    id: edge.id,
    source: edge.source,
    target: edge.target,
    label: edge.label,
    animated: edge.animated,
    style: edge.style,
    type: "smoothstep"
  }));

  return { nodes, edges };
}

export function renderReactFlowComponent(element, reactFlowData, options = {}) {
  if (!window.React || !window.ReactDOM || !window.xyflowReact) {
    throw new Error("React, ReactDOM, and @xyflow/react must be loaded before calling renderReactFlowComponent");
  }

  const { nodes, edges } = transformToReactFlowFormat(reactFlowData);
  const { useState, useEffect } = React;
  const { ReactFlow, Controls, MiniMap, Background, useNodesState, useEdgesState } = window.xyflowReact;

  function FlowComponent({ initialNodes, initialEdges, flowOptions }) {
    const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
    const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

    useEffect(() => {
      setNodes(initialNodes);
      setEdges(initialEdges);
    }, [initialNodes, initialEdges]);

    const nodeTypes = flowOptions.nodeTypes || {
      default: ({ data }) => React.createElement("div", {
        style: {
          padding: "10px 20px",
          background: data.background || "#4a9eff",
          borderRadius: "8px",
          color: "#fff",
          minWidth: "80px",
          textAlign: "center",
          fontSize: "13px",
          boxShadow: "0 2px 8px rgba(0,0,0,0.3)"
        }
      }, data.label)
    };

    return React.createElement(
      ReactFlow,
      {
        nodes,
        edges,
        onNodesChange,
        onEdgesChange,
        nodeTypes,
        fitView: flowOptions.fitView !== false,
        minZoom: flowOptions.minZoom || 0.1,
        maxZoom: flowOptions.maxZoom || 4,
        style: flowOptions.style || { background: "#0f0f23" },
        className: flowOptions.className
      },
      React.createElement(Controls),
      React.createElement(MiniMap, { nodeColor: "#4a9eff" }),
      React.createElement(Background, { variant: "dots", gap: 20, size: 1, color: "#333" })
    );
  }

  const root = ReactDOM.createRoot(element);
  root.render(React.createElement(FlowComponent, {
    initialNodes: nodes,
    initialEdges: edges,
    flowOptions: options
  }));

  return { nodes, edges, root };
}

export async function createReactFlowDiagram(element, options = {}) {
  const client = await getDefaultClient();
  const builder = new FlowchartBuilder(options.direction ?? Direction.TopToBottom);

  const api = {
    builder,
    client,
    
    direction(dir) {
      builder.direction(dir);
      return api;
    },
    
    node(id, text, shape) {
      builder.node(id, text, shape);
      return api;
    },
    
    edge(from, to, type, label) {
      builder.edge(from, to, type, label);
      return api;
    },
    
    connect(...connections) {
      builder.connect(...connections);
      return api;
    },
    
    subgraph(id, title, configure) {
      builder.subgraph(id, title, configure);
      return api;
    },
    
    classDef(name, properties) {
      builder.classDef(name, properties);
      return api;
    },
    
    class(nodeId, ...classNames) {
      builder.class(nodeId, ...classNames);
      return api;
    },
    
    style(nodeId, styles) {
      builder.style(nodeId, styles);
      return api;
    },
    
    async render(renderOptions = null) {
      const diagram = builder.build();
      const mermaid = diagram.toMermaid();
      const data = client.renderReactFlow(mermaid, renderOptions);
      renderReactFlowComponent(element, data, options);
      return { diagram, data, mermaid };
    },
    
    async toData(renderOptions = null) {
      const diagram = builder.build();
      const mermaid = diagram.toMermaid();
      return client.renderReactFlow(mermaid, renderOptions);
    }
  };

  return api;
}

function sanitizeSvgForDom(svgMarkup) {
  if (typeof svgMarkup !== "string" || svgMarkup.trim() === "") {
    return "";
  }

  const parser = new DOMParser();
  const doc = parser.parseFromString(svgMarkup, "image/svg+xml");
  if (doc.querySelector("parsererror")) {
    return "";
  }

  const root = doc.documentElement;
  if (!root || root.tagName.toLowerCase() !== "svg") {
    return "";
  }

  const disallowedTags = new Set(["script", "iframe", "object", "embed", "link", "meta"]);
  const nodes = [root, ...Array.from(root.querySelectorAll("*"))];
  for (const node of nodes) {
    const tag = node.tagName.toLowerCase();
    if (disallowedTags.has(tag)) {
      node.remove();
      continue;
    }

    if (tag === "style") {
      node.textContent = sanitizeCssText(node.textContent ?? "");
    }

    for (const attr of Array.from(node.attributes)) {
      const name = attr.name.toLowerCase();
      const value = attr.value ?? "";
      if (name.startsWith("on")) {
        node.removeAttribute(attr.name);
        continue;
      }

      if (name === "style") {
        const sanitized = sanitizeCssText(value);
        if (sanitized === "") {
          node.removeAttribute(attr.name);
        } else {
          node.setAttribute(attr.name, sanitized);
        }
        continue;
      }

      const normalized = value.replace(/\s+/g, "").toLowerCase();
      if (
        normalized.includes("javascript:") ||
        normalized.includes("vbscript:") ||
        normalized.includes("data:text/html") ||
        normalized.includes("data:application/xhtml+xml")
      ) {
        node.removeAttribute(attr.name);
      }
    }
  }

  return new XMLSerializer().serializeToString(root);
}

function sanitizeCssText(cssText) {
  if (!cssText) {
    return "";
  }

  let sanitized = cssText;
  sanitized = sanitized.replace(/expression\s*\(/gi, "blocked(");
  sanitized = sanitized.replace(/javascript\s*:/gi, "blocked:");
  sanitized = sanitized.replace(/vbscript\s*:/gi, "blocked:");
  sanitized = sanitized.replace(/@import[^;]*;?/gi, "");
  sanitized = sanitized.replace(/@charset[^;]*;?/gi, "");
  sanitized = sanitized.replace(/@namespace[^;]*;?/gi, "");
  sanitized = sanitized.replace(
    /url\s*\(\s*(['"]?)\s*(?:javascript:|vbscript:|data:text\/html|data:application\/xhtml\+xml)/gi,
    "url($1blocked:"
  );
  return sanitized.trim();
}

export default NaiadFluent;
