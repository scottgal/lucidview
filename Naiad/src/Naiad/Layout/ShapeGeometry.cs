namespace MermaidSharp.Layout;

/// <summary>
/// Geometric algorithms operating on <see cref="PathSegment"/> lists:
/// centroid, ray-cast intersection (with Cardano's formula for cubics),
/// arc-length parameterization, and tangent computation.
/// </summary>
internal static class ShapeGeometry
{
    const double Epsilon = 1e-9;

    /// <summary>
    /// Scale a unit-space segment list to fit a node at (cx, cy) with given width/height.
    /// Unit space is assumed to be [0, unitW] x [0, unitH]; output is centered on (cx, cy).
    /// </summary>
    public static List<PathSegment> ScaleToNode(
        List<PathSegment> unitSegments,
        double unitW, double unitH,
        double cx, double cy, double width, double height)
    {
        if (unitSegments.Count == 0) return unitSegments;

        var sx = width / Math.Max(unitW, Epsilon);
        var sy = height / Math.Max(unitH, Epsilon);
        var ox = cx - width / 2;
        var oy = cy - height / 2;

        var result = new List<PathSegment>(unitSegments.Count);
        foreach (var seg in unitSegments)
        {
            result.Add(seg.Kind switch
            {
                SegmentKind.Line => PathSegment.Line(
                    seg.X0 * sx + ox, seg.Y0 * sy + oy,
                    seg.X1 * sx + ox, seg.Y1 * sy + oy),
                SegmentKind.QuadBezier => PathSegment.Quad(
                    seg.X0 * sx + ox, seg.Y0 * sy + oy,
                    seg.X1 * sx + ox, seg.Y1 * sy + oy,
                    seg.X2 * sx + ox, seg.Y2 * sy + oy),
                SegmentKind.CubicBezier => PathSegment.Cubic(
                    seg.X0 * sx + ox, seg.Y0 * sy + oy,
                    seg.X1 * sx + ox, seg.Y1 * sy + oy,
                    seg.X2 * sx + ox, seg.Y2 * sy + oy,
                    seg.X3 * sx + ox, seg.Y3 * sy + oy),
                _ => seg
            });
        }
        return result;
    }

    /// <summary>
    /// Compute the geometric centroid of a closed path using the shoelace formula
    /// on linearized points. Falls back to bounding-box center.
    /// </summary>
    public static PointD Centroid(List<PathSegment> segments)
    {
        if (segments.Count == 0) return new(0, 0);

        // Linearize to polygon vertices
        var pts = Linearize(segments, 8);
        if (pts.Count < 3)
        {
            return BoundingBoxCenter(segments);
        }

        // Shoelace centroid
        double area = 0, cx = 0, cy = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var j = (i + 1) % pts.Count;
            var cross = pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            area += cross;
            cx += (pts[i].X + pts[j].X) * cross;
            cy += (pts[i].Y + pts[j].Y) * cross;
        }

        area /= 2;
        if (Math.Abs(area) < Epsilon)
            return BoundingBoxCenter(segments);

