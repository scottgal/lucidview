using System.Text;
using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

public sealed class IndexedTextFileTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task ContinuationPagesPreserveEveryCharacterOfALongUnicodeLine(string encodingName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-continuation-{Guid.NewGuid():N}.txt");
        try
        {
            Encoding encoding = encodingName switch
            {
                "utf16le" => new UnicodeEncoding(false, true),
                "utf16be" => new UnicodeEncoding(true, true),
                "utf32le" => new UTF32Encoding(false, true),
                "utf32be" => new UTF32Encoding(true, true),
                _ => new UTF8Encoding(true)
            };
            var original = string.Concat(Enumerable.Repeat("xé😀漢", 50_000)) + "END";
            await File.WriteAllTextAsync(path, original, encoding);
            var file = await IndexedTextFile.OpenAsync(path);
            TextPosition? next = new(0, 0);
            var reconstructed = new StringBuilder();
            while (next is { } start)
            {
                var page = await file.ReadTextPageAsync(start, maxCharacters: 4097);
                Assert.InRange(page.Text.Length, 1, 4098);
                Assert.False(char.IsLowSurrogate(page.Text[0]));
                Assert.False(char.IsHighSurrogate(page.Text[^1]));
                reconstructed.Append(page.Text);
                next = page.Next;
            }
            Assert.Equal(original, reconstructed.ToString());
            var tail = await file.ReadTextPageAsync(new TextPosition(0, original.Length - 3));
            Assert.Equal("END", tail.Text);
            Assert.Null(tail.Next);
            Assert.InRange(file.IndexBytes, 8, 1000); // One seek record per byte chunk, not per displayed page.
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ContinuationAcrossCrLfByteBoundaryPreservesPhysicalPositions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-continuation-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, new string('a', 65535) + "\r\nsecond\rthird\nlast");
            var file = await IndexedTextFile.OpenAsync(path);
            var first = await file.ReadTextPageAsync(new TextPosition(0, 0), maxLines: 1);
            Assert.Equal(new string('a', 65535), first.Text);
            Assert.Equal(new TextPosition(1, 0), first.Next);
            var rest = await file.ReadTextPageAsync(first.Next!.Value);
            Assert.Equal("second\nthird\nlast", rest.Text);
            Assert.Equal(new TextPosition(1, 0), rest.Start);
            Assert.Equal(new TextPosition(3, 3), rest.End);
            var withinSurrogate = await file.ReadTextPageAsync(new TextPosition(1, 2), maxCharacters: 2);
            Assert.Equal("co", withinSurrogate.Text);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task ReadsAcrossSparseCheckpointsAndFindsLateText(string encodingName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-index-{Guid.NewGuid():N}.txt");
        try
        {
            Encoding encoding = encodingName switch
            {
                "utf16le" => new UnicodeEncoding(false, true),
                "utf16be" => new UnicodeEncoding(true, true),
                "utf32le" => new UTF32Encoding(false, true),
                "utf32be" => new UTF32Encoding(true, true),
                _ => new UTF8Encoding(true)
            };
            var lines = Enumerable.Range(0, 700)
                .Select(i => i == 513 ? $"line {i} café TARGET" : $"line {i} café")
                .ToArray();
            await File.WriteAllTextAsync(path, string.Join('\n', lines), encoding);

            var indexed = await IndexedTextFile.OpenAsync(path);
            Assert.Equal(700, indexed.LineCount);
            Assert.Equal(lines.Skip(250).Take(20), await indexed.ReadLinesAsync(250, 20));
            Assert.Equal(lines.Skip(510).Take(8), await indexed.ReadLinesAsync(510, 8));
            Assert.Equal(513, await indexed.FindNextAsync("target", 500));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DetectsFileChangesAfterIndexing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-index-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, "first\nsecond");
            var indexed = await IndexedTextFile.OpenAsync(path);
            await File.AppendAllTextAsync(path, "\nthird");
            await Assert.ThrowsAsync<IOException>(() => indexed.ReadLinesAsync(0, 3));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SearchScansBeyondDisplayedPartOfVeryLongLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-index-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, "first\n" + new string('x', 100_000) + "target\nlast");
            var indexed = await IndexedTextFile.OpenAsync(path);
            Assert.Equal(3, indexed.LineCount);
            Assert.Equal(1, await indexed.FindNextAsync("target", 0));
            Assert.Null(await indexed.FindNextAsync("target", 2));
            Assert.Equal(64 * 1024, (await indexed.ReadLinesAsync(1, 1))[0].Length);
            var match = await indexed.FindAsync("target", new TextPosition(0, 0));
            Assert.Equal(new TextMatch(1, 100_000, 6), match);
            var context = await indexed.ReadMatchContextAsync(match!.Value);
            Assert.Contains("target", context.Text);
            Assert.InRange(context.Text.Length, 6, 166);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IndexesAndReadsAFileAboveLargeFileThreshold()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-index-{Guid.NewGuid():N}.md");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                for (var i = 0; i < 250_000; i++)
                    await writer.WriteLineAsync($"Line {i:D6}: {new string('a', 72)}");

            Assert.True(new FileInfo(path).Length > 16L * 1024 * 1024);
            var indexed = await IndexedTextFile.OpenAsync(path);
            Assert.Equal(250_001, indexed.LineCount); // Final newline leaves one empty line.
            Assert.StartsWith("Line 249990:", (await indexed.ReadLinesAsync(249_990, 1))[0]);
            Assert.Equal(249_999, await indexed.FindNextAsync("Line 249999:", 249_000));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task ReadsAndSearchesEverySupportedLineEnding(string newline)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-newlines-{Guid.NewGuid():N}.txt");
        try
        {
            var lines = Enumerable.Range(0, 700).Select(i => $"row {i}").ToArray();
            await File.WriteAllTextAsync(path, string.Join(newline, lines));
            var file = await IndexedTextFile.OpenAsync(path);
            Assert.Equal(700, file.LineCount);
            Assert.Equal(lines.Skip(511).Take(4), await file.ReadLinesAsync(511, 4));
            Assert.Equal(new TextMatch(513, 0, 7), await file.FindAsync("row 513", new TextPosition(500, 0)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NavigatesMultipleMatchesOnTheSameLineInBothDirections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucidview-search-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, "aba ABA aba\nelsewhere");
            var file = await IndexedTextFile.OpenAsync(path);
            Assert.Equal(new TextMatch(0, 4, 3), await file.FindAsync("aba", new TextPosition(0, 1)));
            Assert.Equal(new TextMatch(0, 8, 3), await file.FindAsync("aba", new TextPosition(0, 5)));
            Assert.Equal(new TextMatch(0, 4, 3), await file.FindAsync("aba", new TextPosition(0, 8), backwards: true));
            Assert.Equal(new TextMatch(0, 8, 3), await file.FindAsync("aba", new TextPosition(file.LineCount, 0), backwards: true));
        }
        finally { File.Delete(path); }
    }
}
