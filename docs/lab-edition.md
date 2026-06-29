# lucidLAB Edition

lucidLAB is the dogfood testbed for lucidRAG + lucidSupport infrastructure that StyloBot will eventually consume. Dogfood-only, Debug-only, not shipped in releases.

See `docs/superpowers/specs/2026-06-29-lucidlab-design.md` for the design and constraints.

## NuGet feed setup

lucidLAB consumes OSS packages from `nuget.org` and commercial packages from GitHub Packages at `https://nuget.pkg.github.com/scottgal/index.json`. The repo-level `NuGet.Config` declares both sources; credentials are per-environment.

### CI

In `.github/workflows/ci.yml`, the Lab matrix runs `dotnet nuget add source ... --username scottgal --password $GITHUB_TOKEN --store-password-in-clear-text` before `dotnet restore`.

### Local development

Add a PAT with `read:packages` scope to your user-scope NuGet config:

- macOS/Linux: `~/.config/NuGet/NuGet.Config`
- Windows: `%APPDATA%\NuGet\NuGet.Config`

```xml
<configuration>
  <packageSourceCredentials>
    <github-lucidrag-commercial>
      <add key="Username" value="scottgal" />
      <add key="ClearTextPassword" value="ghp_..." />
    </github-lucidrag-commercial>
  </packageSourceCredentials>
</configuration>
```

Never check the PAT into the repo.