using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// V7 against a database that already has items and tags in it, which is the
/// only version of this migration that matters: anyone who used the T
/// shortcut before this build has one.
///
/// Built by applying V1 to V6 by hand and stamping user_version at 6, so this
/// is genuinely an old database being opened by a new build rather than a new
/// one pretending. Same shape as CanonicalIdMigrationTests, for the same
/// reason.
/// </summary>
public class ItemTagIndexMigrationTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task SeedV6DatabaseAsync()
    {
        await using var connection = _temp.Open();
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");

        for (var version = 0; version < 6; version++)
            await ExecuteAsync(connection, Migrations.All[version]);

        await ExecuteAsync(connection, "PRAGMA user_version = 6;");

        await ExecuteAsync(connection,
            """
            INSERT INTO feeds (id, feed_url, title) VALUES (1, 'https://example.com/rss', 'RSS');

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc, canonical_id)
            VALUES (1, 1, 'one', 'https://example.com/one', 'Tagged already',
                    '2026-08-28T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00',
                    'https://example.com/one');

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc, canonical_id)
            VALUES (2, 1, 'two', 'https://example.com/two', 'Untagged',
                    '2026-08-27T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00',
                    'https://example.com/two');

            INSERT INTO tags (id, name) VALUES (1, 'later');
            INSERT INTO item_tags (item_id, tag_id) VALUES (1, 1);
            """);
    }

    [Fact]
    public async Task The_migration_runs_on_a_database_that_already_holds_items_and_tags()
    {
        await SeedV6DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);

        await using var connection = _temp.Open();
        Assert.Equal(Migrations.All.Count, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_item_tags_tag';"));
    }

    [Fact]
    public async Task Existing_tags_and_their_links_come_through_the_migration_intact()
    {
        await SeedV6DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var tags = new TagRepository(db);

        Assert.Equal(["later"], await tags.GetForItemAsync(1));

        var usage = Assert.Single(await tags.GetUsageAsync());
        Assert.Equal("later", usage.Name);
        Assert.Equal(1, usage.ArticleCount);
        Assert.Equal(1, usage.UnreadCount);
    }

    [Fact]
    public async Task A_tag_view_works_immediately_after_the_migration()
    {
        await SeedV6DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var items = new ItemRepository(db);

        var listed = await items.QueryAsync(
            new ItemQuery(null, null, ItemFilter.All, 100, 0) { TagName = "later" });

        Assert.Equal("Tagged already", Assert.Single(listed).Title);
    }

    /// <summary>
    /// The index is what the migration is for, so this asserts the planner
    /// actually reaches for it rather than only that it exists. Without it the
    /// by-tag lookup scans item_tags, once per tag on every sidebar rebuild.
    /// </summary>
    [Fact]
    public async Task The_by_tag_lookup_uses_the_new_index_rather_than_scanning()
    {
        await SeedV6DatabaseAsync();
        await using (var db = await ReaderDatabase.OpenAsync(_temp.Path)) { }

        await using var connection = _temp.Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXPLAIN QUERY PLAN
            SELECT it.item_id FROM item_tags it
            JOIN tags t ON t.id = it.tag_id
            WHERE t.name = 'later' COLLATE NOCASE;
            """;

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) plan.Add(reader.GetString(3));

        Assert.Contains(plan, line => line.Contains("ix_item_tags_tag"));
    }

    private static async Task<int> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
