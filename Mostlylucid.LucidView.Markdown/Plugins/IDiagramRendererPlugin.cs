using Avalonia;

namespace Mostlylucid.LucidView.Markdown.Plugins;

public interface IDiagramRendererPlugin
{
    string Name { get; }

    /// <summary>
    /// Swaps every diagram marker found under <paramref name="root"/> for the control that draws it,
    /// and returns how many were swapped. The count is what lets a caller tell "the markers are not
    /// laid out yet, come back after the next layout pass" apart from "there is nothing left to do".
    /// </summary>
    int ReplaceDiagramMarkers(Visual root);
}
