export interface NaiadRenderedEventDetail {
  mermaid: string;
  svgLength: number;
  renderMs: number;
  cached: boolean;
  theme: string | null;
}

export interface NaiadRenderErrorEventDetail {
  message: string;
  mermaid?: string;
}

export interface NaiadResizeEventDetail {
  width: number;
  height: number;
}

export interface NaiadMenuActionEventDetail {
  action: "download-svg" | "download-png";
}

export interface NaiadBeforeExportEventDetail {
  format: "svg" | "png";
  fileName: string;
}

export interface NaiadAfterExportEventDetail extends NaiadBeforeExportEventDetail {
  byteLength: number;
  mimeType: string;
}

export interface NaiadExportErrorEventDetail extends NaiadBeforeExportEventDetail {
  message: string;
}

export interface NaiadPngExportOptions {
  scale?: number;
  background?: string;
}

export interface NaiadRenderOptions {
  padding?: number;
  theme?: string;
  fontSize?: number;
  fontFamily?: string;
  showBoundingBox?: boolean;
  maxNodes?: number;
  maxEdges?: number;
  maxComplexity?: number;
  maxInputSize?: number;
  renderTimeout?: number;
  curvedEdges?: boolean;
  includeExternalResources?: boolean;
  skinPack?: string;
  themeColors?: {
    textColor?: string;
    backgroundColor?: string;
    nodeFill?: string;
    nodeStroke?: string;
    edgeStroke?: string;
    subgraphFill?: string;
    subgraphStroke?: string;
    edgeLabelBackground?: string;
  };
}

export interface NaiadSkinPackConfigEntry {
  name: string;
  label: string;
  aliases: string[];
  embeddedInWasm: boolean;
  archive: string | null;
  directory: string | null;
  styleSource: "internal" | "geometry-only" | "svg-path-style";
  description: string;
}

export interface NaiadSkinPackConfig {
  version: number;
  defaultPack: string;
  packs: NaiadSkinPackConfigEntry[];
}

export class NaiadClient {
  init(): Promise<this>;
  health(): string;
  detectDiagramType(mermaid: string): string;
  renderSvg(mermaid: string, options?: NaiadRenderOptions | null): string;
  renderSvgDocument(mermaid: string, options?: NaiadRenderOptions | null): unknown;
  listSkinPacks(): string[];
}

export class NaiadDiagramElement extends HTMLElement {
  mermaid: string;
  options: NaiadRenderOptions | string | null;
  theme: string;
  cacheSize: number;
  downloadFileName: string;
  render(): Promise<void>;
  getSvgMarkup(): string;
  toSvgBlob(): Promise<Blob>;
  toPngBlob(options?: NaiadPngExportOptions | null): Promise<Blob>;
  downloadSvg(fileName?: string | null): Promise<Blob | null>;
  downloadPng(fileName?: string | null, options?: NaiadPngExportOptions | null): Promise<Blob | null>;
  getBuiltInSkinPacks(): Promise<string[]>;
}

declare global {
  interface HTMLElementTagNameMap {
    "naiad-diagram": NaiadDiagramElement;
  }

  interface HTMLElementEventMap {
    rendered: CustomEvent<NaiadRenderedEventDetail>;
    rendererror: CustomEvent<NaiadRenderErrorEventDetail>;
    resized: CustomEvent<NaiadResizeEventDetail>;
    menuaction: CustomEvent<NaiadMenuActionEventDetail>;
    beforeexport: CustomEvent<NaiadBeforeExportEventDetail>;
    afterexport: CustomEvent<NaiadAfterExportEventDetail>;
    exporterror: CustomEvent<NaiadExportErrorEventDetail>;
  }
}
