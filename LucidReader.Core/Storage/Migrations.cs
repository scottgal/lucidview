namespace LucidReader.Core.Storage;

/// <summary>
/// Forward-only schema migrations. Append only. Once a migration has shipped
/// it is frozen; corrections go in a new entry.
/// </summary>
public static class Migrations
{
    public static IReadOnlyList<string> All { get; } = new[] { V1, V2 };

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
}
