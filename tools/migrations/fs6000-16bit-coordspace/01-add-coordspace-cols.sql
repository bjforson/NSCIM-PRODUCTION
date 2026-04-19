-- 2026-04-19 (v2.9.6): annotation coordinate-space tracking
--
-- Context. FS6000 image serving is being upgraded: when a scan has raw .img
-- channels in fs6000images (HighEnergy/LowEnergy/Material), the image
-- pipeline now returns a 16-bit-sourced composite at the scan's native raw
-- dimensions (typically 3256x1378) instead of the vendor JPEG (typically
-- 2295x1378). Existing annotations were stored with coordinates relative
-- to the vendor JPEG; when the same scan is now served at raw dims, those
-- coordinates would land in the wrong spot unless we scale them.
--
-- Rather than migrate the 73 historical rows into a single coord space,
-- we tag each annotation with the dimensions of the image it was drawn
-- against. On read, the serving layer compares the annotation's stored
-- coord space to the currently-served image dims and scales if necessary.
--
-- Nullable. Historical rows (73 of them pre-2.9.6) stay NULL and are
-- treated as vendor-JPEG space for their scanner type at read time.
--
-- Idempotent: uses ADD COLUMN IF NOT EXISTS.

ALTER TABLE public.containerannotations
    ADD COLUMN IF NOT EXISTS coordspacewidth  integer NULL,
    ADD COLUMN IF NOT EXISTS coordspaceheight integer NULL;

-- Quick sanity check: confirm columns are there
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'containerannotations'
  AND column_name IN ('coordspacewidth', 'coordspaceheight')
ORDER BY column_name;
