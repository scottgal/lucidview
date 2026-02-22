using System.Text.RegularExpressions;

namespace MermaidSharp;

internal static class RegexCompat
{
    internal static RegexOptions Compiled =>
        OperatingSystem.IsBrowser() ? RegexOptions.None : RegexOptions.Compiled;
}
