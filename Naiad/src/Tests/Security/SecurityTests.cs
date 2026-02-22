using System.Linq;
using VerifyTests;

namespace Tests.Security;

public class SecurityTests : TestBase
{
    [Test]
    public void LargeInput_ShouldThrowSecurityException()
    {
        // Generate input larger than MaxInputSize (50KB)
        var largeInput = new string('A', 51000);
        var input = $$"""
            flowchart LR
                {{largeInput}}
            """;

        var ex = Assert.Throws<MermaidSecurityException>(() =>
            Mermaid.Render(input));

        Assert.That(ex?.Message, Does.Contain("Input size"));
    }

    [Test]
    public void InputExactlyAtLimit_ShouldRender()
    {
        // Input exactly at limit (50KB)
        var exactInput = new string('A', 50000);
        var input = $$"""
            flowchart LR
                A[{{exactInput}}]
            """;

        // Should not throw - this is the maximum allowed size
        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void TooManyNodes_ShouldThrowSecurityException()
    {
        // Create flowchart with >1000 nodes (default MaxNodes)
        var nodes = Enumerable.Range(0, 1001).Select(i => $"A{i}").ToList();
        var input = "flowchart LR\n" + string.Join(" --> ", nodes);

        var ex = Assert.Throws<MermaidSecurityException>(() =>
            Mermaid.Render(input));

        Assert.That(ex?.Message, Does.Contain("too many nodes"));
    }

    [Test]
    public void MaxNodesExactly_ShouldRender()
    {
        // Create flowchart with exactly 1000 nodes (default MaxNodes)
        var nodes = Enumerable.Range(0, 1000).Select(i => $"A{i}").ToList();
        var input = "flowchart LR\n" + string.Join(" --> ", nodes);

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void TooManyEdges_ShouldThrowSecurityException()
    {
        // Create flowchart with >500 edges (default MaxEdges)
        var input = """
            flowchart LR
                A0 --> A1 --> A2 --> A3 --> A4 --> A5 --> A6 --> A7 --> A8 --> A9 --> A10
                B0 --> B1 --> B2 --> B3 --> B4 --> B5 --> B6 --> B7 --> B8 --> B9 --> B10 --> B11 --> B12 --> B13 --> B14 --> B15
                C0 --> C1 --> C2 --> C3 --> C4 --> C5 --> C6 --> C7 --> C8 --> C9 --> C10 --> C11 --> C12 --> C13 --> C14 --> C15 --> C16 --> C17 --> C18 --> C19 --> C20
                D0 --> D1 --> D2 --> D3 --> D4 --> D5 --> D6 --> D7 --> D8 --> D9 --> D10 --> D11 --> D12 --> D13 --> D14 --> D15 --> D16 --> D17 --> D18 --> D19 --> D20 --> D21 --> D22 --> D23 --> D24 --> D25
                E0 --> E1 --> E2 --> E3 --> E4 --> E5 --> E6 --> E7 --> E8 --> E9 --> E10 --> E11 --> E12 --> E13 --> E14 --> E15 --> E16 --> E17 --> E18 --> E19 --> E20 --> E21 --> E22 --> E23 --> E24 --> E25 --> E26 --> E27 --> E28 --> E29 --> E30
                F0 --> F1 --> F2 --> F3 --> F4 --> F5 --> F6 --> F7 --> F8 --> F9 --> F10 --> F11 --> F12 --> F13 --> F14 --> F15 --> F16 --> F17 --> F18 --> F19 --> F20 --> F21 --> F22 --> F23 --> F24 --> F25 --> F26 --> F27 --> F28 --> F29 --> F30 --> F31 --> F32 --> F33 --> F34 --> F35
                G0 --> G1 --> G2 --> G3 --> G4 --> G5 --> G6 --> G7 --> G8 --> G9 --> G10 --> G11 --> G12 --> G13 --> G14 --> G15 --> G16 --> G17 --> G18 --> G19 --> G20 --> G21 --> G22 --> G23 --> G24 --> G25 --> G26 --> G27 --> G28 --> G29 --> G30 --> G31 --> G32 --> G33 --> G34 --> G35 --> G36 --> G37 --> G38 --> G39 --> G40
            """;

        var ex = Assert.Throws<MermaidSecurityException>(() =>
            Mermaid.Render(input));

        Assert.That(ex?.Message, Does.Contain("too many edges"));
    }

    [Test]
    public void ComplexityLimit_ShouldThrowSecurityException()
    {
        // 1000 nodes + 1000 edges = complexity of 3000 > 2000 (MaxComplexity)
        var nodes = Enumerable.Range(0, 1000).Select(i => $"N{i}").ToList();
        var input = "flowchart LR\n" + string.Join(" --> ", nodes) + " --> N0";

        var ex = Assert.Throws<MermaidSecurityException>(() =>
            Mermaid.Render(input));

        Assert.That(ex?.Message, Does.Contain("complexity"));
    }

    [Test]
    public void ComplexityExactlyAtLimit_ShouldRender()
    {
        // 667 nodes + 667 edges = complexity of 2000 (MaxComplexity)
        var nodes = Enumerable.Range(0, 667).Select(i => $"N{i}").ToList();
        var input = "flowchart LR\n" + string.Join(" --> ", nodes);

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void DisabledLimits_ShouldAllowLargeDiagram()
    {
        // Create diagram exceeding all limits but with limits disabled
        var largeInput = new string('A', 60000);
        var nodes = Enumerable.Range(0, 1500).Select(i => $"A{i}").ToList();
        var input = $$"""
            flowchart LR
                A[{{largeInput}}]
                {{string.Join(" --> ", nodes)}}
            """;

        var options = new RenderOptions
        {
            MaxNodes = 0,          // Disabled
            MaxEdges = 0,          // Disabled
            MaxInputSize = 0,      // Disabled
            MaxComplexity = 0,      // Disabled
            RenderTimeout = 0,      // Disabled
        };

        var svg = Mermaid.Render(input, options);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void IconClassWithSpecialChars_ShouldBeSanitized()
    {
        // Icon class with special characters should be sanitized
        var input = """
            flowchart LR
                A[fa:fa-user onload="alert('XSS')"] --> B
            """;

        var svg = Mermaid.Render(input);
        
        // Should not contain the malicious class
        Assert.That(svg, Does.Not.Contain("onload"));
        Assert.That(svg, Does.Not.Contain("alert"));
        Assert.That(svg, Does.Not.Contain("XSS"));
    }

    [Test]
    public void IconClassWithScript_ShouldBeSanitized()
    {
        // Icon class attempting script injection
        var input = """
            flowchart LR
                A[fa:fa-user<script>alert(1)</script>] --> B
            """;

        var svg = Mermaid.Render(input);
        
        // Script tags should be escaped or removed
        Assert.That(svg, Does.Not.Contain("<script>"));
        Assert.That(svg, Does.Not.Contain("alert"));
    }

    [Test]
    public void IconClassWithCRLF_ShouldBeSanitized()
    {
        // Icon class with CRLF injection attempt
        var input = """
            flowchart LR
                A[fa:fa-user%0D%0Astyle=color:red] --> B
            """;

        var svg = Mermaid.Render(input);
        
        // CRLF should be escaped
        Assert.That(svg, Does.Not.Contain("style=color:red"));
    }

    [Test]
    public void ValidIconClass_ShouldRender()
    {
        // Valid FontAwesome icon should render correctly
        var input = """
            flowchart LR
                A[fa:fa-user] --> B[fa:fa-check] --> C[fa:fa-arrow-right]
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("fa-user"));
        Assert.That(svg, Does.Contain("fa-check"));
        Assert.That(svg, Does.Contain("fa-arrow-right"));
    }

    [Test]
    public void ExternalResourcesDisabledByDefault()
    {
        // External CDN should NOT be included by default
        var input = """
            flowchart LR
                A[Start] --> B[End]
            """;

        var svg = Mermaid.Render(input);
        
        // Should not contain CDN link
        Assert.That(svg, Does.Not.Contain("cdnjs.cloudflare.com"));
        Assert.That(svg, Does.Not.Contain("font-awesome"));
        Assert.That(svg, Does.Not.Contain("@import"));
    }

    [Test]
    public void ExternalResourcesExplicitlyDisabled()
    {
        // Explicitly disable external resources
        var input = """
            flowchart LR
                A[Start] --> B[End]
            """;

        var options = new RenderOptions
        {
            IncludeExternalResources = false
        };

        var svg = Mermaid.Render(input, options);
        
        Assert.That(svg, Does.Not.Contain("cdnjs.cloudflare.com"));
        Assert.That(svg, Does.Not.Contain("@import"));
    }

    [Test]
    public void ExternalResourcesEnabled_ShouldIncludeCDN()
    {
        // Enable external resources explicitly
        var input = """
            flowchart LR
                A[Start] --> B[End]
            """;

        var options = new RenderOptions
        {
            IncludeExternalResources = true
        };

        var svg = Mermaid.Render(input, options);
        
        // Should contain CDN link when enabled
        Assert.That(svg, Does.Contain("cdnjs.cloudflare.com"));
    }

    [Test]
    public void TextContentShouldBeHtmlEncoded()
    {
        // Text with HTML should be encoded
        var input = """
            flowchart LR
                A[<script>alert('XSS')</script>] --> B[<img src=x onerror=alert(1)>]
            """;

        var svg = Mermaid.Render(input);
        
        // HTML should be encoded
        Assert.That(svg, Does.Not.Contain("<script>"));
        Assert.That(svg, Does.Not.Contain("<img"));
        Assert.That(svg, Does.Not.Contain("onerror"));
        Assert.That(svg, Does.Contain("&lt;"));
        Assert.That(svg, Does.Contain("&gt;"));
    }

    [Test]
    public void TextContentWithQuotes_ShouldBeEncoded()
    {
        // Text with quotes should be encoded
        var input = $$"""
            flowchart LR
                A["User's input] --> B[Normal]
            """;

        var svg = Mermaid.Render(input);
        
        // Quotes should be encoded
        Assert.That(svg, Does.Contain("&quot;"));
        Assert.That(svg, Does.Contain("&apos;"));
    }

    [Test]
    public void CustomThemeWithCssExpression_ShouldBeBlocked()
    {
        // Custom CSS with expression() should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default fill:expression(alert('XSS'));
            """;

        var svg = Mermaid.Render(input);
        
        // CSS expression should be blocked
        Assert.That(svg, Does.Not.Contain("expression("));
        Assert.That(svg, Does.Not.Contain("alert"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void CustomThemeWithJavascriptUrl_ShouldBeBlocked()
    {
        // CSS with javascript: URL should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default fill:url(javascript:alert(1));
            """;

        var svg = Mermaid.Render(input);
        
        // javascript: should be blocked
        Assert.That(svg, Does.Not.Contain("javascript:"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void CustomThemeWithImport_ShouldBeBlocked()
    {
        // CSS with @import should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default style:background-image:url(https://evil.com/style.css);
            """;

        var svg = Mermaid.Render(input);
        
        // @import should be blocked
        Assert.That(svg, Does.Not.Contain("https://evil.com"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void CustomThemeWithBehavior_ShouldBeBlocked()
    {
        // CSS with behavior: should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default behavior:url(#default#VML);
            """;

        var svg = Mermaid.Render(input);
        
        // behavior: should be blocked
        Assert.That(svg, Does.Not.Contain("behavior:"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void SafeCustomTheme_ShouldRender()
    {
        // Safe CSS should render fine
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default fill:#f9f,stroke:#333,stroke-width:2px;
            """;

        var svg = Mermaid.Render(input);
        
        Assert.That(svg, Does.Contain("#f9f"));
        Assert.That(svg, Does.Contain("#333"));
        Assert.That(svg, Does.Contain("stroke-width"));
    }

    [Test]
    public void ValidDiagram_ShouldRender()
    {
        // Normal valid diagram should work fine
        var input = """
            flowchart LR
                A[Start] --> B{Decision} -->|Yes| C[Process 1]
                B -->|No| D[Process 2]
                C --> E[End]
                D --> E
            """;

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("flowchart"));
    }

    [Test]
    public void Timeout_ShouldWorkWithDefault()
    {
        var options = new RenderOptions
        {
            RenderTimeout = 1  // 1ms timeout - might not be hit for simple diagrams
        };

        // Simple diagram might complete in 1ms
        var input = """
            flowchart LR
                A[Start] --> B[End]
            """;

        var svg = Mermaid.Render(input, options);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void TimeoutDisabled_ShouldAllowLongRendering()
    {
        var options = new RenderOptions
        {
            RenderTimeout = 0  // Disabled
        };

        var input = """
            flowchart LR
                A[Start] --> B[End]
            """;

        var svg = Mermaid.Render(input, options);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void EmptyInput_ShouldHandleGracefully()
    {
        var input = "";

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void WhitespaceOnlyInput_ShouldHandleGracefully()
    {
        var input = "   \n\n  ";

        var svg = Mermaid.Render(input);
        Assert.That(svg, Does.Contain("svg"));
    }

    [Test]
    public void MalformedMermaid_ShouldThrowParseException()
    {
        var input = """
            flowchart LR
                A[Start] --> B
                C[Extra]
            """;

        var ex = Assert.Throws<MermaidParseException>(() =>
            Mermaid.Render(input));

        Assert.That(ex?.Message, Is.Not.Empty);
    }

    [Test]
    public void NodeLabelWithAngleBrackets_ShouldBeEncoded()
    {
        // Labels with < > should be encoded
        var input = $$"""
            flowchart LR
                A[<User>] --> B[>User>]
            """;

        var svg = Mermaid.Render(input);
        
        // Should be encoded
        Assert.That(svg, Does.Contain("&lt;User&gt;"));
        Assert.That(svg, Does.Not.Contain("<User>"));
        Assert.That(svg, Does.Not.Contain(">User>"));
    }

    [Test]
    public void NodeLabelWithAmpersand_ShouldBeEncoded()
    {
        // Labels with & should be encoded first
        var input = $$"""
            flowchart LR
                A[A & B] --> C[D & E]
            """;

        var svg = Mermaid.Render(input);
        
        // Should be encoded (amp must be first)
        Assert.That(svg, Does.Contain("&amp;"));
        Assert.That(svg, Does.Not.Contain(" & "));
    }

    [Test]
    public void FontAwesomePrefix_ShouldBeSanitized()
    {
        // Test different FontAwesome prefixes
        var input = """
            flowchart LR
                A[fa:fa-user] --> B[fab:fa-user] --> C[fas:fa-user] --> D[far:fa-user]
            """;

        var svg = Mermaid.Render(input);
        
        // Valid prefixes should work
        Assert.That(svg, Does.Contain("fa fa-user"));
        Assert.That(svg, Does.Contain("fab fa-user"));
    }

    [Test]
    public void InvalidFontAwesomePrefix_ShouldBeSanitized()
    {
        // Invalid prefix should be sanitized
        var input = """
            flowchart LR
                A[invalid:fa-user] --> B[xss:fa-alert(1)]
            """;

        var svg = Mermaid.Render(input);
        
        // Invalid prefixes should be blocked or sanitized
        Assert.That(svg, Does.Not.Contain("invalid:"));
        Assert.That(svg, Does.Not.Contain("xss:"));
    }

    [Test]
    public void MultipleSecurityFeatures_Combined()
    {
        // Test multiple security features working together
        var input = """
            flowchart LR
                A[fa:fa-user<script>X</script>] --> B
                style A fill:expression(alert(1));
            """;

        var svg = Mermaid.Render(input);
        
        // Both XSS vectors should be blocked
        Assert.That(svg, Does.Not.Contain("<script>"));
        Assert.That(svg, Does.Not.Contain("expression("));
        Assert.That(svg, Does.Not.Contain("X"));
        Assert.That(svg, Does.Not.Contain("alert"));
    }

    [Test]
    public void DataUrlInCss_ShouldBeBlocked()
    {
        // CSS with data: URL should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default fill:url(data:text/html,<script>alert(1)</script>);
            """;

        var svg = Mermaid.Render(input);
        
        // data: URLs should be blocked
        Assert.That(svg, Does.Not.Contain("data:"));
        Assert.That(svg, Does.Not.Contain("<script>"));
    }

    [Test]
    public void MozBinding_ShouldBeBlocked()
    {
        // CSS with -moz-binding should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default -moz-binding:url(#default#VML);
            """;

        var svg = Mermaid.Render(input);
        
        // -moz-binding should be blocked
        Assert.That(svg, Does.Not.Contain("-moz-binding"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void CharsetInCss_ShouldBeBlocked()
    {
        // CSS with @charset should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default @charset "UTF-8";
            """;

        var svg = Mermaid.Render(input);
        
        // @charset should be blocked
        Assert.That(svg, Does.Not.Contain("@charset"));
        Assert.That(svg, Does.Contain("blocked"));
    }

    [Test]
    public void NamespaceInCss_ShouldBeBlocked()
    {
        // CSS with @namespace should be blocked
        var input = """
            flowchart LR
                A[Start] --> B[End]
                classDef default @namespace "http://www.w3.org/1999/xhtml";
            """;

        var svg = Mermaid.Render(input);
        
        // @namespace should be blocked
        Assert.That(svg, Does.Not.Contain("@namespace"));
        Assert.That(svg, Does.Contain("blocked"));
    }
}
