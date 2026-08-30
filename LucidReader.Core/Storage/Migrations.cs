namespace LucidReader.Core.Storage;

/// <summary>
/// Forward-only schema migrations. Append only. Once a migration has shipped
/// it is frozen; corrections go in a new entry.
/// </summary>
public static class Migrations
{
    public static IReadOnlyList<string> All { get; } = new[] { V1, V2, V3, V4, V5, V6 };

    private const string V1 = """
        CREATE TABLE folders (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT    NOT NULL,
            sort_order  INTEGER NOT NULL DEFAULT 0,
            parent_id   INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL
        );

        CREATE TABLE feeds (
            id                        INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_id                 INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL,
            feed_url                  TEXT    NOT NULL,
            site_url                  TEXT    NULL,
            title                     TEXT    NULL,
            title_override            TEXT    NULL,
            icon_path                 TEXT    NULL,
            is_enabled                INTEGER NOT NULL DEFAULT 1,
            last_fetched_utc          TEXT    NULL,
            last_success_utc          TEXT    NULL,
            etag                      TEXT    NULL,
            last_modified             TEXT    NULL,
            consecutive_failures      INTEGER NOT NULL DEFAULT 0,
            last_error                TEXT    NULL,
            next_due_utc              TEXT    NULL,
            refresh_interval_minutes  INTEGER NULL,
            auto_download             INTEGER NULL,
            fetch_full_text           INTEGER NULL,
            retention_days            INTEGER NULL
        );

        CREATE UNIQUE INDEX ix_feeds_url ON feeds(feed_url);
        CREATE INDEX ix_feeds_next_due ON feeds(next_due_utc) WHERE is_enabled = 1;

        CREATE TABLE items (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            feed_id           INTEGER NOT NULL REFERENCES feeds(id) ON DELETE CASCADE,
            guid              TEXT    NOT NULL,
            link              TEXT    NULL,
            title             TEXT    NULL,
            author            TEXT    NULL,
            published_utc     TEXT    NULL,
            updated_utc       TEXT    NULL,
            summary           TEXT    NULL,
            content_markdown  TEXT    NULL,
            content_source    INTEGER NOT NULL DEFAULT 0,
            is_read           INTEGER NOT NULL DEFAULT 0,
            is_starred        INTEGER NOT NULL DEFAULT 0,
            first_seen_utc    TEXT    NOT NULL,
            offline_state     INTEGER NOT NULL DEFAULT 0,
            offline_error     TEXT    NULL
        );

        CREATE UNIQUE INDEX ix_items_feed_guid ON items(feed_id, guid);
        CREATE INDEX ix_items_feed_published ON items(feed_id, published_utc DESC);
        CREATE INDEX ix_items_unread ON items(is_read, published_utc DESC);
        CREATE INDEX ix_items_starred ON items(is_starred) WHERE is_starred = 1;
        CREATE INDEX ix_items_offline_pending ON items(offline_state) WHERE offline_state = 1;

        CREATE TABLE tags (
            id    INTEGER PRIMARY KEY AUTOINCREMENT,
            name  TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX ix_tags_name ON tags(name);

        CREATE TABLE item_tags (
            item_id  INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            tag_id   INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
            PRIMARY KEY (item_id, tag_id)
        );

        CREATE VIRTUAL TABLE items_fts USING fts5(
            title,
            content_markdown,
            content='items',
            content_rowid='id',
            tokenize='unicode61'
        );

        CREATE TRIGGER items_fts_insert AFTER INSERT ON items BEGIN
            INSERT INTO items_fts(rowid, title, content_markdown)
            VALUES (new.id, new.title, new.content_markdown);
        END;

        CREATE TRIGGER items_fts_delete AFTER DELETE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, content_markdown)
            VALUES ('delete', old.id, old.title, old.content_markdown);
        END;

        CREATE TRIGGER items_fts_update AFTER UPDATE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, content_markdown)
            VALUES ('delete', old.id, old.title, old.content_markdown);
            INSERT INTO items_fts(rowid, title, content_markdown)
            VALUES (new.id, new.title, new.content_markdown);
        END;
        """;

