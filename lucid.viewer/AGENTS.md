# Repository Guidelines

## Project Structure & Module Organization
- Solution file: `lucid.viewer.sln`.
- App project: `lucid.viewer/`.
- UI markup: `lucid.viewer/Views/*.axaml` with code-behind in `*.axaml.cs`.
- View models: `lucid.viewer/ViewModels/`.
- App entry points and wiring: `lucid.viewer/Program.cs`, `App.axaml`, `ViewLocator.cs`.
- Static assets (icons/images/fonts): `lucid.viewer/Assets/`.
- Build output appears under `lucid.viewer/bin/` and `lucid.viewer/obj/` (do not commit).

## Security Policy

### Naiad (Mermaid Rendering)
The Naiad fork includes security features for rendering untrusted Mermaid diagrams:
- Resource limits: Max nodes (1000), edges (500), input size (50KB), timeout (10s)
- Input sanitization: CSS injection protection, icon class validation
- XSS prevention: XML/HTML encoding for all text content
- External resource control: FontAwesome CDN disabled by default

**Always use secure defaults for user content:**
```csharp
var options = new RenderOptions
{
    IncludeExternalResources = false  // No CDNs
    // All limits at defaults
};
```

See `Naiad/SECURITY.md` for full threat model.

### General Security
- Never log or include secrets/credentials in code
- Validate all user input before rendering
- Use parameterized queries (if database access is added)

## Build, Test, and Development Commands
- `dotnet restore lucid.viewer.sln`  
  Restores NuGet dependencies.
- `dotnet build lucid.viewer.sln -c Debug`  
  Compiles the desktop app for local development.
- `dotnet run --project lucid.viewer/lucid.viewer.csproj`  
  Launches the Avalonia app locally.
- `dotnet build lucid.viewer.sln -c Release`  
  Produces release binaries for validation.

## Coding Style & Naming Conventions
- Language: C# (`net9.0`) with nullable reference types enabled.
- Use 4-space indentation and braces on new lines (follow existing files).
- Prefer file-scoped namespaces (`namespace X;`) where already used.
- Naming:
  - Types and public members: `PascalCase`
  - Local variables/parameters: `camelCase`
  - XAML views: `MainWindow.axaml` + matching `MainWindow.axaml.cs`
- Keep MVVM boundaries clear: view logic in `Views`, state and bindable data in `ViewModels`.

## Testing Guidelines
- No dedicated test project is present yet in this repository.
- Minimum gate before PR: run `dotnet build` in Debug and Release and smoke-test the app with `dotnet run`.
- When adding tests, create a sibling test project (for example `lucid.viewer.Tests`) and name test files as `<ClassName>Tests.cs`.

## Commit & Pull Request Guidelines
- Follow the existing commit style from history: concise, imperative subjects (for example, `Refactor font settings...`, `Update README...`).
- Keep commits focused to one logical change.
- PRs should include:
  - What changed and why
  - Manual verification steps/commands
  - Screenshots or short recordings for UI changes (window chrome, layout, rendering)
  - Linked issue/task when applicable
