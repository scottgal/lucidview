# Naiad.Blazor

Blazor wrapper for the `<naiad-diagram>` web component.

## Install

```bash
dotnet add package Naiad.Blazor
```

## Register

In `Program.cs`:

```csharp
using Naiad.Blazor;

builder.Services.AddNaiadBlazor(options =>
{
    // Default is Mermaid profile.
    // options.ScriptUrl = NaiadBlazorProfiles.Mermaid;
    options.ScriptUrl = NaiadBlazorProfiles.Complete;
});
```

## Use

In `_Imports.razor`:

```razor
@using Naiad.Blazor
```

In a page/component:

```razor
<NaiadDiagram Mermaid="@diagram"
              Theme="auto"
              FitWidth="true"
              ShowMenu="true"
              OptionsJson='{"skinPack":"material3"}'
              style="min-height: 280px;" />

@code {
    private const string diagram = """
    flowchart LR
      A[Blazor] --> B[Naiad]
      B --> C[SVG]
    """;
}
```

## Script Sources

Built-in profile constants:

- `NaiadBlazorProfiles.Mermaid`
- `NaiadBlazorProfiles.Complete`

You can also set a self-hosted script URL:

```csharp
builder.Services.AddNaiadBlazor(o =>
    o.ScriptUrl = "/js/naiad-web-component.js");
```

## In-Repo Sample

See:

- `Naiad/src/Naiad.Blazor.Sample`
