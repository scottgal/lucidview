-- The database mylo's two driving scripts assert against.
--
-- Applied by run-reader-smoke.sh and run-reader-settings.sh to a throwaway
-- database in a temporary directory (see MYLO_DATA_DIR in
-- LucidReader/App.axaml.cs), never to a real profile. Every count, title and
-- status line either script expects is decided here, which is what lets both
-- run twice in a row from any starting state and assert exact numbers rather
-- than "contains something".
--
-- Feed addresses use the reserved .test TLD so that nothing here can resolve,
-- and the seeded settings.json turns startup refresh off, so a run makes no
-- network requests of its own.
--
-- Article dates are relative to now rather than literal, and only ever a few
-- days back. A literal date would quietly stop being true: the retention
-- settings keep read articles for 30 days, so a fixed date would eventually
-- age past that and the "Clean up now" check in reader-settings.yaml would
-- start deleting a row and asserting the wrong count. That is precisely the
-- kind of rot these scripts exist to avoid.

INSERT INTO folders (id, name, sort_order) VALUES (1, 'Harness Folder', 0);

INSERT INTO feeds (id, folder_id, feed_url, site_url, title, is_enabled)
VALUES (1, 1, 'https://harness-alpha.test/feed.xml', 'https://harness-alpha.test/', 'Harness Alpha', 1);

INSERT INTO feeds (id, folder_id, feed_url, site_url, title, is_enabled)
VALUES (2, NULL, 'https://harness-beta.test/feed.xml', 'https://harness-beta.test/', 'Harness Beta', 1);

-- Harness Alpha: three articles, one of them already read.
--
-- Compositor internals is offline_state=2 (Downloaded) with
-- content_source=1 (Extracted), the one combination that hides the offline
-- badge; every other row leaves the badge showing, so selecting one then the
-- other proves the badge is driven by the item rather than stuck on.
INSERT INTO items (id, feed_id, guid, link, title, author, published_utc, summary,
                   content_markdown, content_source, is_read, is_starred,
                   first_seen_utc, offline_state)
VALUES (1, 1, 'alpha-1', 'https://harness-alpha.test/compositor',
        'Compositor internals explained', 'Alpha Author',
        strftime('%Y-%m-%dT%H:%M:%SZ','now','-2 days'), 'A summary of the compositor pipeline.',
        'The compositor pipeline, end to end.

Rendering happens on its own thread.', 1, 0, 0, strftime('%Y-%m-%dT%H:%M:%SZ','now','-2 days'), 2);

-- The body here is long on purpose, and the word "kingfishers" sits past the
-- 180th character. That is what verify-reader-search.yaml uses to tell the two
-- preview lines apart: the ordinary row preview is the first 180 characters of
-- this body, so it cannot contain that word, while a search for it must show
-- the passage around it. If this text is ever shortened, or the word moved
-- earlier, that script stops proving anything.
INSERT INTO items (id, feed_id, guid, link, title, published_utc, summary,
                   content_markdown, content_source, is_read, is_starred,
                   first_seen_utc, offline_state)
VALUES (2, 1, 'alpha-2', 'https://harness-alpha.test/weeknotes',
        'Weeknotes from the harness', strftime('%Y-%m-%dT%H:%M:%SZ','now','-3 days'),
        'A short status update.',
        'Nothing shipped, nothing broke. The week was spent on small things: a slow query that turned out to be an index nobody had noticed was missing, a settings page that saved twice, and a long walk along the towpath on Friday afternoon, where the nesting kingfishers were finally back on the far bank.',
        0, 0, 0, strftime('%Y-%m-%dT%H:%M:%SZ','now','-3 days'), 0);

INSERT INTO items (id, feed_id, guid, link, title, published_utc, summary,
                   content_markdown, content_source, is_read, is_starred,
                   first_seen_utc, offline_state)
VALUES (3, 1, 'alpha-3', 'https://harness-alpha.test/archive',
        'An article already read', strftime('%Y-%m-%dT%H:%M:%SZ','now','-4 days'),
        'Read before this run started.',
        'Read before this run started.', 0, 1, 0, strftime('%Y-%m-%dT%H:%M:%SZ','now','-4 days'), 0);

-- Harness Beta: two articles, one of them starred.
INSERT INTO items (id, feed_id, guid, link, title, published_utc, summary,
                   content_markdown, content_source, is_read, is_starred,
                   first_seen_utc, offline_state)
VALUES (4, 2, 'beta-1', 'https://harness-beta.test/release',
        'Beta release notes', strftime('%Y-%m-%dT%H:%M:%SZ','now','-5 days'),
        'What changed in this beta.',
        'Compositor fixes shipped in this beta build.', 0, 0, 1,
        strftime('%Y-%m-%dT%H:%M:%SZ','now','-5 days'), 0);

INSERT INTO items (id, feed_id, guid, link, title, published_utc, summary,
                   content_markdown, content_source, is_read, is_starred,
                   first_seen_utc, offline_state)
VALUES (5, 2, 'beta-2', 'https://harness-beta.test/unrelated',
        'An unrelated beta item', strftime('%Y-%m-%dT%H:%M:%SZ','now','-6 days'),
        'Nothing relevant in here.',
        'Nothing relevant in here at all.', 0, 0, 0, strftime('%Y-%m-%dT%H:%M:%SZ','now','-6 days'), 0);
