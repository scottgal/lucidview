import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const packageDir = path.resolve(scriptDir, "..");
const repoRoot = path.resolve(packageDir, "..", "..", "..");

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
const debugWwwroot = path.join(
  repoRoot,
  "Naiad",
  "src",
  "Naiad.Wasm.Host",
  "bin",
  "Debug",
  "net10.0-browser",
  "wwwroot"
);
const debugFrameworkDir = path.join(debugWwwroot, "_framework");

const distDir = path.join(packageDir, "dist");
const publishDir = path.dirname(publishWwwroot);
const skinsSourceDir = path.join(repoRoot, "Naiad", "skins");
const skinConfigSource = path.join(packageDir, "skin-packs.config.json");
const debugRuntimeInfoDll = path.join(
  repoRoot,
  "Naiad",
  "src",
  "Naiad.Wasm.Host",
  "bin",
  "Debug",
  "net10.0-browser",
  "System.Runtime.InteropServices.RuntimeInformation.dll"
);

rmSync(publishDir, { recursive: true, force: true });
run("dotnet", [
  "publish",
  hostProject,
  "-c",
  "Release",
  "-v",
  "minimal",
  "-p:PublishTrimmed=false"
]);
run("dotnet", ["build", hostProject, "-c", "Debug", "-v", "minimal"]);

if (!existsSync(publishWwwroot)) {
  throw new Error(`Expected publish output not found: ${publishWwwroot}`);
}

const publishRuntimeInfoDll = path.join(
  publishWwwroot,
  "_framework",
  "System.Runtime.InteropServices.RuntimeInformation.dll"
);
if (!existsSync(publishRuntimeInfoDll) && existsSync(debugRuntimeInfoDll)) {
  cpSync(debugRuntimeInfoDll, publishRuntimeInfoDll, { force: true });
}

rmSync(distDir, { recursive: true, force: true });
mkdirSync(distDir, { recursive: true });
cpSync(publishWwwroot, distDir, { recursive: true });
rmSync(path.join(distDir, ".test-server.log"), { force: true });
rmSync(path.join(distDir, "client-diagnostics.html"), { force: true });
rmSync(path.join(distDir, "client-diagnostics.html.br"), { force: true });
rmSync(path.join(distDir, "client-diagnostics.html.gz"), { force: true });

const distFrameworkDir = path.join(distDir, "_framework");
if (existsSync(debugFrameworkDir)) {
  rmSync(distFrameworkDir, { recursive: true, force: true });
  cpSync(debugFrameworkDir, distFrameworkDir, { recursive: true });
  console.log(`Normalized dist framework assets from ${debugFrameworkDir}`);
}

const distSkinsDir = path.join(distDir, "skins");
rmSync(distSkinsDir, { recursive: true, force: true });
mkdirSync(distSkinsDir, { recursive: true });
cpSync(skinsSourceDir, distSkinsDir, { recursive: true });
cpSync(skinConfigSource, path.join(distSkinsDir, "skin-packs.json"), { force: true });

console.log(`Synced npm dist from ${publishWwwroot}`);
console.log(`Copied skin packs and config to ${distSkinsDir}`);
console.log(`Dist ready at ${distDir}`);

function run(command, args) {
  const result = spawnSync(command, args, {
    stdio: "inherit"
  });

  if (result.status !== 0) {
    throw new Error(`Command failed: ${command} ${args.join(" ")}`);
  }
}
