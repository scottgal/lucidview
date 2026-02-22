namespace MermaidSharp.Layout;

internal enum SegmentKind { Line, QuadBezier, CubicBezier }

internal readonly record struct PathSegment(
    SegmentKind Kind,
    double X0, double Y0,
    double X1, double Y1,
    double X2, double Y2,
    double X3, double Y3)
{
    public static PathSegment Line(double x0, double y0, double x1, double y1) =>
        new(SegmentKind.Line, x0, y0, x1, y1, 0, 0, 0, 0);

    public static PathSegment Quad(double x0, double y0, double cx, double cy, double x1, double y1) =>
        new(SegmentKind.QuadBezier, x0, y0, cx, cy, x1, y1, 0, 0);

    public static PathSegment Cubic(double x0, double y0, double cx1, double cy1, double cx2, double cy2, double x1, double y1) =>
        new(SegmentKind.CubicBezier, x0, y0, cx1, cy1, cx2, cy2, x1, y1);

    public double EndX => Kind switch
    {
        SegmentKind.Line => X1,
        SegmentKind.QuadBezier => X2,
        SegmentKind.CubicBezier => X3,
        _ => X1
    };

    public double EndY => Kind switch
    {
        SegmentKind.Line => Y1,
        SegmentKind.QuadBezier => Y2,
        SegmentKind.CubicBezier => Y3,
        _ => Y1
    };
}

internal readonly record struct PointD(double X, double Y);
