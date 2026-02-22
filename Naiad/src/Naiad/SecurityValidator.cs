using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MermaidSharp;

/// <summary>
/// Provides security validation and resource limit checking for Mermaid diagram rendering.
/// </summary>
public static class SecurityValidator
{
    private static readonly Regex SafeIconName = new(@"^[a-z-]+$", RegexCompat.Compiled | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Validates input against security limits and throws if exceeded.
    /// </summary>
    public static void ValidateInput(string input, RenderOptions options)
    {
        if (string.IsNullOrEmpty(input))
            return;
        
        if (options.MaxInputSize > 0 && input.Length > options.MaxInputSize)
        {
            throw new MermaidSecurityException($"Input size ({input.Length}) exceeds maximum allowed size ({options.MaxInputSize})");
        }
    }
    
    /// <summary>
    /// Validates diagram complexity against configured limits.
    /// </summary>
    public static void ValidateComplexity(int nodeCount, int edgeCount, RenderOptions options)
    {
        if (options.MaxNodes > 0 && nodeCount > options.MaxNodes)
        {
            throw new MermaidSecurityException($"Diagram has too many nodes ({nodeCount} > {options.MaxNodes})");
        }
        
        if (options.MaxEdges > 0 && edgeCount > options.MaxEdges)
        {
            throw new MermaidSecurityException($"Diagram has too many edges ({edgeCount} > {options.MaxEdges})");
        }
        
        if (options.MaxComplexity > 0)
        {
            var complexity = nodeCount + (edgeCount * 2);
            if (complexity > options.MaxComplexity)
            {
                throw new MermaidSecurityException($"Diagram complexity ({complexity}) exceeds maximum ({options.MaxComplexity})");
            }
        }
    }
    
    /// <summary>
    /// Sanitizes and validates an icon class name.
    /// Only allows lowercase letters and hyphens to prevent class injection.
    /// </summary>
    public static string SanitizeIconClass(string? iconClass)
    {
        if (string.IsNullOrEmpty(iconClass))
            return string.Empty;
        
        var sanitized = iconClass.Trim();
        
        if (!SafeIconName.IsMatch(sanitized))
        {
            return string.Empty;
        }
        
        return sanitized;
    }
    
    /// <summary>
    /// Sanitizes CSS to prevent CSS injection attacks.
    /// Removes dangerous CSS properties and expressions.
    /// </summary>
    public static string SanitizeCss(string? css)
    {
        if (string.IsNullOrEmpty(css))
            return string.Empty;
        
        var sanitized = css;
        
        var dangerousPatterns = new[]
        {
            (Pattern: @"expression\s*\(", Replacement: " /* blocked */ "),
            (Pattern: @"javascript\s*:", Replacement: " /* blocked */ "),
            (Pattern: @"behavior\s*:", Replacement: " /* blocked */ "),
            (Pattern: @"-moz-binding\s*:", Replacement: " /* blocked */ "),
            (Pattern: @"@import\s+url\s*\(", Replacement: " /* blocked */ "),
            (Pattern: @"@import\s+", Replacement: " /* blocked */ "),
            (Pattern: @"@charset\s+", Replacement: " /* blocked */ "),
            (Pattern: @"@namespace\s+", Replacement: " /* blocked */ "),
            (Pattern: @"url\s*\(\s*data\s*:", Replacement: "url(#)"),
            (Pattern: @"url\s*\(\s*javascript\s*:", Replacement: "url(#)"),
        };
        
        foreach (var (Pattern, Replacement) in dangerousPatterns)
        {
            sanitized = Regex.Replace(sanitized, Pattern, Replacement, RegexOptions.IgnoreCase);
        }
        
        return sanitized;
    }
    
    /// <summary>
    /// Wraps an action with a timeout and throws if exceeded.
    /// </summary>
    public static T WithTimeout<T>(Func<T> action, int timeoutMs, string operationName)
    {
        if (timeoutMs <= 0)
        {
            return action();
        }

        // Browser WASM runs without reliable preemptive cancellation for sync work.
        // Execute directly and rely on input/complexity limits.
        if (OperatingSystem.IsBrowser())
        {
            return action();
        }
        
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => action(), cts.Token);
        
        if (task.Wait(timeoutMs))
        {
            return task.Result;
        }
        
        cts.Cancel();
        throw new MermaidSecurityException($"Operation '{operationName}' exceeded timeout of {timeoutMs}ms");
    }
}

/// <summary>
/// Exception thrown when security limits are exceeded.
/// </summary>
public class MermaidSecurityException(string message) : Exception(message);