    // auto_paused_utc: nullable so a UI can tell an auto-paused feed (set by
    // FeedRefreshService when consecutive_failures reaches
    // BackoffPolicy.AutoPauseThreshold) apart from one the user disabled
    // deliberately (never sets this column). Cleared, along with
    // consecutive_failures and last_error, whenever FeedRepository.SetEnabledAsync
    // re-enables a feed - see that method for why re-enabling has to reset all
    // three or a re-enabled feed is re-disabled on its very first failure.
    //
    // item_tombstones: records (feed_id, guid) pairs RetentionService has
    // deleted, so a later refresh's upsert can tell "genuinely new item" apart
    // from "this exact item was deliberately pruned" and not resurrect the
    // latter as an unread item with its downloaded content gone. Tombstones
    // are themselves pruned on a much longer horizon (RetentionService's
    // TombstoneRetention) than any item retention window, both so the table
    // cannot grow without bound and so a guid genuinely reused by the
    // publisher long after the original item is gone is eventually treated as
    // new again rather than blocked forever.
    private const string V2 = """
        ALTER TABLE feeds ADD COLUMN auto_paused_utc TEXT NULL;

        CREATE TABLE item_tombstones (
            feed_id      INTEGER NOT NULL REFERENCES feeds(id) ON DELETE CASCADE,
            guid         TEXT    NOT NULL,
            deleted_utc  TEXT    NOT NULL,
            PRIMARY KEY (feed_id, guid)
        );

        CREATE INDEX ix_item_tombstones_deleted ON item_tombstones(deleted_utc);
        """;

    // image_url: the article's social-card image (OpenGraph/Twitter),
    // captured by SiteMetadataExtractor from HTML OfflineDownloader already
    // fetched for full-text extraction - never a page fetched on purpose for
    // this. Publisher-owned, so ItemRepository's upsert overwrites it on
    // conflict alongside title and summary, unlike the reader-owned columns
    // (is_read, is_starred, content_markdown, offline_state) upsert never
    // touches.
    private const string V3 = """
        ALTER TABLE items ADD COLUMN image_url TEXT NULL;
        """;

    // Narrows the FTS update trigger to the two columns the index actually
    // mirrors. V1 created it as an unscoped AFTER UPDATE ON items, so every
    // write to the row fired it: marking an article read, starring it,
    // marking a whole feed read, recording a failed download. Each of those
    // deleted and reinserted the item's entire content_markdown term list for
    // no gain, on the path the user is on while reading. Measured over 2000
    // items of about 10KB each, one "UPDATE items SET is_read = 1 WHERE
    // is_read = 0" cost 148ms and 42MB with the V1 trigger against 26ms and
    // 33MB with this one.
    //
    // Dropping and recreating a trigger does not touch the index contents, so
    // no rebuild is needed: rows are unchanged, and every subsequent edit to
    // title or content_markdown still maintains them.
    private const string V4 = """
        DROP TRIGGER items_fts_update;

        CREATE TRIGGER items_fts_update
        AFTER UPDATE OF title, content_markdown ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, content_markdown)
            VALUES ('delete', old.id, old.title, old.content_markdown);
            INSERT INTO items_fts(rowid, title, content_markdown)
            VALUES (new.id, new.title, new.content_markdown);
        END;
        """;

