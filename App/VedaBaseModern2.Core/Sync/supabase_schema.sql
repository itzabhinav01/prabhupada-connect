-- ============================================================================
-- Bhaktivedanta VedaBase Modern — Production Supabase / PostgreSQL Schema
-- Phase 6: Offline-First Cloud Sync Schema with Row Level Security (RLS)
-- ============================================================================
-- IMPORTANT ARCHITECTURAL RULES:
-- 1. This schema contains ONLY personal research data (bookmarks, highlights, notes).
-- 2. The canonical scripture corpus (corpus_v10_2_canonical.db) is strictly local and
--    is NEVER uploaded to Supabase.
-- 3. Row Level Security (RLS) is ENABLED on all tables.
-- 4. Users can only access, insert, update, or delete their own rows (auth.uid() = user_id).
-- 5. No service_role key is ever required. The client connects with the public anon key
--    and the user's JWT access token.
-- ============================================================================

-- 1. Schema Info (Compatibility Tracking)
CREATE TABLE IF NOT EXISTS public.vb_schema_info (
    version INT PRIMARY KEY,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE public.vb_schema_info ENABLE ROW LEVEL SECURITY;

-- Allow anyone (anon + authenticated) to check schema compatibility
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE tablename = 'vb_schema_info' AND policyname = 'vb_schema_info_read_policy'
    ) THEN
        CREATE POLICY vb_schema_info_read_policy ON public.vb_schema_info
            FOR SELECT USING (true);
    END IF;
END $$;

INSERT INTO public.vb_schema_info (version, updated_at)
VALUES (1, NOW())
ON CONFLICT (version) DO UPDATE SET updated_at = EXCLUDED.updated_at;


-- 2. Bookmark Collections Table
CREATE TABLE IF NOT EXISTS public.vb_bookmark_collections (
    id TEXT NOT NULL,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    device_id TEXT NOT NULL,
    PRIMARY KEY (user_id, id)
);

ALTER TABLE public.vb_bookmark_collections ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'vb_bookmark_collections' AND policyname = 'collections_owner_policy') THEN
        CREATE POLICY collections_owner_policy ON public.vb_bookmark_collections
            FOR ALL USING (auth.uid() = user_id) WITH CHECK (auth.uid() = user_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_vb_collections_user_updated
    ON public.vb_bookmark_collections (user_id, updated_at);


-- 3. Bookmarks Table
CREATE TABLE IF NOT EXISTS public.vb_bookmarks (
    id TEXT NOT NULL,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    record_key TEXT NOT NULL,
    collection_id TEXT NULL,
    title TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    device_id TEXT NOT NULL,
    PRIMARY KEY (user_id, id)
);

ALTER TABLE public.vb_bookmarks ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'vb_bookmarks' AND policyname = 'bookmarks_owner_policy') THEN
        CREATE POLICY bookmarks_owner_policy ON public.vb_bookmarks
            FOR ALL USING (auth.uid() = user_id) WITH CHECK (auth.uid() = user_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_vb_bookmarks_user_updated
    ON public.vb_bookmarks (user_id, updated_at);

-- Ensure only one active bookmark per verse per user (matching local SQLite uniqueness rule)
CREATE UNIQUE INDEX IF NOT EXISTS idx_vb_bookmarks_user_active_verse
    ON public.vb_bookmarks (user_id, record_key)
    WHERE deleted_at IS NULL;


-- 4. Highlights Table
CREATE TABLE IF NOT EXISTS public.vb_highlights (
    id TEXT NOT NULL,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    record_key TEXT NOT NULL,
    field TEXT NOT NULL,
    color TEXT NOT NULL,
    start_offset INT NOT NULL DEFAULT -1,
    length INT NOT NULL DEFAULT -1,
    selected_text TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    device_id TEXT NOT NULL,
    PRIMARY KEY (user_id, id)
);

ALTER TABLE public.vb_highlights ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'vb_highlights' AND policyname = 'highlights_owner_policy') THEN
        CREATE POLICY highlights_owner_policy ON public.vb_highlights
            FOR ALL USING (auth.uid() = user_id) WITH CHECK (auth.uid() = user_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_vb_highlights_user_updated
    ON public.vb_highlights (user_id, updated_at);


-- 5. Notes Table
CREATE TABLE IF NOT EXISTS public.vb_notes (
    id TEXT NOT NULL,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    record_key TEXT NULL,
    title TEXT NULL,
    content TEXT NOT NULL,
    field TEXT NULL,
    start_offset INT NULL,
    length INT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    device_id TEXT NOT NULL,
    PRIMARY KEY (user_id, id)
);

ALTER TABLE public.vb_notes ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'vb_notes' AND policyname = 'notes_owner_policy') THEN
        CREATE POLICY notes_owner_policy ON public.vb_notes
            FOR ALL USING (auth.uid() = user_id) WITH CHECK (auth.uid() = user_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_vb_notes_user_updated
    ON public.vb_notes (user_id, updated_at);
