import { dotnet } from "./_framework/dotnet.js";

export class NaiadClient {
    #exportsRef = null;
    #initialized = false;

    async init() {
        if (this.#initialized) return this;

        let runtime;
        try {
            runtime = await dotnet.create();
        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            throw new Error(`dotnet.create failed: ${message}`);
        }

        let naiadAssembly;
        try {
            const config = runtime.getConfig();
            naiadAssembly = await runtime.getAssemblyExports(config.mainAssemblyName);
        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            throw new Error(`getAssemblyExports failed: ${message}`);
        }

        this.#exportsRef = naiadAssembly.MermaidSharp.Wasm.NaiadHostExports;
        this.#initialized = true;
        return this;
    }

    get isInitialized() {
        return this.#initialized;
    }

    health() {
        this.#ensureReady();
        return this.#exportsRef.Health();
    }

    echo(value) {
        this.#ensureReady();
        return this.#exportsRef.Echo(value);
    }

    detectDiagramType(mermaid) {
        this.#ensureReady();
        return this.#exportsRef.DetectDiagramType(mermaid);
    }

    renderSvg(mermaid, options = null) {
        this.#ensureReady();
        const optionsJson = options ? JSON.stringify(options) : "";
        return this.#exportsRef.RenderSvg(mermaid, optionsJson);
    }

    renderSvgDocument(mermaid, options = null) {
        this.#ensureReady();
        const optionsJson = options ? JSON.stringify(options) : "";
        const raw = this.#exportsRef.RenderSvgDocumentJson(mermaid, optionsJson);
        return JSON.parse(raw);
    }

    listSkinPacks() {
        this.#ensureReady();
        const raw = this.#exportsRef.GetBuiltInSkinPacksJson();
        return JSON.parse(raw);
    }

    renderReactFlow(mermaid, options = null) {
        this.#ensureReady();
        const optionsJson = options ? JSON.stringify(options) : "";
        const raw = this.#exportsRef.RenderReactFlowJson(mermaid, optionsJson);
        return JSON.parse(raw);
    }

    debugFlowchartParse(mermaid) {
        this.#ensureReady();
        return this.#exportsRef.DebugFlowchartParse(mermaid);
    }

    debugFlowchartRender(mermaid) {
        this.#ensureReady();
        return this.#exportsRef.DebugFlowchartRender(mermaid);
    }

    flowchart(direction = "TB") {
        return new FlowchartBuilderApi(this, direction);
    }

    sequence() {
        return new SequenceBuilderApi(this);
    }

    parse(mermaid) {
        const type = this.detectDiagramType(mermaid);
        return {
            type,
            isFlowchart: type === "Flowchart",
            isSequence: type === "Sequence",
            isClass: type === "Class",
            isState: type === "State",
            isGantt: type === "Gantt",
            isPie: type === "Pie",
            isTimeline: type === "Timeline",
            isMindmap: type === "Mindmap"
        };
    }

    #ensureReady() {
        if (!this.#exportsRef || !this.#initialized) {
            throw new Error("NaiadClient is not initialized. Call init() first.");
        }
    }
}

class FlowchartBuilderApi {
    #client;
    #direction = "TB";
    #nodes = [];
    #edges = [];
    #subgraphs = [];
    #classDefs = [];
    #classAssignments = [];

    constructor(client, direction = "TB") {
        this.#client = client;
        this.#direction = direction;
    }

    direction(dir) {
        this.#direction = dir;
        return this;
    }

    node(id, label = null, shape = "rect") {
        this.#nodes.push({ id, label, shape });
        return this;
    }

    edge(from, to, label = null, arrowType = "arrow") {
        this.#edges.push({ from, to, label, arrowType });
        return this;
    }

    connect(...pairs) {
        for (const [from, to] of pairs) {
            this.edge(from, to);
        }
        return this;
    }

    subgraph(id, title, configure) {
        const builder = new SubgraphBuilderApi(id, title);
        configure(builder);
        this.#subgraphs.push(builder.build());
        return this;
    }

    classDef(name, styles) {
        this.#classDefs.push({ name, styles });
        return this;
    }

