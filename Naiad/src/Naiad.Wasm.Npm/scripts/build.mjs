import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const packageDir = path.resolve(scriptDir, "..");
const repoRoot = path.resolve(packageDir, "..", "..", "..");
const wasmProfile = process.env.NAIAD_WASM_PROFILE ?? "complete";
const includeSkins = (process.env.NAIAD_WASM_INCLUDE_SKINS ?? (wasmProfile === "complete" ? "1" : "0")) !== "0";
const publishTrimmed = (process.env.NAIAD_WASM_TRIM ?? "1") !== "0";
const invariantGlobalization = (process.env.NAIAD_WASM_INVARIANT_GLOBALIZATION ?? "1") !== "0";

const hostProject = path.join(
  repoRoot,
  "Naiad",
  "src",
  "Naiad.Wasm.Host",
  "Naiad.Wasm.Host.csproj"
);

const publishWwwroot = path.join(
  repoRoot,
  "Naiad",
  "src",
  "Naiad.Wasm.Host",
  "bin",
  "Release",
  "net10.0-browser",
  "publish",
  "wwwroot"
);

const distDir = path.join(packageDir, "dist");
const publishDir = path.dirname(publishWwwroot);
const skinsSourceDir = existsSync(path.join(repoRoot, "Naiad", "skins"))
  ? path.join(repoRoot, "Naiad", "skins")
  : path.join(repoRoot, "skins");
const skinConfigSource = path.join(packageDir, "skin-packs.config.json");

safeRemoveDirectory(publishDir);
run("dotnet", [
  "publish",
  hostProject,
  "-c",
  "Release",
  "-v",
  "minimal",
  `-p:PublishTrimmed=${publishTrimmed ? "true" : "false"}`,
  `-p:InvariantGlobalization=${invariantGlobalization ? "true" : "false"}`,
  "-p:DebuggerSupport=false",
  `-p:NaiadWasmProfile=${wasmProfile}`
]);

if (!existsSync(publishWwwroot)) {
  throw new Error(`Expected publish output not found: ${publishWwwroot}`);
}

rmSync(distDir, { recursive: true, force: true });
mkdirSync(distDir, { recursive: true });
cpSync(publishWwwroot, distDir, { recursive: true });
rmSync(path.join(distDir, ".test-server.log"), { force: true });

const distSkinsDir = path.join(distDir, "skins");
rmSync(distSkinsDir, { recursive: true, force: true });
if (includeSkins) {
  if (existsSync(skinsSourceDir)) {
    mkdirSync(distSkinsDir, { recursive: true });
    cpSync(skinsSourceDir, distSkinsDir, { recursive: true });
    cpSync(skinConfigSource, path.join(distSkinsDir, "skin-packs.json"), { force: true });
  } else {
    console.warn(`Skins source not found at ${skinsSourceDir}; skipping external skin assets.`);
  }
}

const retainedTopLevel = new Set([
  "_framework",
  "naiad-web-component.js",
  "naiad-client.js",
  "app.css"
]);

for (const entry of readdirSync(distDir, { withFileTypes: true })) {
  if (retainedTopLevel.has(entry.name) || entry.name === "skins") {
    continue;
  }

  rmSync(path.join(distDir, entry.name), { recursive: true, force: true });
}

pruneFiles(distDir, [".br", ".gz", ".pdb", ".map"]);

console.log(`Synced npm dist from ${publishWwwroot}`);
console.log(`WASM profile: ${wasmProfile}`);
console.log(`PublishTrimmed: ${publishTrimmed}`);
console.log(`InvariantGlobalization: ${invariantGlobalization}`);
if (includeSkins) {
  if (existsSync(path.join(distDir, "skins"))) {
    console.log(`Copied skin packs and config to ${distSkinsDir}`);
  }
}
console.log(`Dist ready at ${distDir}`);

function run(command, args) {
  const result = spawnSync(command, args, {
    stdio: "inherit"
  });

  if (result.status !== 0) {
    throw new Error(`Command failed: ${command} ${args.join(" ")}`);
  }
}

function pruneFiles(rootDir, extensions) {
  for (const entry of readdirSync(rootDir, { withFileTypes: true })) {
    const fullPath = path.join(rootDir, entry.name);

    if (entry.isDirectory()) {
      pruneFiles(fullPath, extensions);
      continue;
    }

    if (extensions.some(ext => entry.name.endsWith(ext))) {
      rmSync(fullPath, { force: true });
    }
  }
}

function safeRemoveDirectory(dirPath) {
  try {
    rmSync(dirPath, { recursive: true, force: true });
  } catch (error) {
    if (error?.code !== "EPERM") {
      throw error;
    }

    console.warn(`Could not clean ${dirPath} due to file lock; continuing with dotnet publish.`);
  }
}
