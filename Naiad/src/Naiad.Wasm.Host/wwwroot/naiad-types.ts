export type Direction = "TB" | "BT" | "LR" | "RL";

export type NodeShape =
  | "Rectangle"
  | "RoundedRect"
  | "Stadium"
  | "Subroutine"
  | "Cylinder"
  | "Circle"
  | "DoubleCircle"
  | "Asymmetric"
  | "Diamond"
  | "Hexagon"
  | "Parallelogram"
  | "ParallelogramAlt"
  | "Trapezoid"
  | "TrapezoidAlt"
  | "Document"
  | "TriangleUp"
  | "TriangleDown"
  | "Hourglass"
  | "Text";

export type EdgeType =
  | "Arrow"
  | "Open"
  | "DottedArrow"
  | "Dotted"
  | "ThickArrow"
  | "Thick"
  | "CircleEnd"
  | "CrossEnd"
  | "BiDirectional"
  | "BiDirectionalCircle"
  | "BiDirectionalCross";

export type DiagramType =
  | "Flowchart"
  | "Sequence"
  | "Class"
  | "State"
  | "EntityRelationship"
  | "UserJourney"
  | "Gantt"
  | "Pie"
  | "Quadrant"
  | "Requirement"
  | "GitGraph"
  | "Mindmap"
  | "Timeline"
  | "C4"
  | "Sankey"
  | "Block"
  | "Packet"
  | "Architecture"
  | "Xychart"
  | "Treemap";

export interface FlowchartNodeConfig {
  id: string;
  text?: string | null;
  shape?: NodeShape;
  cssClass?: string | null;
  link?: string | null;
  tooltip?: string | null;
  metadata?: Record<string, unknown> | null;
}

export interface FlowchartEdgeConfig {
  from: string;
  to: string;
  type?: EdgeType;
  label?: string | null;
  minLength?: number;
}

export interface FlowchartSubgraphConfig {
  id: string;
  title: string;
  direction?: Direction | null;
  nodes?: FlowchartNodeConfig[];
  edges?: FlowchartEdgeConfig[];
  subgraphs?: FlowchartSubgraphConfig[];
}

export interface FlowchartClassDef {
  name: string;
  properties: Map<string, string> | Record<string, string>;
}

export interface FlowchartClassAssignment {
  targetId: string;
  className: string;
}

export interface RenderOptions {
  theme?: "default" | "dark" | "forest" | "neutral";
  skinPack?: string;
  fontSize?: number;
  fontFamily?: string;
  width?: number;
  height?: number;
  [key: string]: unknown;
}

export interface SerializeOptions {
  newLine?: string;
  indent?: string;
}

export interface ParseResult<T> {
  success: boolean;
  value: T | null;
  error: string | null;
  diagnostics: Diagnostic[];
}

export interface SerializeResult {
  success: boolean;
  mermaid: string | null;
  error: string | null;
}

export interface Diagnostic {
  code: string;
  severity: "error" | "warning" | "info";
  message: string;
  span?: SourceSpan | null;
  path?: string | null;
  nodeId?: string | null;
  suggestion?: string | null;
}

export interface SourceSpan {
  start: number;
  end: number;
  lineStart: number;
  lineEnd: number;
}

export interface ReactFlowPosition {
  x: number;
  y: number;
}

export interface ReactFlowNodeData {
  label: string;
}

export interface ReactFlowNodeStyle {
  background?: string;
  borderColor?: string;
  borderWidth?: number;
  color?: string;
  fontSize?: number;
}

export interface ReactFlowNode {
  id: string;
  position: ReactFlowPosition;
  data: ReactFlowNodeData;
  type: "default" | "group" | "input" | "output";
  width: number;
  height: number;
  parentNode?: string | null;
  extent?: "parent" | null;
  style?: ReactFlowNodeStyle | null;
}

export interface ReactFlowEdgeStyle {
  stroke?: string;
  strokeWidth?: number;
  strokeDasharray?: string;
}

