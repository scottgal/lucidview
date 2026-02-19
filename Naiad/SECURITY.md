# Security Policy for Naiad Mermaid Rendering

## Overview

Naiad is a pure C# implementation of Mermaid diagram rendering. This document describes the security model, threat vectors, and mitigations implemented to protect against malicious input.

## Threat Model

Naiad is designed to render Mermaid diagrams from potentially untrusted sources, including:
- User-provided markdown files
- Network-fetched markdown documents
- Code snippets in documentation

### Primary Attack Vectors

1. **Denial of Service (DoS)** - Attacks that exhaust CPU, memory, or time
2. **Code Injection** - Attacks that execute arbitrary code via SVG/CSS/HTML
3. **Cross-Site Scripting (XSS)** - Attacks that inject malicious scripts into rendered output
4. **Resource Exfiltration** - Loading external resources that leak information
5. **CSS Injection** - Using CSS properties to modify page behavior

## Security Features

### 1. Resource Limits

All `RenderOptions` include configurable security limits (defaults shown):

```csharp
public int MaxNodes { get; set; } = 1000;
public int MaxEdges { get; set; } = 500;
public int MaxComplexity { get; set; } = 2000;
public int MaxInputSize { get; set; } = 50000;
public int RenderTimeout { get; set; } = 10000;
```

**Mitigations:**
- `ValidateInput()` - Rejects diagrams exceeding `MaxInputSize` characters
- `ValidateComplexity()` - Rejects diagrams with too many nodes/edges
- `WithTimeout()` - Aborts rendering if `RenderTimeout` milliseconds exceeded

### 2. Input Sanitization

#### Icon Class Sanitization
FontAwesome icon classes are validated to prevent injection:

```csharp
public static string SanitizeIconClass(string? iconClass)
{
    // Only allows lowercase letters and hyphens
    // e.g., "fa-user" is allowed, "fa-user onload" is rejected
    var sanitized = iconClass.Trim();
    if (!SafeIconName.IsMatch(sanitized))
        return string.Empty;
    return sanitized;
}
```

#### CSS Sanitization
User-provided CSS is sanitized to block dangerous properties:

```csharp
public static string SanitizeCss(string? css)
{
    // Blocks: expression(), javascript:, behavior:, @import, etc.
    // Replaces dangerous patterns with comments
}
```

**Blocked Patterns:**
- `expression()` - CSS expression execution
- `javascript:` - JavaScript URIs
- `behavior:` - IE behaviors
- `-moz-binding:` - XBL binding
- `@import url()` - External stylesheet loading
- `@charset`, `@namespace` - Character set/namespace attacks
- `url(data:)` - Data URLs (potential for scripts)

### 3. XML/HTML Escaping

All text content is properly escaped before SVG output:

```csharp
static string EscapeXml(string text) =>
    text
        .Replace("&", "&amp;")    // MUST be first
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
```

**Note:** The `&` replacement **must** be first to prevent double-escaping issues.

### 4. External Resource Control

By default, external resources (CDNs) are disabled:

```csharp
public bool IncludeExternalResources { get; set; } = false;
```

**Behavior:**
- `true` - Includes FontAwesome CDN link
- `false` - Omits external resources entirely (secure default)

### 5. Output Encoding

HTML labels use `System.Net.WebUtility.HtmlEncode()` for XSS protection:

```csharp
html.Append(System.Net.WebUtility.HtmlEncode(textBefore));
```

SVG text uses `EscapeXml()` for XML injection protection.

## Security Configuration

### Secure Defaults

All security limits have secure defaults to protect against accidental misuse:

| Setting | Default | Purpose |
|----------|---------|---------|
| MaxNodes | 1000 | Prevent memory exhaustion |
| MaxEdges | 500 | Prevent complex graphs |
| MaxComplexity | 2000 | Combined complexity check |
| MaxInputSize | 50000 | Prevent large inputs |
| RenderTimeout | 10000ms | Prevent CPU exhaustion |
| IncludeExternalResources | false | Prevent CDN tracking |

### Disabling Limits

To disable limits (not recommended for untrusted input):

```csharp
var options = new RenderOptions
{
    MaxNodes = 0,          // Disable node limit
    MaxInputSize = 0,      // Disable size limit
    RenderTimeout = 0,      // Disable timeout
};
```

⚠️ **Warning:** Only disable limits when rendering trusted, local content.

## Recommended Security Practices

### For Rendering User Content

```csharp
var options = new RenderOptions
{
    // Keep all limits at defaults
    MaxNodes = 1000,
    MaxEdges = 500,
    MaxInputSize = 50000,
    RenderTimeout = 10000,
    
    // Do NOT include external resources
    IncludeExternalResources = false,
};

try
{
    var svg = Mermaid.Render(userInput, options);
}
catch (MermaidSecurityException ex)
{
    // Log security violations
    logger.LogWarning($"Rejected malicious diagram: {ex.Message}");
}
```

### For Rendering Trusted Content

```csharp
var options = new RenderOptions
{
    // Can relax limits for known-safe diagrams
    MaxNodes = 0,          // No limit
    MaxInputSize = 0,      // No limit
    RenderTimeout = 0,      // No timeout
    
    // Include FontAwesome if needed
    IncludeExternalResources = true,
};

var svg = Mermaid.Render(trustedContent, options);
```

## Known Limitations

### What Naiad Does NOT Sanitize

1. **Mermaid Syntax Errors** - Invalid diagram syntax throws `MermaidParseException`
2. **Memory Limits** - No absolute memory cap (relies on system limits)
3. **Concurrent Renders** - No built-in rate limiting (caller must implement)

### What Naiad DOES Sanitize

1. ✅ All text content (HTML-encoded or XML-escaped)
2. ✅ Icon class names (alphanumeric + hyphens only)
3. ✅ CSS properties (dangerous patterns blocked)
4. ✅ Input size (enforced via `MaxInputSize`)
5. ✅ Diagram complexity (enforced via `MaxNodes`/`MaxEdges`)
6. ✅ Rendering time (enforced via `RenderTimeout`)

## Reporting Security Issues

If you discover a security vulnerability in Naiad:

1. **Do not create a public issue** - This alerts attackers
2. Email: security@ (repository owner)
3. Include:
   - Affected versions
   - Minimal reproduction steps
   - Impact assessment
4. Allow reasonable time for response before disclosure

## Version History

| Version | Changes |
|---------|----------|
| 0.1.1+ | Added security limits, CSS sanitization, input validation |
| 0.1.0 | Initial release without security features |

## References

- [OWASP XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
- [OWASP SVG Security](https://cheatsheetseries.owasp.org/cheatsheets/SVG_Security_Cheat_Sheet.html)
- [CSS Security](https://web.dev/secure-csp/)
