using SixLabors.ImageSharp;

namespace Mostlylucid.ImageSharp.Svg.Internal;

/// <summary>
/// Tiny SVG path command iterator that extracts the FIRST point + outgoing
/// tangent and the LAST point + incoming tangent from a <c>d</c> attribute.
/// Used by the marker renderer so <c>marker-start</c> and <c>marker-end</c>
/// arrowheads can be oriented to match the path direction.
/// </summary>
/// <remarks>
/// We don't need a full path renderer here — only the endpoint tangents.
/// For straight segments (M/L/H/V/Z) the tangent is the segment direction.
/// For cubic curves (C/S) the end tangent is the line from the last control
/// point to the endpoint, which is the standard SVG marker semantics. For
/// quadratic curves (Q/T) we use the same approach.
/// </remarks>
internal static class SvgPathEndpoints
{
    public readonly struct Endpoint
    {
        public PointF Point { get; }
        /// <summary>Tangent angle in radians (0 = +x axis).</summary>
        public float Angle { get; }
        public Endpoint(PointF point, float angle) { Point = point; Angle = angle; }
    }

    public static (Endpoint? Start, Endpoint? End) Compute(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return (null, null);

        var current = new PointF(0, 0);
        var subPathStart = new PointF(0, 0);
        Endpoint? start = null;
        Endpoint? end = null;
        var lastIncomingDir = new PointF(1, 0); // outgoing direction of last segment

        var tokenizer = new PathTokenizer(d);
        char prevCommand = ' ';

        while (tokenizer.NextCommand(out var cmd))
        {
            if (cmd == 0)
            {
                // Bare numbers continue the previous command (per SVG spec).
                cmd = prevCommand switch
                {
                    'M' => 'L',
                    'm' => 'l',
                    _ => prevCommand,
                };
            }

            switch (cmd)
            {
                case 'M' or 'm':
                {
                    var x = tokenizer.NextNumber();
                    var y = tokenizer.NextNumber();
                    var p = cmd == 'm'
                        ? new PointF(current.X + x, current.Y + y)
                        : new PointF(x, y);
                    current = p;
                    subPathStart = p;
                    if (start == null)
                    {
                        // We don't yet know the outgoing direction — record
                        // a placeholder. Will be overwritten by the next
                        // command's direction calculation.
                        start = new Endpoint(p, 0);
                    }
                    break;
                }

                case 'L' or 'l':
                {
                    var x = tokenizer.NextNumber();
                    var y = tokenizer.NextNumber();
                    var p = cmd == 'l'
                        ? new PointF(current.X + x, current.Y + y)
                        : new PointF(x, y);
                    UpdateEndpoints(ref start, ref end, current, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'H' or 'h':
                {
                    var x = tokenizer.NextNumber();
                    var p = cmd == 'h' ? new PointF(current.X + x, current.Y) : new PointF(x, current.Y);
                    UpdateEndpoints(ref start, ref end, current, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'V' or 'v':
                {
                    var y = tokenizer.NextNumber();
                    var p = cmd == 'v' ? new PointF(current.X, current.Y + y) : new PointF(current.X, y);
                    UpdateEndpoints(ref start, ref end, current, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'C' or 'c':
                {
                    // Cubic Bezier: (cx1, cy1) (cx2, cy2) (x, y)
                    var x1 = tokenizer.NextNumber();
                    var y1 = tokenizer.NextNumber();
                    var x2 = tokenizer.NextNumber();
                    var y2 = tokenizer.NextNumber();
                    var x  = tokenizer.NextNumber();
                    var y  = tokenizer.NextNumber();
                    var c1 = cmd == 'c' ? new PointF(current.X + x1, current.Y + y1) : new PointF(x1, y1);
                    var c2 = cmd == 'c' ? new PointF(current.X + x2, current.Y + y2) : new PointF(x2, y2);
                    var p  = cmd == 'c' ? new PointF(current.X + x,  current.Y + y)  : new PointF(x, y);
                    // Outgoing tangent at start = c1 - current; incoming tangent at end = p - c2.
                    UpdateEndpointsCurve(ref start, ref end, current, c1, c2, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'S' or 's':
                {
                    var x2 = tokenizer.NextNumber();
                    var y2 = tokenizer.NextNumber();
                    var x  = tokenizer.NextNumber();
                    var y  = tokenizer.NextNumber();
                    var c2 = cmd == 's' ? new PointF(current.X + x2, current.Y + y2) : new PointF(x2, y2);
                    var p  = cmd == 's' ? new PointF(current.X + x,  current.Y + y)  : new PointF(x, y);
                    UpdateEndpointsCurve(ref start, ref end, current, current, c2, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'Q' or 'q':
                {
                    var x1 = tokenizer.NextNumber();
                    var y1 = tokenizer.NextNumber();
                    var x  = tokenizer.NextNumber();
                    var y  = tokenizer.NextNumber();
                    var c1 = cmd == 'q' ? new PointF(current.X + x1, current.Y + y1) : new PointF(x1, y1);
                    var p  = cmd == 'q' ? new PointF(current.X + x,  current.Y + y)  : new PointF(x, y);
                    UpdateEndpointsCurve(ref start, ref end, current, c1, c1, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'T' or 't':
                {
                    var x = tokenizer.NextNumber();
                    var y = tokenizer.NextNumber();
                    var p = cmd == 't' ? new PointF(current.X + x, current.Y + y) : new PointF(x, y);
                    UpdateEndpoints(ref start, ref end, current, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'A' or 'a':
                {
                    // Arc: rx ry x-axis-rotation large-arc-flag sweep-flag x y
                    tokenizer.NextNumber(); tokenizer.NextNumber(); tokenizer.NextNumber();
                    tokenizer.NextNumber(); tokenizer.NextNumber();
                    var x = tokenizer.NextNumber();
                    var y = tokenizer.NextNumber();
                    var p = cmd == 'a' ? new PointF(current.X + x, current.Y + y) : new PointF(x, y);
                    // Approximate tangent as the chord direction. Good enough
                    // for marker orientation on small arcs.
                    UpdateEndpoints(ref start, ref end, current, p, ref lastIncomingDir);
                    current = p;
                    break;
                }

                case 'Z' or 'z':
                    UpdateEndpoints(ref start, ref end, current, subPathStart, ref lastIncomingDir);
                    current = subPathStart;
                    break;
            }

            prevCommand = cmd;
        }

        return (start, end);
    }

    private static void UpdateEndpoints(
        ref Endpoint? start, ref Endpoint? end,
        PointF from, PointF to, ref PointF lastDir)
    {
        var dir = new PointF(to.X - from.X, to.Y - from.Y);
        var angle = MathF.Atan2(dir.Y, dir.X);
        if (start.HasValue && start.Value.Angle == 0 && start.Value.Point == from)
        {
            // Replace placeholder with the real outgoing direction.
            start = new Endpoint(from, angle);
        }
        end = new Endpoint(to, angle);
        if (dir.X != 0 || dir.Y != 0)
            lastDir = dir;
    }

    private static void UpdateEndpointsCurve(
        ref Endpoint? start, ref Endpoint? end,
        PointF from, PointF c1, PointF c2, PointF to,
        ref PointF lastDir)
    {
        // Outgoing tangent at start = direction (from → c1)
        // Incoming tangent at end   = direction (c2 → to)
        var startDir = new PointF(c1.X - from.X, c1.Y - from.Y);
        if (startDir.X == 0 && startDir.Y == 0)
            startDir = new PointF(to.X - from.X, to.Y - from.Y);
        var endDir   = new PointF(to.X - c2.X, to.Y - c2.Y);
        if (endDir.X == 0 && endDir.Y == 0)
            endDir = new PointF(to.X - from.X, to.Y - from.Y);

        if (start.HasValue && start.Value.Angle == 0 && start.Value.Point == from)
            start = new Endpoint(from, MathF.Atan2(startDir.Y, startDir.X));

        end = new Endpoint(to, MathF.Atan2(endDir.Y, endDir.X));
        if (endDir.X != 0 || endDir.Y != 0)
            lastDir = endDir;
    }

    private struct PathTokenizer
    {
        private readonly string _d;
        private int _i;

        public PathTokenizer(string d) { _d = d; _i = 0; }

        /// <summary>
        /// Returns the next path command letter, or '\0' if the next non-
        /// whitespace token is a number (i.e. a continuation of the previous
        /// command). Returns false at end of string.
        /// </summary>
        public bool NextCommand(out char cmd)
        {
            cmd = ' ';
            SkipNoise();
            if (_i >= _d.Length) return false;

            var c = _d[_i];
            if (IsCommandLetter(c))
            {
                cmd = c;
                _i++;
                return true;
            }
            // Number → continuation.
            cmd = '\0';
            return true;
        }

        public float NextNumber()
        {
            SkipNoise();
            if (_i >= _d.Length) return 0;

            var start = _i;
            if (_d[_i] == '+' || _d[_i] == '-') _i++;
            while (_i < _d.Length && (char.IsDigit(_d[_i]) || _d[_i] == '.'))
                _i++;
            if (_i < _d.Length && (_d[_i] == 'e' || _d[_i] == 'E'))
            {
                _i++;
                if (_i < _d.Length && (_d[_i] == '+' || _d[_i] == '-')) _i++;
                while (_i < _d.Length && char.IsDigit(_d[_i])) _i++;
            }

            if (_i == start) return 0;
            return float.TryParse(_d.AsSpan(start, _i - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n : 0;
        }

        private void SkipNoise()
        {
            while (_i < _d.Length && (_d[_i] == ' ' || _d[_i] == ',' ||
                   _d[_i] == '\t' || _d[_i] == '\n' || _d[_i] == '\r'))
                _i++;
        }

        private static bool IsCommandLetter(char c) =>
            c is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v'
              or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't'
              or 'A' or 'a' or 'Z' or 'z';
    }
}