    class(nodeId, className) {
        this.#classAssignments.push({ nodeId, className });
        return this;
    }

    build() {
        return {
            type: "Flowchart",
            direction: this.#direction,
            nodes: [...this.#nodes],
            edges: [...this.#edges],
            subgraphs: [...this.#subgraphs],
            classDefs: [...this.#classDefs],
            classAssignments: [...this.#classAssignments],
            toMermaid: () => this.#toMermaid(),
            render: (options) => this.render(options)
        };
    }

    #toMermaid() {
        const lines = [`flowchart ${this.#direction}`];

        for (const node of this.#nodes) {
            lines.push(`    ${formatNode(node)}`);
        }

        for (const subgraph of this.#subgraphs) {
            lines.push(...formatSubgraph(subgraph, 1));
        }

        for (const edge of this.#edges) {
            lines.push(`    ${formatEdge(edge)}`);
        }

        for (const classDef of this.#classDefs) {
            const styleStr = typeof classDef.styles === "string"
                ? classDef.styles
                : Object.entries(classDef.styles).map(([k, v]) => `${k}:${v}`).join(",");
            lines.push(`    classDef ${classDef.name} ${styleStr}`);
        }

        for (const assignment of this.#classAssignments) {
            lines.push(`    class ${assignment.nodeId} ${assignment.className}`);
        }

        return lines.join("\n");
    }

    render(options = null) {
        const mermaid = this.#toMermaid();
        return this.#client.renderSvg(mermaid, options);
    }

    renderTo(element, options = null) {
        if (typeof element === "string") {
            element = document.querySelector(element);
        }
        return this.render(options).then(svg => {
            if (element) element.innerHTML = sanitizeSvgForDom(svg);
            return svg;
        });
    }
}

class SubgraphBuilderApi {
    #id;
    #title;
    #direction = null;
    #nodes = [];
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

    node(id, label = null, shape = "rect") {
        this.#nodes.push({ id, label, shape });
        return this;
    }

    edge(from, to, label = null, arrowType = "arrow") {
        this.#edges.push({ from, to, label, arrowType });
        return this;
    }

    subgraph(id, title, configure) {
        const builder = new SubgraphBuilderApi(id, title);
        configure(builder);
        this.#subgraphs.push(builder.build());
        return this;
    }

    build() {
        return {
            id: this.#id,
            title: this.#title,
            direction: this.#direction,
            nodes: [...this.#nodes],
            edges: [...this.#edges],
            subgraphs: [...this.#subgraphs]
        };
    }
}

class SequenceBuilderApi {
    #client;
    #participants = [];
    #messages = [];
    #notes = [];
    #autonumber = false;

    constructor(client) {
        this.#client = client;
    }

    participant(id, label = null) {
        this.#participants.push({ id, label, type: "participant" });
        return this;
    }

    actor(id, label = null) {
        this.#participants.push({ id, label, type: "actor" });
        return this;
    }

    message(from, to, text, arrowType = "->>") {
        this.#messages.push({ from, to, text, arrowType });
        return this;
    }

    arrow(from, to, text) {
        return this.message(from, to, text, "->>");
    }

    dotted(from, to, text) {
        return this.message(from, to, text, "-->>");
    }

    note(position, actor, text) {
        this.#notes.push({ position, actor, text });
        return this;
    }

    noteOver(actors, text) {
        const actorList = Array.isArray(actors) ? actors : [actors];
        this.#notes.push({ position: "over", actors: actorList, text });
        return this;
    }

    autonumber(enabled = true) {
        this.#autonumber = enabled;
        return this;
    }

    build() {
        return {
            type: "Sequence",
            participants: [...this.#participants],
            messages: [...this.#messages],
            notes: [...this.#notes],
            autonumber: this.#autonumber,
            toMermaid: () => this.#toMermaid(),
            render: (options) => this.render(options)
        };
    }

    #toMermaid() {
        const lines = ["sequenceDiagram"];

        if (this.#autonumber) {
            lines.push("    autonumber");
        }

        for (const p of this.#participants) {
            const keyword = p.type === "actor" ? "actor" : "participant";
            if (p.label) {
                lines.push(`    ${keyword} ${p.id} as "${p.label}"`);
            } else {
                lines.push(`    ${keyword} ${p.id}`);
            }
        }

        for (const note of this.#notes) {
            if (note.actors) {
                lines.push(`    Note over ${note.actors.join(",")}: ${note.text}`);
            } else {
                lines.push(`    Note ${note.position} of ${note.actor}: ${note.text}`);
            }
        }

        for (const msg of this.#messages) {
            lines.push(`    ${msg.from}${msg.arrowType}${msg.to}: ${msg.text}`);
        }

        return lines.join("\n");
    }

    render(options = null) {
        const mermaid = this.#toMermaid();
        return this.#client.renderSvg(mermaid, options);
    }

    renderTo(element, options = null) {
        if (typeof element === "string") {
            element = document.querySelector(element);
        }
        return this.render(options).then(svg => {
            if (element) element.innerHTML = sanitizeSvgForDom(svg);
            return svg;
        });
    }
}

function formatNode(node) {
    if (!node.label) return node.id;
    const label = `"${node.label.replace(/"/g, '\\"')}"`;
    const shape = node.shape || "rect";
    let formatted;
    switch (shape) {
        case "rounded": formatted = `${node.id}(${label})`; break;
        case "stadium": formatted = `${node.id}([${label}])`; break;
        case "subroutine": formatted = `${node.id}[[${label}]]`; break;
        case "cylinder": formatted = `${node.id}[(${label})]`; break;
        case "circle": formatted = `${node.id}((${label}))`; break;
        case "doublecircle": formatted = `${node.id}(((${label})))`; break;
        case "diamond": formatted = `${node.id}{${label}}`; break;
        case "hexagon": formatted = `${node.id}{{${label}}}`; break;
        case "parallelogram": formatted = `${node.id}[/${label}/]`; break;
        case "parallelogramAlt": formatted = `${node.id}[\\${label}\\]`; break;
        case "asymmetric": formatted = `${node.id}>${label}]`; break;
        default: formatted = `${node.id}[${label}]`;
    }
    return formatted;
}

function formatEdge(edge) {
    let arrow;
    switch (edge.arrowType) {
        case "open": arrow = "---"; break;
        case "dotted": arrow = "-.-"; break;
        case "dottedArrow": arrow = "-.->"; break;
        case "thick": arrow = "==="; break;
        case "thickArrow": arrow = "==>"; break;
        case "circleEnd": arrow = "--o"; break;
        case "crossEnd": arrow = "--x"; break;
        case "bidirectional": arrow = "<-->"; break;
        default: arrow = "-->";
    }

    if (edge.label) {
        return `${edge.from} ${arrow}|${edge.label.replace(/\|/g, "\\|")}| ${edge.to}`;
    }
    return `${edge.from} ${arrow} ${edge.to}`;
}

function formatSubgraph(subgraph, level) {
    const indent = "    ".repeat(level);
    const lines = [`${indent}subgraph ${subgraph.id} ["${subgraph.title}"]`];

    if (subgraph.direction) {
        lines.push(`${indent}    direction ${subgraph.direction}`);
    }

    for (const node of subgraph.nodes) {
        lines.push(`${indent}    ${formatNode(node)}`);
    }

    for (const nested of subgraph.subgraphs) {
        lines.push(...formatSubgraph(nested, level + 1));
    }

    for (const edge of subgraph.edges) {
        lines.push(`${indent}    ${formatEdge(edge)}`);
    }

    lines.push(`${indent}end`);
    return lines;
}

let defaultClient = null;
let defaultClientPromise = null;

export async function getClient() {
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

export async function render(mermaid, options = null) {
    const client = await getClient();
    return client.renderSvg(mermaid, options);
}

export async function renderTo(element, mermaid, options = null) {
    const svg = await render(mermaid, options);
    if (typeof element === "string") {
        element = document.querySelector(element);
    }
    if (element) element.innerHTML = sanitizeSvgForDom(svg);
    return svg;
}

export async function flowchart(direction = "TB") {
    const client = await getClient();
    return client.flowchart(direction);
}

export async function sequence() {
    const client = await getClient();
    return client.sequence();
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