export interface ReactFlowEdge {
  id: string;
  source: string;
  target: string;
  type: "default" | "straight" | "step" | "smoothstep" | "bezier";
  label?: string | null;
  animated: boolean;
  style?: ReactFlowEdgeStyle | null;
}

export interface ReactFlowDocument {
  nodes: ReactFlowNode[];
  edges: ReactFlowEdge[];
}

export interface SequenceParticipant {
  actor: string;
  name?: string | null;
  type: "participant" | "actor";
}

export interface SequenceMessage {
  from: string;
  to: string;
  text: string;
  type: "solid" | "dotted";
}

export interface SequenceNote {
  position: string;
  actor?: string | null;
  actors?: string[] | null;
  text: string;
}

export interface NaiadClientInterface {
  isInitialized: boolean;
  init(): Promise<this>;
  health(): string;
  echo(value: string): string;
  detectDiagramType(mermaid: string): DiagramType;
  renderSvg(mermaid: string, options?: RenderOptions | null): string;
  renderSvgDocument(
    mermaid: string,
    options?: RenderOptions | null
  ): Record<string, unknown>;
  listSkinPacks(): string[];
  renderReactFlow(mermaid: string, options?: RenderOptions | null): ReactFlowDocument;
  debugFlowchartParse(mermaid: string): string;
  debugFlowchartRender(mermaid: string): string;
  flowchart(direction?: Direction): FlowchartBuilderApi;
  sequence(): SequenceBuilderApi;
  parse(mermaid: string): ParsedDiagramInfo;
}

export interface ParsedDiagramInfo {
  type: DiagramType;
  isFlowchart: boolean;
  isSequence: boolean;
  isClass: boolean;
  isState: boolean;
  isGantt: boolean;
  isPie: boolean;
  isTimeline: boolean;
  isMindmap: boolean;
}

export interface FlowchartBuilderApi {
  direction(dir: Direction): this;
  node(id: string, label?: string | null, shape?: NodeShape): this;
  nodeWithConfig(config: FlowchartNodeConfig): this;
  nodes(...nodeDefs: Array<string | FlowchartNodeConfig | [string, string?, NodeShape?]>): this;
  edge(from: string, to: string, type?: EdgeType, label?: string | null): this;
  edgeWithConfig(config: FlowchartEdgeConfig): this;
  connect(...connections: Array<string | FlowchartEdgeConfig | [string, string, EdgeType?, string?]>): this;
  subgraph(id: string, title: string, configure: (builder: SubgraphBuilderApi) => void): this;
  classDef(name: string, styles: string | Record<string, string>): this;
  class(nodeId: string, ...classNames: string[]): this;
  style(nodeId: string, styles: string | Record<string, string>): this;
  build(): FlowchartDiagramApi;
}

export interface SubgraphBuilderApi {
  direction(dir: Direction): this;
  node(id: string, label?: string | null, shape?: NodeShape): this;
  edge(from: string, to: string, type?: EdgeType, label?: string | null): this;
  subgraph(id: string, title: string, configure: (builder: SubgraphBuilderApi) => void): this;
}

export interface FlowchartDiagramApi {
  type: "Flowchart";
  direction: Direction;
  nodes: FlowchartNode[];
  edges: FlowchartEdge[];
  subgraphs: FlowchartSubgraph[];
  classDefs: FlowchartClassDef[];
  classAssignments: FlowchartClassAssignment[];
  toMermaid(options?: SerializeOptions): string;
  render(options?: RenderOptions | null): Promise<string>;
  renderTo(element: Element | string, options?: RenderOptions | null): Promise<string>;
  toReactFlow(options?: RenderOptions | null): Promise<ReactFlowDocument>;
  renderReactFlowTo(element: Element | string, options?: ReactFlowRenderOptions | null): Promise<void>;
}

export interface SequenceBuilderApi {
  participant(id: string, label?: string | null): this;
  actor(id: string, label?: string | null): this;
  message(from: string, to: string, text: string, arrowType?: "->>" | "-->"): this;
  arrow(from: string, to: string, text: string): this;
  dotted(from: string, to: string, text: string): this;
  note(position: string, actor: string, text: string): this;
  noteOver(actors: string | string[], text: string): this;
  autonumber(enabled?: boolean): this;
  build(): SequenceDiagramApi;
}

