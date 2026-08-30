using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// V6 against a database that already has rows in it, which is the only
/// version of this migration that matters: every user upgrading has one.
///
/// The database is built by applying V1 to V5 by hand and stamping
/// user_version at 5, so this is genuinely an old database being opened by a
/// new build rather than a new one pretending.
/// </summary>
public class CanonicalIdMigrationTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task SeedV5DatabaseAsync()
    {
        await using var connection = _temp.Open();

        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");

        // Every migration up to but NOT including the one under test.
        for (var version = 0; version < 5; version++)
            await ExecuteAsync(connection, Migrations.All[version]);

        await ExecuteAsync(connection, "PRAGMA user_version = 5;");

        await ExecuteAsync(connection,
            """
            INSERT INTO feeds (id, feed_url, title) VALUES (1, 'https://example.com/rss', 'RSS');
            INSERT INTO feeds (id, feed_url, title) VALUES (2, 'https://example.com/atom', 'Atom');

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc)
            VALUES (1, 1, 'rss-1', 'https://example.com/posts/one/?utm_source=rss', 'Shared',
                    '2026-08-28T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00');

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc)
            VALUES (2, 2, 'tag:example,2026:1', 'https://EXAMPLE.com/posts/one#top', 'Shared',
                    '2026-08-28T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00');

            INSERT INTO items (id, feed_id, guid, link, title, published_utc, first_seen_utc)
            VALUES (3, 1, 'rss-2', 'https://example.com/posts/two', 'Different',
                    '2026-08-27T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00');

            INSERT INTO items (id, feed_id, guid, title, published_utc, first_seen_utc)
            VALUES (4, 1, 'rss-3', 'No link at all',
                    '2026-08-26T09:00:00.0000000+00:00', '2026-08-28T10:00:00.0000000+00:00');
            """);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Opening_a_populated_v5_database_migrates_and_keeps_every_row()
    {
        await SeedV5DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);

        Assert.Equal(4, await ScalarAsync(db, "SELECT count(*) FROM items;"));
        Assert.Equal(Migrations.All.Count, await ScalarAsync(db, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task The_backfill_gives_existing_rows_the_same_identity_new_rows_get()
    {
        await SeedV5DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);

        var first = await TextAsync(db, "SELECT canonical_id FROM items WHERE id = 1;");
        var second = await TextAsync(db, "SELECT canonical_id FROM items WHERE id = 2;");

        Assert.Equal("https://example.com/posts/one", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task The_backfill_leaves_a_row_with_no_link_without_an_identity()
    {
        await SeedV5DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);

        Assert.Equal(1, await ScalarAsync(db, "SELECT count(*) FROM items WHERE canonical_id IS NULL;"));
    }

    /// <summary>
    /// The point of the whole exercise for an existing user: the doubles they
    /// already have stop showing up twice, with nothing deleted to achieve it.
    /// </summary>
    [Fact]
    public async Task Doubles_a_user_already_had_collapse_in_the_list_without_being_deleted()
    {
        await SeedV5DatabaseAsync();

        await using var db = await ReaderDatabase.OpenAsync(_temp.Path);
        var items = new ItemRepository(db);

        var listed = await items.QueryAsync(new ItemQuery(null, null, ItemFilter.All, 100, 0));

        // Four rows stored, three articles: the RSS and Atom copies of the
        // same post are one.
        Assert.Equal(4, await ScalarAsync(db, "SELECT count(*) FROM items;"));
        Assert.Equal(3, listed.Count);
    }

    [Fact]
    public async Task Running_the_backfill_again_writes_nothing_and_changes_nothing()
    {
        await SeedV5DatabaseAsync();

        await using (var db = await ReaderDatabase.OpenAsync(_temp.Path))
        {
            Assert.Equal(3, await ScalarAsync(db, "SELECT count(*) FROM items WHERE canonical_id IS NOT NULL;"));
        }

        await using var connection = _temp.Open();
        Assert.Equal(0, await CanonicalIdBackfill.RunAsync(connection));
    }

    private static Task<int> ScalarAsync(ReaderDatabase db, string sql) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });

    private static Task<string?> TextAsync(ReaderDatabase db, string sql) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync() as string;
        });
}
