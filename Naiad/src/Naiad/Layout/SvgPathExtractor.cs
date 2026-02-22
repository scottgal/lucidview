using System.Globalization;

namespace MermaidSharp.Layout;

/// <summary>
/// Parses SVG path <c>d</c> attribute strings into <see cref="PathSegment"/> lists.
/// Handles M/L/H/V/Q/C/A/Z commands in both absolute and relative forms.
/// Arcs are approximated as cubic Bezier segments.
/// </summary>
internal static class SvgPathExtractor
{
    public static List<PathSegment> ExtractSegments(string svgPathData)
    {
        var segments = new List<PathSegment>();
        if (string.IsNullOrWhiteSpace(svgPathData))
            return segments;

        var tokens = Tokenize(svgPathData);
        if (tokens.Count == 0)
            return segments;

        double cx = 0, cy = 0;       // current point
        double sx = 0, sy = 0;       // subpath start (for Z)
        var i = 0;

        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.IsCommand)
            {
                var cmd = token.Command;
                var abs = char.IsUpper(cmd);
                cmd = char.ToUpperInvariant(cmd);
                i++;

                switch (cmd)
                {
                    case 'M':
                    {
                        var x = NextNum(tokens, ref i);
                        var y = NextNum(tokens, ref i);
                        if (!abs) { x += cx; y += cy; }
                        cx = x; cy = y;
                        sx = cx; sy = cy;
                        // Implicit lineto after first M coordinate pair
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var lx = NextNum(tokens, ref i);
                            var ly = NextNum(tokens, ref i);
                            if (!abs) { lx += cx; ly += cy; }
                            segments.Add(PathSegment.Line(cx, cy, lx, ly));
                            cx = lx; cy = ly;
                        }
                        break;
                    }
                    case 'L':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var x = NextNum(tokens, ref i);
                            var y = NextNum(tokens, ref i);
                            if (!abs) { x += cx; y += cy; }
                            segments.Add(PathSegment.Line(cx, cy, x, y));
                            cx = x; cy = y;
                        }
                        break;
                    }
                    case 'H':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var x = NextNum(tokens, ref i);
                            if (!abs) x += cx;
                            segments.Add(PathSegment.Line(cx, cy, x, cy));
                            cx = x;
                        }
                        break;
                    }
                    case 'V':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var y = NextNum(tokens, ref i);
                            if (!abs) y += cy;
                            segments.Add(PathSegment.Line(cx, cy, cx, y));
                            cy = y;
                        }
                        break;
                    }
                    case 'Q':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var cpx = NextNum(tokens, ref i);
                            var cpy = NextNum(tokens, ref i);
                            var x = NextNum(tokens, ref i);
                            var y = NextNum(tokens, ref i);
                            if (!abs) { cpx += cx; cpy += cy; x += cx; y += cy; }
                            segments.Add(PathSegment.Quad(cx, cy, cpx, cpy, x, y));
                            cx = x; cy = y;
                        }
                        break;
                    }
                    case 'C':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var cp1x = NextNum(tokens, ref i);
                            var cp1y = NextNum(tokens, ref i);
                            var cp2x = NextNum(tokens, ref i);
                            var cp2y = NextNum(tokens, ref i);
                            var x = NextNum(tokens, ref i);
                            var y = NextNum(tokens, ref i);
                            if (!abs) { cp1x += cx; cp1y += cy; cp2x += cx; cp2y += cy; x += cx; y += cy; }
                            segments.Add(PathSegment.Cubic(cx, cy, cp1x, cp1y, cp2x, cp2y, x, y));
                            cx = x; cy = y;
                        }
                        break;
                    }
                    case 'A':
                    {
                        while (i < tokens.Count && !tokens[i].IsCommand)
                        {
                            var rx = NextNum(tokens, ref i);
                            var ry = NextNum(tokens, ref i);
                            var xRot = NextNum(tokens, ref i);
                            var largeArc = (int)NextNum(tokens, ref i);
                            var sweep = (int)NextNum(tokens, ref i);
                            var x = NextNum(tokens, ref i);
                            var y = NextNum(tokens, ref i);
                            if (!abs) { x += cx; y += cy; }
                            ArcToCubics(segments, cx, cy, rx, ry, xRot, largeArc != 0, sweep != 0, x, y);
                            cx = x; cy = y;
                        }
                        break;
                    }
                    case 'Z':
                    {
                        if (Math.Abs(cx - sx) > 0.001 || Math.Abs(cy - sy) > 0.001)
                            segments.Add(PathSegment.Line(cx, cy, sx, sy));
                        cx = sx; cy = sy;
                        break;
                    }
                }
            }
            else
            {
                // Stray number — skip
                i++;
            }
        }

        return segments;
    }

    /// <summary>
    /// Extract only the first closed sub-path (outer boundary) from an SVG path.
    /// For shapes with holes (donut), this returns just the outer contour.
    /// </summary>
    public static List<PathSegment> ExtractOuterContour(string svgPathData)
    {
        var all = ExtractSegments(svgPathData);
        if (all.Count == 0) return all;

        // Find the first Z (close) in the segment list by tracking subpath starts
        // The first contiguous group of segments from start forms the outer contour
        var result = new List<PathSegment>();
        double sx = all[0].X0, sy = all[0].Y0;

        foreach (var seg in all)
        {
            result.Add(seg);
            // Check if this segment closes back to subpath start
            if (Math.Abs(seg.EndX - sx) < 0.01 && Math.Abs(seg.EndY - sy) < 0.01)
                break;
        }

        return result;
    }

    #region Tokenizer

    readonly record struct Token(bool IsCommand, char Command, double Value)
    {
        public static Token Cmd(char c) => new(true, c, 0);
        public static Token Num(double v) => new(false, '\0', v);
    }

    static List<Token> Tokenize(string d)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < d.Length)
        {
            var c = d[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (IsCommandChar(c))
            {
                tokens.Add(Token.Cmd(c));
                i++;
                continue;
            }

            if (c is '-' or '+' or '.' || char.IsDigit(c))
            {
                var start = i;
                if (c is '-' or '+') i++;
                var hasDot = false;
                while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !hasDot)))
                {
                    if (d[i] == '.') hasDot = true;
                    i++;
                }
                // Handle scientific notation
                if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
                {
                    i++;
                    if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                    while (i < d.Length && char.IsDigit(d[i])) i++;
                }

                var span = d.AsSpan(start, i - start);
                if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                    tokens.Add(Token.Num(val));
                continue;
            }

            i++; // skip unknown chars
        }
        return tokens;
    }

    static bool IsCommandChar(char c) => c is
        'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or
        'Q' or 'q' or 'C' or 'c' or 'A' or 'a' or 'Z' or 'z' or
        'S' or 's' or 'T' or 't';

    static double NextNum(List<Token> tokens, ref int i)
    {
        while (i < tokens.Count && tokens[i].IsCommand) i++;
        if (i >= tokens.Count) return 0;
        var val = tokens[i].Value;
        i++;
        return val;
    }

    #endregion

    #region Arc to Cubic Bezier

    /// <summary>
    /// Convert an SVG arc to one or more cubic Bezier segments.
    /// Based on the W3C SVG implementation notes for arc parameterization.
    /// </summary>
    static void ArcToCubics(List<PathSegment> segments,
        double x1, double y1, double rx, double ry,
        double xRotDeg, bool largeArc, bool sweep,
        double x2, double y2)
    {
        if (Math.Abs(x1 - x2) < 0.001 && Math.Abs(y1 - y2) < 0.001)
            return;

        if (rx < 0.001 || ry < 0.001)
        {
            segments.Add(PathSegment.Line(x1, y1, x2, y2));
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);

        var phi = xRotDeg * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        // Step 1: Compute (x1', y1')
        var dx2 = (x1 - x2) / 2.0;
        var dy2 = (y1 - y2) / 2.0;
        var x1p = cosPhi * dx2 + sinPhi * dy2;
        var y1p = -sinPhi * dx2 + cosPhi * dy2;

        // Step 2: Compute (cx', cy')
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;

        // Ensure radii are large enough
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var lambdaSqrt = Math.Sqrt(lambda);
            rx *= lambdaSqrt;
            ry *= lambdaSqrt;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        var num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        var den = rxSq * y1pSq + rySq * x1pSq;
        var sq = Math.Max(0, num / den);
        var root = Math.Sqrt(sq) * (largeArc == sweep ? -1 : 1);

        var cxp = root * rx * y1p / ry;
        var cyp = -root * ry * x1p / rx;

        // Step 3: Compute (cx, cy) from (cx', cy')
        var cxr = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
        var cyr = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

        // Step 4: Compute theta1 and dtheta
        var theta1 = VectorAngle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var dtheta = VectorAngle(
            (x1p - cxp) / rx, (y1p - cyp) / ry,
            (-x1p - cxp) / rx, (-y1p - cyp) / ry);

        if (!sweep && dtheta > 0) dtheta -= 2 * Math.PI;
        else if (sweep && dtheta < 0) dtheta += 2 * Math.PI;

        // Split into 90-degree segments
        var numSegs = (int)Math.Ceiling(Math.Abs(dtheta) / (Math.PI / 2));
        var segAngle = dtheta / numSegs;

        var curX = x1;
        var curY = y1;

        for (var s = 0; s < numSegs; s++)
        {
            var t1 = theta1 + s * segAngle;
            var t2 = t1 + segAngle;

            var alpha = 4.0 / 3.0 * Math.Tan(segAngle / 4.0);

            var cos1 = Math.Cos(t1);
            var sin1 = Math.Sin(t1);
            var cos2 = Math.Cos(t2);
            var sin2 = Math.Sin(t2);

            var ep1x = rx * cos1;
            var ep1y = ry * sin1;
            var ep2x = rx * cos2;
            var ep2y = ry * sin2;

            var cp1x = ep1x - alpha * rx * sin1;
            var cp1y = ep1y + alpha * ry * cos1;
            var cp2x = ep2x + alpha * rx * sin2;
            var cp2y = ep2y - alpha * ry * cos2;

            // Transform back from unit circle space
            var bcp1x = cosPhi * cp1x - sinPhi * cp1y + cxr;
            var bcp1y = sinPhi * cp1x + cosPhi * cp1y + cyr;
            var bcp2x = cosPhi * cp2x - sinPhi * cp2y + cxr;
            var bcp2y = sinPhi * cp2x + cosPhi * cp2y + cyr;
            var bepx = cosPhi * ep2x - sinPhi * ep2y + cxr;
            var bepy = sinPhi * ep2x + cosPhi * ep2y + cyr;

            segments.Add(PathSegment.Cubic(curX, curY, bcp1x, bcp1y, bcp2x, bcp2y, bepx, bepy));
            curX = bepx;
            curY = bepy;
        }
    }

    static double VectorAngle(double ux, double uy, double vx, double vy)
    {
        var sign = (ux * vy - uy * vx) < 0 ? -1.0 : 1.0;
        var dot = ux * vx + uy * vy;
        var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        var cos = Math.Clamp(dot / Math.Max(len, 1e-10), -1.0, 1.0);
        return sign * Math.Acos(cos);
    }

    #endregion
}
