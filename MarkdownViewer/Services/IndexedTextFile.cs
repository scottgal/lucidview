using System.Runtime.CompilerServices;
using System.Text;

namespace MarkdownViewer.Services;

public readonly record struct TextPosition(long Line, long Column);
public readonly record struct TextMatch(long Line, long Column, int Length);
public readonly record struct FileTextLine(long Number, string Text, bool IsTruncated);
public readonly record struct MatchContext(string Text, long FirstColumn);
public readonly record struct TextFilePage(string Text, TextPosition Start, TextPosition End, TextPosition? Next);

/// <summary>A sparse byte-offset index. File contents and search results stay on disk.</summary>
public sealed class IndexedTextFile
{
    private const int IndexStride = 256;
    public const int MaxDisplayedLineLength = 64 * 1024;
    private readonly List<long> _checkpoints = [];
    private readonly SortedList<(long Line, long Column), (long Offset, bool SkipLf)> _textCheckpoints = [];
    private readonly object _checkpointLock = new();
    private readonly Encoding _encoding;
    private readonly string _path;
    private readonly long _fileLength;
    private readonly DateTime _lastWriteUtc;

    private IndexedTextFile(string path, Encoding encoding, long startOffset, FileInfo info)
    {
        _path = path;
        _encoding = encoding;
        _fileLength = info.Length;
        _lastWriteUtc = info.LastWriteTimeUtc;
        _checkpoints.Add(startOffset);
    }

    public long LineCount { get; private set; }
    public long Length => _fileLength;
    public long IndexBytes
    {
        get { lock (_checkpointLock) return (long)_checkpoints.Count * sizeof(long) + _textCheckpoints.Count * 32L; }
    }
    public string EncodingName => _encoding.WebName;

