using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace MarkdownViewer.Lab.Services.Storage;

/// <summary>
/// Persistent full-text lexical index backed by Lucene.Net 4.8 (BM25 scoring).
/// Uses StandardAnalyzer and a Classic QueryParser for field "body".
/// Each segment is stored as a document with fields:
///   segment_id  — BIGINT as string, stored
///   content_hash — hex string, stored
///   body        — text field, analysed, not stored
/// </summary>
public sealed class LexicalIndex : IAsyncDisposable
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    private readonly FSDirectory _dir;
    private readonly StandardAnalyzer _analyzer;
    private readonly IndexWriter _writer;

    private LexicalIndex(FSDirectory dir, StandardAnalyzer analyzer, IndexWriter writer)
    {
        _dir     = dir;
        _analyzer = analyzer;
        _writer  = writer;
    }

    public static Task<LexicalIndex> OpenAsync(string indexDir, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(indexDir);
        var dir      = FSDirectory.Open(indexDir);
        var analyzer = new StandardAnalyzer(Version);
        var cfg      = new IndexWriterConfig(Version, analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
        };
        var writer = new IndexWriter(dir, cfg);
        return Task.FromResult(new LexicalIndex(dir, analyzer, writer));
    }

    public Task IndexAsync(
        long segmentId,
        ContentHash hash,
        string text,
        CancellationToken ct)
    {
        var doc = new Document
        {
            new StringField("segment_id",   segmentId.ToString(), Field.Store.YES),
            new StringField("content_hash", hash.ToHex(),         Field.Store.YES),
            new TextField("body",           text,                 Field.Store.NO),
        };
        // UpdateDocument replaces any existing document with the same segment_id term.
        _writer.UpdateDocument(new Term("segment_id", segmentId.ToString()), doc);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct)
    {
        _writer.Commit();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<(long SegmentId, ContentHash Hash, float Score)>>
        SearchAsync(string query, int k, CancellationToken ct)
    {
        // Commit pending changes so the reader sees them.
        _writer.Commit();

        using var reader   = DirectoryReader.Open(_dir);
        var       searcher = new IndexSearcher(reader);
        var       parser   = new QueryParser(Version, "body", _analyzer);

        // QueryParser.Escape prevents special characters from being misinterpreted.
        var parsed = parser.Parse(QueryParser.Escape(query));
        var hits   = searcher.Search(parsed, k);

        var results = new List<(long, ContentHash, float)>(hits.ScoreDocs.Length);
        foreach (var hit in hits.ScoreDocs)
        {
            var doc   = searcher.Doc(hit.Doc);
            var segId = long.Parse(doc.Get("segment_id"));
            ContentHash.TryParseHex(doc.Get("content_hash"), out var hash);
            results.Add((segId, hash, hit.Score));
        }
        await Task.CompletedTask;
        return results;
    }

    public ValueTask DisposeAsync()
    {
        _writer.Dispose();
        _analyzer.Dispose();
        _dir.Dispose();
        return ValueTask.CompletedTask;
    }
}
