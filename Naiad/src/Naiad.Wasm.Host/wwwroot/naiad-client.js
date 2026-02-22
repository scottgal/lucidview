import { dotnet } from "./_framework/dotnet.js";

export class NaiadClient {
    #exportsRef = null;

    async init() {
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
        return this;
    }

    health() {
        this.#ensureReady();
        return this.#exportsRef.Health();
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

    #ensureReady() {
        if (!this.#exportsRef) {
            throw new Error("NaiadClient is not initialized. Call init() first.");
        }
    }
}
