namespace MarkdownViewer.Lab.Services.Storage;

internal static class MetadataSchema
{
    public const string CreateTablesSql = @"
        CREATE TABLE IF NOT EXISTS documents (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            path TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            title TEXT NOT NULL,
            ingested_utc TEXT NOT NULL,
            source TEXT NOT NULL,
            state TEXT NOT NULL,
            UNIQUE(path, source)
        );
        CREATE INDEX IF NOT EXISTS idx_documents_source ON documents(source);

        CREATE TABLE IF NOT EXISTS segments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_id INTEGER NOT NULL REFERENCES documents(id),
            ordinal INTEGER NOT NULL,
            content_hash INTEGER NOT NULL,
            salience REAL NOT NULL DEFAULT 0.0,
            created_utc TEXT NOT NULL,
            source TEXT NOT NULL,
            UNIQUE(content_hash)
        );
        CREATE INDEX IF NOT EXISTS idx_segments_doc ON segments(document_id);
        CREATE INDEX IF NOT EXISTS idx_segments_source ON segments(source);

        CREATE TABLE IF NOT EXISTS entities (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            type TEXT NOT NULL,
            surface TEXT NOT NULL,
            UNIQUE(type, surface)
        );

        CREATE TABLE IF NOT EXISTS entity_links (
            segment_id INTEGER NOT NULL REFERENCES segments(id),
            entity_id INTEGER NOT NULL REFERENCES entities(id),
            span_start INTEGER NOT NULL,
            span_end INTEGER NOT NULL,
            PRIMARY KEY (segment_id, entity_id, span_start)
        );

        CREATE TABLE IF NOT EXISTS salient_terms (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_id INTEGER NOT NULL REFERENCES documents(id),
            term TEXT NOT NULL,
            score REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_salient_doc ON salient_terms(document_id);

        CREATE TABLE IF NOT EXISTS personal_facts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user TEXT NOT NULL DEFAULT 'default',
            text TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS workspace_attachments (
            path TEXT PRIMARY KEY,
            include_glob TEXT,
            exclude_glob TEXT
        );

        CREATE TABLE IF NOT EXISTS workspace_library (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            content_hash INTEGER NOT NULL UNIQUE,
            original_url TEXT,
            added_utc TEXT NOT NULL
        );
    ";
}
