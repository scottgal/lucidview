namespace MermaidSharp.Layout;

static class LayoutUtils
{
    public static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        if (values.Count == 1) return values[0];
        if (values.Count == 2) return (values[0] + values[1]) / 2;

        var mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) / 2
            : values[mid];
    }
}
