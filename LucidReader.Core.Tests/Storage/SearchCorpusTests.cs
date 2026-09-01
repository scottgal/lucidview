using System.Diagnostics;
using System.Text;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;
using Xunit.Abstractions;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// Search quality measured against a corpus the size of a real profile
/// rather than against three fixture rows, because most of what makes search
/// good or bad only appears at scale: ranking is meaningless when everything
/// matches, and a prefix query that is instant over five items is a
/// different question over four thousand.
///
/// The corpus is generated, seeded, and thrown away: 4000 articles across 20
/// feeds of ordinary-length prose, one article in five with no downloaded
/// body at all (the auto-download-off, failed-fetch, offline_state 0 case),
/// plus three planted articles that hold the search term in a title, in a
/// summary and buried at the end of a long body respectively.
///
/// Every assertion below is a property the item list depends on. The timings
/// are printed rather than asserted tightly, since a machine-dependent number
/// makes a brittle test; the assertion is only that a query is nowhere near
/// the point where typing would stutter.
/// </summary>
public class SearchCorpusTests(ITestOutputHelper output)
{
    private const int Feeds = 20;
    private const int Items = 4000;

    /// <summary>
    /// Wall-clock budget for a single query.
    ///
    /// 250ms on a developer's machine, which is the search debounce: a query
    /// slower than that is visible as a stutter while typing, and that is the
    /// thing worth defending.
    ///
    /// Four times that on CI, and not because the guarantee is weaker there.
    /// A hosted runner is a contended VM whose wall clock reports what else is
    /// running on the host as much as it reports this code. The release of
    /// mylo 0.2.5 was blocked by a median of 269.4 ms on a run where the same
    /// corpus answered prefix queries in 1.1 ms, and the identical commit
    /// measured well inside budget locally and passed on an immediate re-run
    /// with nothing changed.
    ///
    /// The alternative to this is a test that fails a release at random, and
    /// the usual end of such a test is that somebody deletes it, after which
    /// nothing watches search performance at all. A looser bound that always
    /// runs beats a tight one that gets removed. What the looser bound still
    /// catches is the regression that actually matters: a lost index or an
    /// accidental table scan over four thousand articles costs seconds, not
    /// tens of milliseconds, so it fails this just as surely.
    ///
    /// The measured median is printed either way, so a real slowdown is
    /// visible in the log before it is large enough to fail anything.
    /// </summary>
    private static double QueryBudgetMs =>
        Environment.GetEnvironmentVariable("CI") == "true" ? 1000 : 250;

    private static readonly string[] Vocabulary =
    [
        "compositor", "rendering", "pipeline", "database", "writer", "lock", "journal",
        "kingfisher", "estuary", "harbour", "weeknotes", "release", "migration", "schema",
        "index", "tokenizer", "snippet", "ranking", "relevance", "keyboard", "shortcut",
        "sidebar", "article", "publisher", "subscription", "retention", "tombstone",
        "download", "extraction", "markdown", "typography", "column", "measure", "layout",
        "thread", "dispatcher", "cancellation", "debounce", "selection", "guard", "sequence",
        "morning", "afternoon", "winter", "harvest", "orchard", "railway", "printing",
        "letterpress", "correspondence", "archive", "catalogue", "footnote", "margin"
    ];