export interface SequenceDiagramApi {
  type: "Sequence";
  participants: SequenceParticipant[];
  messages: SequenceMessage[];
  notes: SequenceNote[];
  autonumber: boolean;
  toMermaid(options?: SerializeOptions): string;
  render(options?: RenderOptions | null): Promise<string>;
  renderTo(element: Element | string, options?: RenderOptions | null): Promise<string>;
}

export interface FlowchartNode {
  id: string;
  text: string | null;
  shape: NodeShape;
  cssClass: string | null;
  link: string | null;
  tooltip: string | null;
  metadata: Record<string, unknown> | null;
}

export interface FlowchartEdge {
  from: string;
  to: string;
  type: EdgeType;
  label: string | null;
  minLength: number;
}

export interface FlowchartSubgraph {
  id: string;
  title: string;
  direction: Direction | null;
  nodes: FlowchartNode[];
  edges: FlowchartEdge[];
  subgraphs: FlowchartSubgraph[];
  cssClass: string | null;
}

export interface ReactFlowRenderOptions {
  renderOptions?: RenderOptions | null;
  nodeTypes?: Record<string, unknown>;
  edgeTypes?: Record<string, unknown>;
  fitView?: boolean;
  minZoom?: number;
  maxZoom?: number;
  defaultViewport?: { x: number; y: number; zoom: number };
  onNodeClick?: (event: ReactFlowNodeClickEvent) => void;
  onEdgeClick?: (event: ReactFlowEdgeClickEvent) => void;
  onNodeDoubleClick?: (event: ReactFlowNodeClickEvent) => void;
  className?: string;
  style?: Record<string, string | number>;
}

export interface ReactFlowNodeClickEvent {
  node: ReactFlowNode;
  nodes: ReactFlowNode[];
  event: MouseEvent;
}

export interface ReactFlowEdgeClickEvent {
  edge: ReactFlowEdge;
  event: MouseEvent;
}

export interface InteractiveDiagramEvents {
  "node:hover": { nodeId: string; element: Element; event: MouseEvent };
  "node:leave": { nodeId: string; element: Element; event: MouseEvent };
  "node:click": {
    nodeId: string;
    element: Element;
    selected: boolean;
    event: MouseEvent;
  };
  "node:dblclick": { nodeId: string; element: Element; event: MouseEvent };
  "node:highlight": { nodeId: string };
  "node:unhighlight": { nodeId: string };
  "edge:hover": { edgeId: string; element: Element; event: MouseEvent };
  "edge:leave": { edgeId: string; element: Element; event: MouseEvent };
  "edge:click": { edgeId: string; element: Element; event: MouseEvent };
  "selection:changed": { selectedNodes: string[] };
  rendered: { svg: string };
}

export type InteractiveDiagramEventType = keyof InteractiveDiagramEvents;

export interface InteractiveDiagramInterface {
  on<K extends InteractiveDiagramEventType>(
    event: K,
    callback: (detail: InteractiveDiagramEvents[K]) => void
  ): () => void;
  off<K extends InteractiveDiagramEventType>(
    event: K,
    callback: (detail: InteractiveDiagramEvents[K]) => void
  ): void;
  emit<K extends InteractiveDiagramEventType>(
    event: K,
    detail: InteractiveDiagramEvents[K]
  ): void;
  once<K extends InteractiveDiagramEventType>(
    event: K,
    callback: (detail: InteractiveDiagramEvents[K]) => void
  ): () => void;
  render(): Promise<string>;
  getSelectedNodes(): string[];
  selectNode(nodeId: string, addToSelection?: boolean): void;
  clearSelection(): void;
  highlightNode(nodeId: string, highlight?: boolean): void;
  getDiagram(): FlowchartDiagramApi | SequenceDiagramApi | null;
  updateDiagram(
    newDiagram: FlowchartDiagramApi | SequenceDiagramApi
  ): Promise<string>;
}
