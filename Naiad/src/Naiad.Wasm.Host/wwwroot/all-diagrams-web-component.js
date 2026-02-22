const summaryEl = document.getElementById("summary");
const diagramsEl = document.getElementById("diagrams");
const params = new URLSearchParams(window.location.search);
const selectedSkin = (params.get("skin") ?? "").trim();
const selectedTheme = (params.get("theme") ?? "auto").trim().toLowerCase();

window.__naiadAllDiagramsResult = {
  done: false,
  total: 0,
  failures: [],
  passes: []
};

window.__naiadAllDiagramsReady = run();

async function run() {
  try {
    const diagrams = await loadPrimaryDiagrams();

    if (diagrams.length === 0) {
      throw new Error("No mermaid code blocks were found in test sources.");
    }

    updateSummary(`Rendering ${diagrams.length} diagrams...`);
    const results = await renderDiagrams(diagrams);

    const failures = results.filter((entry) => !entry.ok);
    const passes = results.filter((entry) => entry.ok);

    window.__naiadAllDiagramsResult = {
      done: true,
      total: results.length,
      failures,
      passes
    };

    if (failures.length === 0) {
      updateSummary(
        `All diagrams rendered successfully.\n` +
          `count=${results.length} theme=${selectedTheme || "auto"} skin=${selectedSkin || "(default)"}`,
        true
      );
    } else {
      const details = failures
        .map((entry) => `${entry.index}. ${entry.title}: ${entry.error}`)
        .join("\n");
      updateSummary(
        `Some diagrams failed to render.\n` +
          `total=${results.length} failures=${failures.length}\n\n` +
          details,
        false
      );
    }

    return window.__naiadAllDiagramsResult;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    window.__naiadAllDiagramsResult = {
      done: true,
      total: 0,
      failures: [{ index: -1, title: "bootstrap", error: message, ok: false }],
      passes: []
    };
    updateSummary(`Failed to initialize all-diagrams page.\n${message}`, false);
    return window.__naiadAllDiagramsResult;
  }
}

async function loadPrimaryDiagrams() {
  const fromIndex = await loadFromTestRendersIndex();
  if (fromIndex.length > 0) {
    return fromIndex;
  }

  const markdown = await loadMarkdown("./all-diagrams-test.md");
  return parseDiagrams(markdown);
}

async function loadFromTestRendersIndex() {
  try {
    const indexMarkdown = await loadMarkdown("./test-renders/renders.include.md");
    const entries = parseRenderIndex(indexMarkdown);
    if (entries.length === 0) {
      return [];
    }

    const diagrams = [];
    for (const entry of entries) {
      const markdown = await loadMarkdown(entry.path);
      const mermaid = parseFirstDiagramBlock(markdown);
      if (!mermaid) {
        continue;
      }

      diagrams.push({
        index: diagrams.length + 1,
        title: entry.title,
        mermaid
      });
    }

    return diagrams;
  } catch {
    return [];
  }
}

async function loadMarkdown(path) {
  const response = await fetch(path, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Unable to load ${path}: HTTP ${response.status}`);
  }
  return await response.text();
}

function parseRenderIndex(markdown) {
  const entries = [];
  const linkRegex = /^-\s+\[([^\]]+)\]\(([^)]+)\)\s*$/gm;
  let match;
  while ((match = linkRegex.exec(markdown)) !== null) {
    const title = (match[1] ?? "").trim();
    const href = (match[2] ?? "").trim();
    if (!title || !href) {
      continue;
    }

    const normalized = href.replace(/\\/g, "/");
    const fileName = normalized.split("/").pop();
    if (!fileName || !fileName.toLowerCase().endsWith(".md")) {
      continue;
    }

    entries.push({
      title,
      path: `./test-renders/${fileName}`
    });
  }

  return entries;
}

function parseFirstDiagramBlock(markdown) {
  const match = markdown.match(/```(?:mermaid)?\s*\r?\n([\s\S]*?)```/i);
  return match && match[1] ? match[1].trim() : "";
}

function parseDiagrams(markdown) {
  const results = [];
  const blockRegex = /^##\s+(.+?)\s*\r?\n[\s\S]*?```mermaid\s*\r?\n([\s\S]*?)```/gm;
  let match;
  while ((match = blockRegex.exec(markdown)) !== null) {
    const rawTitle = (match[1] ?? "").trim();
    const title = rawTitle.replace(/^\d+\.\s*/, "").trim() || `Diagram ${results.length + 1}`;
    const mermaid = (match[2] ?? "").trim();
    if (!mermaid) {
      continue;
    }

    results.push({
      index: results.length + 1,
      title,
      mermaid
    });
  }

  return results;
}

async function renderDiagrams(diagrams) {
  diagramsEl.innerHTML = "";

  const renderTasks = diagrams.map((entry) => {
    const card = document.createElement("article");
    card.className = "card";

    const title = document.createElement("h2");
    title.textContent = `${entry.index}. ${entry.title}`;

    const meta = document.createElement("p");
    meta.className = "meta";
    meta.textContent = "Waiting for render...";

    const diagram = document.createElement("naiad-diagram");
    diagram.setAttribute("fit-width", "");
    diagram.setAttribute("rerender-on-resize", "");
    diagram.setAttribute("show-menu", "");
    diagram.setAttribute("download-filename", slugify(entry.title));

    if (selectedTheme && selectedTheme !== "auto") {
      diagram.setAttribute("theme", selectedTheme);
    } else {
      diagram.setAttribute("theme", "auto");
    }

    if (selectedSkin) {
      diagram.options = { skinPack: selectedSkin };
    }

    const completion = new Promise((resolve) => {
      let settled = false;
      let timeoutId = null;

      function finish(ok, message) {
        if (settled) {
          return;
        }

        settled = true;
        if (timeoutId !== null) {
          clearTimeout(timeoutId);
        }

        if (ok) {
          meta.classList.remove("fail");
          meta.textContent = `Rendered: ${message}`;
          resolve({ ...entry, ok: true, error: "" });
        } else {
          meta.classList.add("fail");
          meta.textContent = `Failed: ${message}`;
          resolve({ ...entry, ok: false, error: message });
        }
      }

      diagram.addEventListener(
        "rendered",
        (event) => {
          const detail = event?.detail ?? {};
          const svgLength = detail.svgLength ?? 0;
          finish(svgLength > 0, `svgLength=${svgLength}`);
        },
        { once: true }
      );

      diagram.addEventListener(
        "rendererror",
        (event) => {
          const message = event?.detail?.message ?? "Unknown render error";
          finish(false, message);
        },
        { once: true }
      );

      timeoutId = setTimeout(() => {
        finish(false, "Timed out waiting for rendered/rendererror event");
      }, 20000);
    });

    diagram.mermaid = entry.mermaid;

    card.appendChild(title);
    card.appendChild(meta);
    card.appendChild(diagram);
    diagramsEl.appendChild(card);

    return completion;
  });

  return await Promise.all(renderTasks);
}

function updateSummary(message, passed = null) {
  summaryEl.textContent = message;
  summaryEl.classList.remove("pass", "fail");
  if (passed === true) {
    summaryEl.classList.add("pass");
  } else if (passed === false) {
    summaryEl.classList.add("fail");
  }
}

function slugify(value) {
  return (value || "diagram")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80) || "diagram";
}
