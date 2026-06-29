using System;
using System.IO.Hashing;
using System.Text;

namespace MarkdownViewer.Lab.Services.Storage;

public readonly record struct ContentHash(ulong Value)
{
    public static ContentHash Compute(ReadOnlySpan<byte> bytes)
        => new(XxHash64.HashToUInt64(bytes));

    public static ContentHash Compute(string s)
        => Compute(Encoding.UTF8.GetBytes(s));

    public string ToHex() => Value.ToString("x16");

    public static bool TryParseHex(string s, out ContentHash hash)
    {
        if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
        {
            hash = new ContentHash(v);
            return true;
        }
        hash = default;
        return false;
    }
}