        cx /= (6 * area);
        cy /= (6 * area);
        return new(cx, cy);
    }

    /// <summary>
    /// Ray-cast from <paramref name="origin"/> in <paramref name="direction"/>
    /// to find the nearest intersection with the path boundary.
    /// Uses exact analytical solutions: parametric for lines, quadratic formula
    /// for quadratic Beziers, and Cardano's formula for cubic Beziers.
    /// </summary>
    public static PointD? IntersectRay(List<PathSegment> segments, PointD origin, PointD direction)
    {
        if (segments.Count == 0) return null;

        var len = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (len < Epsilon) return null;
        var dx = direction.X / len;
        var dy = direction.Y / len;

        PointD? best = null;
        var bestDist = double.MaxValue;

        foreach (var seg in segments)
        {
            var hits = seg.Kind switch
            {
                SegmentKind.Line => IntersectRayLine(origin, dx, dy, seg),
                SegmentKind.QuadBezier => IntersectRayQuad(origin, dx, dy, seg),
                SegmentKind.CubicBezier => IntersectRayCubic(origin, dx, dy, seg),
                _ => []
            };

            foreach (var hit in hits)
            {
                var dist = (hit.X - origin.X) * dx + (hit.Y - origin.Y) * dy;
                if (dist > Epsilon && dist < bestDist)
                {
                    bestDist = dist;
                    best = hit;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Compute a point at fractional position <paramref name="t"/> ∈ [0,1] along the
    /// total arc length of the path. Uses Gauss-Legendre quadrature for Bezier arc lengths.
    /// </summary>
    public static PointD PointAtFraction(List<PathSegment> segments, double t)
    {
        if (segments.Count == 0) return new(0, 0);
        t = Math.Clamp(t, 0, 1);

        // Compute cumulative arc lengths
        var lengths = new double[segments.Count];
        double total = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            lengths[i] = SegmentArcLength(segments[i]);
            total += lengths[i];
        }

        if (total < Epsilon)
            return new(segments[0].X0, segments[0].Y0);

        var target = t * total;
        double cumulative = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            if (cumulative + lengths[i] >= target - Epsilon)
            {
                var localT = lengths[i] > Epsilon
                    ? (target - cumulative) / lengths[i]
                    : 0;
                return EvalSegment(segments[i], Math.Clamp(localT, 0, 1));
            }
            cumulative += lengths[i];
        }

        // Fallback: end of last segment
        var last = segments[^1];
        return new(last.EndX, last.EndY);
    }

    /// <summary>
    /// Compute the tangent vector at a given point on the path.
    /// Returns a unit vector tangent to the boundary at the nearest segment.
    /// </summary>
    public static PointD TangentAtPoint(List<PathSegment> segments, PointD point)
    {
        if (segments.Count == 0) return new(1, 0);

        // Find nearest segment and parameter
        var bestSeg = segments[0];
        var bestT = 0.0;
        var bestDist = double.MaxValue;

        foreach (var seg in segments)
        {
            var (nearT, nearDist) = NearestParameterOnSegment(seg, point);
            if (nearDist < bestDist)
            {
                bestDist = nearDist;
                bestSeg = seg;
                bestT = nearT;
            }
        }

        return TangentOnSegment(bestSeg, bestT);
    }

    /// <summary>
    /// Compute the outward-facing normal at a boundary point.
    /// </summary>
    public static PointD NormalAtPoint(List<PathSegment> segments, PointD point, PointD centroid)
    {
        var tangent = TangentAtPoint(segments, point);
        // Normal is perpendicular to tangent
        var nx = -tangent.Y;
        var ny = tangent.X;

        // Ensure it points outward (away from centroid)
        var toCentroid = new PointD(centroid.X - point.X, centroid.Y - point.Y);
        if (nx * toCentroid.X + ny * toCentroid.Y > 0)
        {
            nx = -nx;
            ny = -ny;
        }

        return new(nx, ny);
    }

    #region Ray-Segment Intersection

    static List<PointD> IntersectRayLine(PointD origin, double dx, double dy, PathSegment seg)
    {
        // Ray: P = origin + t*(dx,dy), t > 0
        // Line segment: P = A + s*(B-A), s ∈ [0,1]
        var ax = seg.X0; var ay = seg.Y0;
        var bx = seg.X1; var by = seg.Y1;
        var ex = bx - ax; var ey = by - ay;

        // Cramer's rule denominator for the 2×2 system:
        //   t*dx - s*ex = ax - origin.X
        //   t*dy - s*ey = ay - origin.Y
        // det = -dx*ey + dy*ex  (note: NOT dx*ey - dy*ex)
        var denom = dy * ex - dx * ey;
        if (Math.Abs(denom) < Epsilon) return [];

        var s = (dx * (ay - origin.Y) - dy * (ax - origin.X)) / denom;
        if (s < -Epsilon || s > 1 + Epsilon) return [];

        var t = (ex * (origin.Y - ay) - ey * (origin.X - ax)) / -denom;
        if (t < Epsilon) return [];

        return [new(origin.X + t * dx, origin.Y + t * dy)];
    }

    static List<PointD> IntersectRayQuad(PointD origin, double dx, double dy, PathSegment seg)
    {
        // Quadratic Bezier: B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
        // Substitute into ray equation and solve
        var p0x = seg.X0 - origin.X; var p0y = seg.Y0 - origin.Y;
        var p1x = seg.X1 - origin.X; var p1y = seg.Y1 - origin.Y;
        var p2x = seg.X2 - origin.X; var p2y = seg.Y2 - origin.Y;

        // B(t) cross direction = 0
        // (B_x(t)*dy - B_y(t)*dx) = 0
        var a = (p0x - 2 * p1x + p2x) * dy - (p0y - 2 * p1y + p2y) * dx;
        var b = 2 * ((p1x - p0x) * dy - (p1y - p0y) * dx);
        var c = p0x * dy - p0y * dx;

        var results = new List<PointD>();
        foreach (var t in SolveQuadratic(a, b, c))
        {
            if (t < -Epsilon || t > 1 + Epsilon) continue;
            var tc = Math.Clamp(t, 0, 1);
            var pt = EvalQuad(seg, tc);
            // Verify point is in ray direction
            var dot = (pt.X - origin.X) * dx + (pt.Y - origin.Y) * dy;
            if (dot > Epsilon)
                results.Add(pt);
        }
        return results;
    }

    static List<PointD> IntersectRayCubic(PointD origin, double dx, double dy, PathSegment seg)
    {
        // Cubic Bezier: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
        // Solve (B_x(t)*dy - B_y(t)*dx) = 0 using Cardano's formula
        var p0x = seg.X0 - origin.X; var p0y = seg.Y0 - origin.Y;
        var p1x = seg.X1 - origin.X; var p1y = seg.Y1 - origin.Y;
        var p2x = seg.X2 - origin.X; var p2y = seg.Y2 - origin.Y;
        var p3x = seg.X3 - origin.X; var p3y = seg.Y3 - origin.Y;

        // Coefficients of t³, t², t, 1 for cross product with ray direction
        var fx0 = p0x; var fy0 = p0y;
        var fx1 = -3 * p0x + 3 * p1x; var fy1 = -3 * p0y + 3 * p1y;
        var fx2 = 3 * p0x - 6 * p1x + 3 * p2x; var fy2 = 3 * p0y - 6 * p1y + 3 * p2y;
        var fx3 = -p0x + 3 * p1x - 3 * p2x + p3x; var fy3 = -p0y + 3 * p1y - 3 * p2y + p3y;

        var a = fx3 * dy - fy3 * dx;
        var b = fx2 * dy - fy2 * dx;
        var c = fx1 * dy - fy1 * dx;
        var d = fx0 * dy - fy0 * dx;

        var results = new List<PointD>();
        foreach (var t in SolveCubic(a, b, c, d))
        {
            if (t < -Epsilon || t > 1 + Epsilon) continue;
            var tc = Math.Clamp(t, 0, 1);
            var pt = EvalCubic(seg, tc);
            var dot = (pt.X - origin.X) * dx + (pt.Y - origin.Y) * dy;
            if (dot > Epsilon)
                results.Add(pt);
        }
        return results;
    }

    #endregion

    #region Polynomial Solvers

    static double[] SolveQuadratic(double a, double b, double c)
    {
        if (Math.Abs(a) < Epsilon)
        {
            if (Math.Abs(b) < Epsilon) return [];
            return [-c / b];
        }

        var disc = b * b - 4 * a * c;
        if (disc < -Epsilon) return [];

        if (disc < Epsilon)
            return [-b / (2 * a)];

        var sqrtDisc = Math.Sqrt(disc);
        return [(-b + sqrtDisc) / (2 * a), (-b - sqrtDisc) / (2 * a)];
    }

    /// <summary>
    /// Solve cubic equation at³ + bt² + ct + d = 0 using Cardano's formula.
    /// Returns all real roots.
    /// </summary>
    static double[] SolveCubic(double a, double b, double c, double d)
    {
        if (Math.Abs(a) < Epsilon)
            return SolveQuadratic(b, c, d);

        // Normalize: t³ + pt² + qt + r = 0
        var p = b / a;
        var q = c / a;
        var r = d / a;

        // Depressed cubic substitution: t = u - p/3
        // u³ + au + b = 0 where:
        var da = (3 * q - p * p) / 3;
        var db = (2 * p * p * p - 9 * p * q + 27 * r) / 27;

        var disc = db * db / 4 + da * da * da / 27;

        if (disc > Epsilon)
        {
            // One real root
            var sqrtDisc = Math.Sqrt(disc);
            var u = CubeRoot(-db / 2 + sqrtDisc);
            var v = CubeRoot(-db / 2 - sqrtDisc);
            return [u + v - p / 3];
        }

        if (disc > -Epsilon)
        {
            // Double or triple root
            if (Math.Abs(db) < Epsilon)
                return [-p / 3]; // triple root

            var u = CubeRoot(-db / 2);
            return [2 * u - p / 3, -u - p / 3];
        }

        // Three real roots (casus irreducibilis) — trigonometric method
        var rr = Math.Sqrt(-da * da * da / 27);
        var theta = Math.Acos(Math.Clamp(-db / (2 * rr), -1, 1));
        var mag = 2 * CubeRoot(rr);

        return
        [
            mag * Math.Cos(theta / 3) - p / 3,
            mag * Math.Cos((theta + 2 * Math.PI) / 3) - p / 3,
            mag * Math.Cos((theta + 4 * Math.PI) / 3) - p / 3
        ];
    }

    static double CubeRoot(double x) =>
        x >= 0 ? Math.Pow(x, 1.0 / 3.0) : -Math.Pow(-x, 1.0 / 3.0);

    #endregion

    #region Bezier Evaluation

    static PointD EvalQuad(PathSegment seg, double t)
    {
        var u = 1 - t;
        return new(
            u * u * seg.X0 + 2 * u * t * seg.X1 + t * t * seg.X2,
            u * u * seg.Y0 + 2 * u * t * seg.Y1 + t * t * seg.Y2);
    }

    static PointD EvalCubic(PathSegment seg, double t)
    {
        var u = 1 - t;
        var u2 = u * u;
        var t2 = t * t;
        return new(
            u2 * u * seg.X0 + 3 * u2 * t * seg.X1 + 3 * u * t2 * seg.X2 + t2 * t * seg.X3,
            u2 * u * seg.Y0 + 3 * u2 * t * seg.Y1 + 3 * u * t2 * seg.Y2 + t2 * t * seg.Y3);
    }

    static PointD EvalSegment(PathSegment seg, double t) => seg.Kind switch
    {
        SegmentKind.Line => new(
            seg.X0 + t * (seg.X1 - seg.X0),
            seg.Y0 + t * (seg.Y1 - seg.Y0)),
        SegmentKind.QuadBezier => EvalQuad(seg, t),
        SegmentKind.CubicBezier => EvalCubic(seg, t),
        _ => new(seg.X0, seg.Y0)
    };

    #endregion

    #region Arc Length

    /// <summary>
    /// Compute arc length of a segment using 5-point Gauss-Legendre quadrature for Beziers.
    /// </summary>
    static double SegmentArcLength(PathSegment seg)
    {
        if (seg.Kind == SegmentKind.Line)
        {
            var dx = seg.X1 - seg.X0;
            var dy = seg.Y1 - seg.Y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // 5-point Gauss-Legendre on [0,1]
        ReadOnlySpan<double> nodes = [0.0469101, 0.2307653, 0.5, 0.7692347, 0.9530899];
        ReadOnlySpan<double> weights = [0.1184634, 0.2393143, 0.2844444, 0.2393143, 0.1184634];

        double length = 0;
        for (var i = 0; i < 5; i++)
        {
            var deriv = DerivativeAt(seg, nodes[i]);
            length += weights[i] * Math.Sqrt(deriv.X * deriv.X + deriv.Y * deriv.Y);
        }
        return length;
    }

    static PointD DerivativeAt(PathSegment seg, double t)
    {
        if (seg.Kind == SegmentKind.QuadBezier)
        {
            // B'(t) = 2(1-t)(P1-P0) + 2t(P2-P1)
            var u = 1 - t;
            return new(
                2 * u * (seg.X1 - seg.X0) + 2 * t * (seg.X2 - seg.X1),
                2 * u * (seg.Y1 - seg.Y0) + 2 * t * (seg.Y2 - seg.Y1));
        }

        // Cubic: B'(t) = 3(1-t)²(P1-P0) + 6(1-t)t(P2-P1) + 3t²(P3-P2)
        var u2 = (1 - t) * (1 - t);
        var ut = (1 - t) * t;
        var t2 = t * t;
        return new(
            3 * u2 * (seg.X1 - seg.X0) + 6 * ut * (seg.X2 - seg.X1) + 3 * t2 * (seg.X3 - seg.X2),
            3 * u2 * (seg.Y1 - seg.Y0) + 6 * ut * (seg.Y2 - seg.Y1) + 3 * t2 * (seg.Y3 - seg.Y2));
    }

    #endregion

    #region Tangent and Nearest Point

    static PointD TangentOnSegment(PathSegment seg, double t)
    {
        PointD d;
        if (seg.Kind == SegmentKind.Line)
        {
            d = new(seg.X1 - seg.X0, seg.Y1 - seg.Y0);
        }
        else
        {
            d = DerivativeAt(seg, t);
        }

        var len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        return len > Epsilon ? new(d.X / len, d.Y / len) : new(1, 0);
    }

    /// <summary>
    /// Find parameter t ∈ [0,1] on segment nearest to point, plus squared distance.
    /// Uses sampling + Newton refinement.
    /// </summary>
    static (double t, double dist) NearestParameterOnSegment(PathSegment seg, PointD point)
    {
        // Sample at 16 points
        var bestT = 0.0;
        var bestDist = double.MaxValue;
        const int samples = 16;

        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            var p = EvalSegment(seg, t);
            var d = (p.X - point.X) * (p.X - point.X) + (p.Y - point.Y) * (p.Y - point.Y);
            if (d < bestDist) { bestDist = d; bestT = t; }
        }

        // Newton refinement (2 iterations)
        for (var iter = 0; iter < 2; iter++)
        {
            var p = EvalSegment(seg, bestT);
            var d = DerivativeAt(seg, bestT);
            var dpx = p.X - point.X;
            var dpy = p.Y - point.Y;
            var num = dpx * d.X + dpy * d.Y;
            var den = d.X * d.X + d.Y * d.Y;
            if (Math.Abs(den) > Epsilon)
            {
                bestT = Math.Clamp(bestT - num / den, 0, 1);
                var pp = EvalSegment(seg, bestT);
                bestDist = (pp.X - point.X) * (pp.X - point.X) + (pp.Y - point.Y) * (pp.Y - point.Y);
            }
        }

        return (bestT, Math.Sqrt(bestDist));
    }

    #endregion

    #region Helpers

    static PointD BoundingBoxCenter(List<PathSegment> segments)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var seg in segments)
        {
            UpdateBounds(seg.X0, seg.Y0, ref minX, ref minY, ref maxX, ref maxY);
            UpdateBounds(seg.EndX, seg.EndY, ref minX, ref minY, ref maxX, ref maxY);
        }

        return new((minX + maxX) / 2, (minY + maxY) / 2);
    }

    static void UpdateBounds(double x, double y, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    /// <summary>
    /// Convert segments to a polygon approximation (linearize curves).
    /// </summary>
    static List<PointD> Linearize(List<PathSegment> segments, int curveSamples)
    {
        var pts = new List<PointD>();
        foreach (var seg in segments)
        {
            if (seg.Kind == SegmentKind.Line)
            {
                pts.Add(new(seg.X0, seg.Y0));
            }
            else
            {
                for (var i = 0; i < curveSamples; i++)
                {
                    var t = (double)i / curveSamples;
                    pts.Add(EvalSegment(seg, t));
                }
            }
        }
        return pts;
    }

    #endregion
}
