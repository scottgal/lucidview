using System;
using System.Collections.Generic;

namespace MarkdownViewer.Lab.Services.Ingestion;

/// <summary>
/// Minimal segment chunker: splits text on blank-line paragraph boundaries and merges
/// adjacent paragraphs until the merged block exceeds <paramref name="targetTokens"/>
/// whitespace-delimited tokens, then emits a new segment.
///
/// <para>
/// Differences from real <c>Mostlylucid.LucidRAG.DocSummarizer.Core.SegmentSelector</c>:
/// no heading detection, no salience scoring (all segments get 0.5), and token
/// approximation is whitespace-split count rather than sub-word BPE tokens.
/// Replaced by <c>SegmentSelector</c> once that package publishes to nuget.org.
/// </para>
/// </summary>
public static class SimpleSegmentSelector
{
    /// <summary>
    /// Chunks <paramref name="text"/> into segments targeting at most
    /// <paramref name="targetTokens"/> whitespace-split tokens per chunk.
    /// </summary>
    public static IReadOnlyList<RawSegment> Chunk(string text, int targetTokens = 400)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<RawSegment>();

        // Split on blank lines to produce raw paragraph blocks.
        var lines = text.Split('\n');
        var blocks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                var block = current.ToString().Trim();
                if (block.Length > 0)
                    blocks.Add(block);
                current.Clear();
            }
            else
            {
                if (current.Length > 0)
                    current.Append('\n');
                current.Append(line);
            }
        }

        // Flush last block.
        var lastBlock = current.ToString().Trim();
        if (lastBlock.Length > 0)
            blocks.Add(lastBlock);

        if (blocks.Count == 0)
            return Array.Empty<RawSegment>();

        // Merge adjacent blocks while total token count stays below targetTokens.
        var segments = new List<RawSegment>();
        var mergeBuffer = new System.Text.StringBuilder();
        int mergeTokens = 0;
        int ordinal = 0;

        foreach (var block in blocks)
        {
            int blockTokens = ApproximateTokenCount(block);

            if (mergeTokens > 0 && mergeTokens + blockTokens >= targetTokens)
            {
                // Flush the accumulated buffer as a segment.
                segments.Add(new RawSegment(ordinal++, mergeBuffer.ToString().Trim()));
                mergeBuffer.Clear();
                mergeTokens = 0;
            }

            if (mergeBuffer.Length > 0)
                mergeBuffer.Append("\n\n");
            mergeBuffer.Append(block);
            mergeTokens += blockTokens;
        }

        // Flush any remaining content.
        if (mergeBuffer.Length > 0)
        {
            var remaining = mergeBuffer.ToString().Trim();
            if (remaining.Length > 0)
                segments.Add(new RawSegment(ordinal, remaining));
        }

        return segments;
    }

    /// <summary>
    /// Approximates token count as whitespace-delimited word count.
    /// Real tokenization uses sub-word BPE; this is intentionally coarse.
    /// </summary>
    private static int ApproximateTokenCount(string text) =>
        text.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Length;
}