    public static async Task<IndexedTextFile> OpenAsync(string path, CancellationToken cancellationToken = default,
        IProgress<long>? progress = null)
    {
        var info = new FileInfo(path);
        await using var stream = OpenStream(path);
        var bom = new byte[4];
        var bomLength = await stream.ReadAtLeastAsync(bom, 4, throwOnEndOfStream: false, cancellationToken);
        var (encoding, startOffset) = DetectEncoding(bom.AsSpan(0, bomLength));
        stream.Position = startOffset;
        var document = new IndexedTextFile(path, encoding, startOffset, info);
        var buffer = new byte[128 * 1024];
        var offset = (long)startOffset;
        var lines = 1L;
        var unitSize = encoding.CodePage is 1200 or 1201 ? 2 : encoding.CodePage is 12000 or 12001 ? 4 : 1;
        var bigEndian = encoding.CodePage is 1201 or 12001;
        uint unit = 0;
        var unitBytes = 0;
        long pendingCr = -1;
        var nextProgress = 16L * 1024 * 1024;

        void NewLine(long nextOffset)
        {
            if (++lines % IndexStride == 1) document._checkpoints.Add(nextOffset);
        }

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            for (var i = 0; i < read; i++)
            {
                unit |= (uint)buffer[i] << (8 * (bigEndian ? unitSize - unitBytes - 1 : unitBytes));
                if (++unitBytes != unitSize) continue;
                var nextOffset = offset + i + 1;
                if (pendingCr >= 0)
                {
                    NewLine(unit == '\n' ? nextOffset : pendingCr);
                    pendingCr = -1;
                    if (unit == '\n') { unit = 0; unitBytes = 0; continue; }
                }
                if (unit == '\r') pendingCr = nextOffset;
                else if (unit == '\n') NewLine(nextOffset);
                unit = 0;
                unitBytes = 0;
            }
            offset += read;
            if (offset >= nextProgress)
            {
                progress?.Report(offset);
                nextProgress = offset + 16L * 1024 * 1024;
            }
        }
        if (pendingCr >= 0) NewLine(pendingCr);
        document.EnsureUnchanged();
        progress?.Report(offset);
        document.LineCount = lines;
        return document;
    }

    /// <summary>Enumerates bounded line previews while preserving physical line numbers.</summary>
    public async IAsyncEnumerable<FileTextLine> ReadLineRecordsAsync(long firstLine = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (firstLine < 0 || firstLine >= LineCount) yield break;
        EnsureUnchanged();
        var checkpoint = firstLine / IndexStride;
        var number = checkpoint * IndexStride;
        await using var stream = OpenStream(_path);
        stream.Position = _checkpoints[checked((int)checkpoint)];
        using var reader = CreateReader(stream);
        var text = new StringBuilder();
        var chars = new char[16 * 1024];
        var truncated = false;
        var skipLf = false;
        while (true)
        {
            var read = await reader.ReadAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            for (var i = 0; i < read; i++)
            {
                var c = chars[i];
                if (skipLf && c == '\n') { skipLf = false; continue; }
                skipLf = c == '\r';
                if (c is '\r' or '\n')
                {
                    if (number >= firstLine) yield return new FileTextLine(number, text.ToString(), truncated);
                    number++;
                    text.Clear();
                    truncated = false;
                }
                else if (number >= firstLine)
                {
                    if (text.Length < MaxDisplayedLineLength) text.Append(c);
                    else truncated = true;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        EnsureUnchanged();
        if (number >= firstLine) yield return new FileTextLine(number, text.ToString(), truncated);
    }

    public async Task<IReadOnlyList<string>> ReadLinesAsync(long firstLine, int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) return [];
        var result = new List<string>();
        await foreach (var line in ReadLineRecordsAsync(firstLine, cancellationToken).ConfigureAwait(false))
        {
            result.Add(line.Text);
            if (result.Count == count) break;
        }
        return result;
    }

    /// <summary>Reads a bounded source page, continuing inside a physical line when necessary.</summary>
    public async Task<TextFilePage> ReadTextPageAsync(TextPosition start, int maxLines = 120,
        int maxCharacters = MaxDisplayedLineLength, CancellationToken cancellationToken = default)
    {
        if (start.Line < 0 || start.Line >= LineCount || start.Column < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (maxLines < 1 || maxCharacters < 2) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        EnsureUnchanged();
        var checkpoint = start.Line / IndexStride;
        var cursor = new TextPosition(checkpoint * IndexStride, 0);
        var offset = _checkpoints[checked((int)checkpoint)];
        var skipLf = false;
        lock (_checkpointLock)
        {
            var low = 0;
            var high = _textCheckpoints.Count - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (_textCheckpoints.Keys[middle].CompareTo((start.Line, start.Column)) <= 0) low = middle + 1;
                else high = middle - 1;
            }
            if (high >= 0 && _textCheckpoints.Values[high].Offset > offset)
            {
                var key = _textCheckpoints.Keys[high];
                cursor = new TextPosition(key.Line, key.Column);
                (offset, skipLf) = _textCheckpoints.Values[high];
            }
        }
        await using var stream = OpenStream(_path);
        stream.Position = offset;
        var bytes = new byte[64 * 1024];
        var chars = new char[_encoding.GetMaxCharCount(bytes.Length)];
        var text = new StringBuilder();
        var pageStart = start;
        var pageEnd = start;
        var started = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_checkpointLock)
                _textCheckpoints.TryAdd((cursor.Line, cursor.Column), (stream.Position, skipLf));
            var read = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var count = stream.Position == Length ? read : CompleteEncodedPrefix(bytes, read);
            stream.Position -= read - count;
            var decoded = _encoding.GetChars(bytes, 0, count, chars, 0);
            for (var i = 0; i < decoded; i++)
            {
                var c = chars[i];
                if (skipLf && c == '\n') { skipLf = false; continue; }
                skipLf = c == '\r';
                var include = cursor.Line > start.Line || cursor.Line == start.Line
                    && (cursor.Column >= start.Column || char.IsHighSurrogate(c) && cursor.Column + 1 == start.Column);
                if (include)
                {
                    if (!started) { pageStart = cursor; started = true; }
                    pageEnd = cursor;
                    text.Append(c is '\r' or '\n' ? '\n' : c);
                }
                cursor = c is '\r' or '\n' ? new TextPosition(cursor.Line + 1, 0) : cursor with { Column = cursor.Column + 1 };
                // Never split a UTF-16 surrogate pair between displayed pages.
                if (include && !char.IsHighSurrogate(c)
                    && (text.Length >= maxCharacters || cursor.Line - pageStart.Line >= maxLines))
                {
                    EnsureUnchanged();
                    var length = text.Length - (text[^1] == '\n' ? 1 : 0);
                    var atEnd = i == decoded - 1 && stream.Position == Length && c is not '\r' and not '\n';
                    return new TextFilePage(text.ToString(0, length), pageStart, pageEnd, atEnd ? null : cursor);
                }
            }
        }
        EnsureUnchanged();
        // A request beyond the end of a line advances to the following line (or its actual EOF).
        if (!started) pageStart = pageEnd = cursor;
        return new TextFilePage(text.ToString(), pageStart, pageEnd, null);
    }

    private int CompleteEncodedPrefix(byte[] bytes, int count)
    {
        if (_encoding.CodePage is 12000 or 12001) return count - count % 4;
        if (_encoding.CodePage is 1200 or 1201)
        {
            count -= count % 2;
            var last = _encoding.CodePage == 1200
                ? bytes[count - 2] | bytes[count - 1] << 8 : bytes[count - 2] << 8 | bytes[count - 1];
            return char.IsHighSurrogate((char)last) ? count - 2 : count;
        }
        var lead = count - 1;
        while (lead > 0 && (bytes[lead] & 0xC0) == 0x80) lead--;
        var b = bytes[lead];
        var size = b is >= 0xC2 and <= 0xDF ? 2 : b is >= 0xE0 and <= 0xEF ? 3 : b is >= 0xF0 and <= 0xF4 ? 4 : 1;
        return count - lead < size ? lead : count;
    }

    public async Task<long?> FindNextAsync(string query, long startLine,
        CancellationToken cancellationToken = default) =>
        (await FindAsync(query, new TextPosition(startLine, 0), cancellationToken: cancellationToken))?.Line;

    /// <summary>Finds one literal match, including text beyond a line's display limit.</summary>
    public async Task<TextMatch?> FindAsync(string query, TextPosition start, bool backwards = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query) || query.Contains('\n') || query.Contains('\r')) return null;
        EnsureUnchanged();
        var startLine = Math.Clamp(start.Line, 0, LineCount);
        if (!backwards && startLine == LineCount) return null;
        var checkpoint = backwards ? 0 : startLine / IndexStride;
        var line = checkpoint * IndexStride;
        var pattern = query.Select(char.ToUpperInvariant).ToArray();
        var prefix = new int[pattern.Length];
        for (int i = 1, matched = 0; i < pattern.Length; i++)
        {
            while (matched > 0 && pattern[i] != pattern[matched]) matched = prefix[matched - 1];
            if (pattern[i] == pattern[matched]) matched++;
            prefix[i] = matched;
        }
        await using var stream = OpenStream(_path);
        stream.Position = _checkpoints[checked((int)checkpoint)];
        using var reader = CreateReader(stream);
        var chars = new char[16 * 1024];
        var found = 0;
        var column = 0L;
        var skipLf = false;
        TextMatch? previous = null;
        while (true)
        {
            var read = await reader.ReadAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            for (var i = 0; i < read; i++)
            {
                var c = chars[i];
                if (skipLf && c == '\n') { skipLf = false; continue; }
                skipLf = c == '\r';
                if (c is '\r' or '\n') { line++; column = 0; found = 0; continue; }
                if (backwards && (line > startLine || line == startLine && column >= start.Column))
                    return previous;
                c = char.ToUpperInvariant(c);
                while (found > 0 && c != pattern[found]) found = prefix[found - 1];
                if (c == pattern[found]) found++;
                if (found == pattern.Length)
                {
                    var matchColumn = column - pattern.Length + 1;
                    if (backwards) previous = new TextMatch(line, matchColumn, query.Length);
                    else if (line > startLine || line == startLine && matchColumn >= start.Column)
                        return new TextMatch(line, matchColumn, query.Length);
                    found = prefix[found - 1];
                }
                column++;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        EnsureUnchanged();
        return previous;
    }

    private StreamReader CreateReader(Stream stream) =>
        new(stream, _encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 32 * 1024, leaveOpen: true);

    public async Task<MatchContext> ReadMatchContextAsync(TextMatch match,
        CancellationToken cancellationToken = default)
    {
        var firstColumn = Math.Max(0, match.Column - 80);
        var page = await ReadTextPageAsync(new TextPosition(match.Line, firstColumn), maxLines: 1,
            maxCharacters: match.Length + 160, cancellationToken).ConfigureAwait(false);
        return new MatchContext(page.Text, page.Start.Column);
    }

    private static FileStream OpenStream(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private void EnsureUnchanged()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length != _fileLength || info.LastWriteTimeUtc != _lastWriteUtc)
            throw new IOException("The file changed since it was indexed. Reopen it to refresh the view.");
    }

    private static (Encoding Encoding, int StartOffset) DetectEncoding(ReadOnlySpan<byte> bom)
    {
        if (bom.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (Encoding.UTF32, 4);
        if (bom.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (new UTF32Encoding(true, true), 4);
        if (bom.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (new UTF8Encoding(false), 3);
        if (bom.StartsWith(new byte[] { 0xff, 0xfe })) return (Encoding.Unicode, 2);
        if (bom.StartsWith(new byte[] { 0xfe, 0xff })) return (Encoding.BigEndianUnicode, 2);
        return (new UTF8Encoding(false), 0);
    }
}