    private static string Sentence(Random random, int words)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < words; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(Vocabulary[random.Next(Vocabulary.Length)]);
        }

        builder.Append('.');
        return builder.ToString();
    }

    private static string Paragraphs(Random random, int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) builder.Append("\n\n");
            for (var s = 0; s < 6; s++)
            {
                if (s > 0) builder.Append(' ');
                builder.Append(Sentence(random, random.Next(8, 20)));
            }
        }

        return builder.ToString();
    }

    [Fact]
    public async Task Search_holds_up_over_a_realistic_corpus()
    {
        var dir = Path.Combine("/tmp", "mylo-search-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "reader.db");

        try
        {
            var build = Stopwatch.StartNew();
            await using var db = await ReaderDatabase.OpenAsync(path);
            var feedRepo = new FeedRepository(db);
            var items = new ItemRepository(db);
            var search = new SearchRepository(db);

            var feedIds = new List<long>();
            for (var f = 0; f < Feeds; f++)
                feedIds.Add(await feedRepo.AddAsync(new Feed
                {
                    FeedUrl = $"https://feed{f}.test/feed.xml",
                    Title = $"Feed {f}"
                }));

            var random = new Random(20260830);
            var seen = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

            for (var batchStart = 0; batchStart < Items; batchStart += 200)
            {
                var batchFeed = feedIds[(batchStart / 200) % feedIds.Count];
                var batch = new List<FeedItem>();
                for (var i = batchStart; i < batchStart + 200 && i < Items; i++)
                {
                    // Every fifth item has no downloaded body at all, which is
                    // the real distribution: auto-download off, a failed fetch,
                    // or an item stored at offline_state 0.
                    var summaryOnly = i % 5 == 0;
                    batch.Add(new FeedItem
                    {
                        FeedId = batchFeed,
                        Guid = $"item-{i}",
                        Title = Sentence(random, random.Next(4, 10)).TrimEnd('.'),
                        Author = $"Author {i % 47}",
                        Summary = Sentence(random, random.Next(12, 30)),
                        ContentMarkdown = summaryOnly ? null : Paragraphs(random, random.Next(4, 12)),
                        ContentSource = summaryOnly ? ContentSource.Feed : ContentSource.Extracted,
                        FirstSeenUtc = seen.AddMinutes(i),
                        PublishedUtc = seen.AddMinutes(i)
                    });
                }

                await items.UpsertManyAsync(batch);
            }

            // Three planted needles, none of which share a word with the
            // generated vocabulary, so each measurement below is unambiguous.
            var needleFeed = feedIds[0];
            await items.UpsertAsync(new FeedItem
            {
                FeedId = needleFeed,
                Guid = "needle-title",
                Title = "Bittern populations of the lower Wye",
                Summary = "A survey.",
                ContentMarkdown = Paragraphs(random, 5),
                FirstSeenUtc = seen,
                PublishedUtc = seen
            });

            var bodyId = await items.UpsertAsync(new FeedItem
            {
                FeedId = needleFeed,
                Guid = "needle-body",
                Title = "An unrelated headline about nothing much",
                Summary = "Also unrelated.",
                FirstSeenUtc = seen,
                PublishedUtc = seen
            });
            await items.SetContentAsync(
                bodyId,
                Paragraphs(random, 8) + "\n\nAnd far below, a single mention of bittern in passing.",
                ContentSource.Extracted);

            await items.UpsertAsync(new FeedItem
            {
                FeedId = feedIds[7],
                Guid = "needle-summary-only",
                Title = "A headline that gives nothing away",
                Summary = "The piece is entirely about the bittern colony at Minsmere.",
                FirstSeenUtc = seen,
                PublishedUtc = seen
            });

            build.Stop();

            var count = (await items.QueryAsync(new ItemQuery(null, null, ItemFilter.All, 100000, 0))).Count;
            var size = new FileInfo(path).Length / 1024 / 1024;
            output.WriteLine($"corpus: {count} items across {Feeds} feeds, {size} MB, built in {build.ElapsedMilliseconds} ms");

            Assert.Equal(Items + 3, count);

            // --- As you type: every prefix of the word matches, so the list
            // never empties out mid-word and refills when the word is done.
            var typed = "bittern";
            for (var length = 1; length <= typed.Length; length++)
            {
                var prefix = typed[..length];
                var sw = Stopwatch.StartNew();
                var hits = await search.SearchAsync(prefix, 500);
                sw.Stop();
                output.WriteLine($"typing \"{prefix}\": {hits.Count} hits in {sw.Elapsed.TotalMilliseconds:0.0} ms");
                Assert.Equal(3, hits.Count);
            }

            // --- Ranking: title, then summary, then a mention buried in a body.
            var ranked = await search.SearchAsync("bittern", 500);
            output.WriteLine("rank order: " + string.Join(", ", ranked.Select(h => h.Item.Guid)));
            Assert.Equal("needle-title", ranked[0].Item.Guid);
            Assert.Equal("needle-summary-only", ranked[1].Item.Guid);
            Assert.Equal("needle-body", ranked[2].Item.Guid);

            // --- Snippets: each result carries the passage that explains it,
            // with the term marked, wherever in the article it came from.
            foreach (var hit in ranked)
            {
                output.WriteLine($"snippet [{hit.Item.Guid}]: {Show(hit.Snippet)}");
                Assert.Contains("bittern", hit.Snippet, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(SearchHit.MatchStart, hit.Snippet);
            }

            // The body hit's passage is from the end of a long article, so it
            // is not the text the ordinary preview line would have shown.
            Assert.DoesNotContain("unrelated headline", ranked[2].Snippet, StringComparison.OrdinalIgnoreCase);

            // --- Timing, over queries that actually hit a lot of rows ---
            foreach (var query in new[] { "compositor", "pipeline database", "renderin", "kingfisher estuary", "author" })
            {
                var timings = new List<double>();
                var hits = 0;
                for (var run = 0; run < 20; run++)
                {
                    var sw = Stopwatch.StartNew();
                    hits = (await search.SearchAsync(query, 500)).Count;
                    sw.Stop();
                    timings.Add(sw.Elapsed.TotalMilliseconds);
                }

                timings.Sort();
                var median = timings[timings.Count / 2];
                output.WriteLine(
                    $"query \"{query}\": {hits} hits, median {median:0.0} ms, " +
                    $"worst {timings[^1]:0.0} ms");

                // Deliberately loose. The number worth defending is "a query
                // finishes well inside the debounce", not a figure from this
                // machine on this day. See QueryBudgetMs for why the ceiling
                // is not the same on a hosted runner as it is on a desk.
                Assert.True(
                    median < QueryBudgetMs,
                    $"\"{query}\" took a median {median:0.0} ms, budget {QueryBudgetMs:0} ms");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Show(string snippet) => snippet
        .Replace(SearchHit.MatchStart.ToString(), "[", StringComparison.Ordinal)
        .Replace(SearchHit.MatchEnd.ToString(), "]", StringComparison.Ordinal);
}