    // Widens the full-text index from (title, content_markdown) to
    // (title, author, summary, content_markdown).
    //
    // summary is the important one. content_markdown only exists once an
    // article has been downloaded and converted, and plenty of items never
    // get that far: auto-download can be off, the fetch can fail, and an item
    // stored with offline_state = 0 is never re-queued by
    // GetPendingOfflineAsync. For all of those the summary is the only body
    // the database holds, so leaving it out of the index made every one of
    // them findable by title alone. author is cheap to add and is a search
    // people actually run ("that piece by so-and-so").
    //
    // The column set of an FTS5 table cannot be altered, so the table and its
    // three triggers are dropped and recreated, then repopulated with the
    // 'rebuild' command, which reads every row back out of items (the
    // external content table) and reindexes it. That is why this migration is
    // safe on a populated database and why it costs one full reindex on the
    // first launch after upgrading.
    //
    // The tokenizer stays unicode61. Adding the porter stemmer was measured
    // and rejected: porter stems the query token as well as the indexed one,
    // so a prefix search for a partly-typed word stops matching as soon as
    // what has been typed is longer than the stem. Against a document
    // containing "running", porter matches "run" and "running" but nothing
    // in between - "runn", "runni", "runnin" all return zero rows - so the
    // result list empties out mid-word and refills when the word is
    // finished. unicode61 matches every one of those prefixes. Since search
    // runs on every keystroke, the stemmer would break the case the index is
    // most used for in order to improve the finished-word case, which the
    // prefix operator already covers in the direction that matters
    // ("run" finds "running").
    //
    // The update trigger keeps V4's UPDATE OF narrowing, extended to the two
    // new columns. It deliberately does NOT list every column of items: a
    // write to is_read, is_starred or offline_state must still leave the
    // index alone, which is the whole point of V4.
    private const string V5 = """
        DROP TRIGGER items_fts_insert;
        DROP TRIGGER items_fts_delete;
        DROP TRIGGER items_fts_update;
        DROP TABLE items_fts;

        CREATE VIRTUAL TABLE items_fts USING fts5(
            title,
            author,
            summary,
            content_markdown,
            content='items',
            content_rowid='id',
            tokenize='unicode61'
        );

        CREATE TRIGGER items_fts_insert AFTER INSERT ON items BEGIN
            INSERT INTO items_fts(rowid, title, author, summary, content_markdown)
            VALUES (new.id, new.title, new.author, new.summary, new.content_markdown);
        END;

        CREATE TRIGGER items_fts_delete AFTER DELETE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, author, summary, content_markdown)
            VALUES ('delete', old.id, old.title, old.author, old.summary, old.content_markdown);
        END;

        CREATE TRIGGER items_fts_update
        AFTER UPDATE OF title, author, summary, content_markdown ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, author, summary, content_markdown)
            VALUES ('delete', old.id, old.title, old.author, old.summary, old.content_markdown);
            INSERT INTO items_fts(rowid, title, author, summary, content_markdown)
            VALUES (new.id, new.title, new.author, new.summary, new.content_markdown);
        END;

        INSERT INTO items_fts(items_fts) VALUES('rebuild');
        """;

    // canonical_id: the article's identity across feeds, computed from its
    // link by CanonicalArticleId. Two rows carrying the same canonical_id are
    // the same article arriving under two subscriptions, which is what a site
    // publishing both an RSS and an Atom feed produces for every post it has.
    //
    // Nullable, and left null by this migration rather than backfilled here.
    // The normalisation is real code (scheme and host case, a trailing slash,
    // tracking parameters, the fragment) and writing an approximation of it in
    // SQL would give existing rows a slightly different identity from the one
    // new rows get, which is worse than having none: rows that should pair up
    // would not, and there would be no way to tell the two spellings apart
    // later. CanonicalIdBackfill runs immediately after migration and fills
    // them with the same function the write path uses. It is restartable
    // (WHERE canonical_id IS NULL) so an interrupted backfill simply resumes.
    //
    // Safe on a populated database: one ALTER TABLE ADD COLUMN, which SQLite
    // performs by rewriting the table header only, plus one index build. No
    // row is read or rewritten, and nothing that already worked depends on the
    // column being set - every query that partitions on it does so through
    // COALESCE(canonical_id, 'row:' || id), so a row with a null identity
    // stands alone exactly as it did before this column existed.
    private const string V6 = """
        ALTER TABLE items ADD COLUMN canonical_id TEXT NULL;

        CREATE INDEX ix_items_canonical ON items(canonical_id) WHERE canonical_id IS NOT NULL;
        """;
}
