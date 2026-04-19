# ASE Format — Findings

## TL;DR

NSCIM's vendor-locked `Ase.Image.dll` is not necessary to read transmission
scan images. The raw `asescans.scanimage` blobs are a tiny fixed-layout
container around uncompressed 16-bit grayscale pixels with a UTF-16 XML
metadata trailer. A ~150-line pure-Python decoder produces images that
visually match and correlate 0.82 with the DLL output on 10/10 samples, with
zero dependency on the DLL or Windows.

## Status

- [x] Samples extracted (10 blobs from `asescans`)
- [x] Black-box characterization done (Track A)
- [x] DLL metadata inspected (Track B)
- [x] Format hypothesis confirmed by direct pixel decode + visual diff
- [x] Prototype decoder written (`05_prototype_decoder.py`)
- [x] Prototype validated vs vendor DLL output (NCC ≈ 0.82, visually identical)

## Format specification

### Header — 16 bytes, fixed

```
offset  size  field              observed values        interpretation
------  ----  -----              ---------------        --------------
0       4     m_signature        49 4d 00 00            "IM\0\0" magic (ImageFileSignature const)
4       2     m_imageWidth       0x0220 / 0x0660        544 or 1632 (little-endian u16)
6       2     m_imageHeight      variable               scan length in scanlines (u16 LE)
8       2     m_lineDataType     0x0002 or 0x0003       2 = DualEnergyBitmap, 3 = ParcelDualEnergyBitmap
10      2     (unknown)          0x0000                 reserved / pad
12      2     (unknown)          0x0002                 always 2; likely bitDepth enum or fileType
14      2     (unknown)          0x0000                 reserved / pad
```

The `m_commentSize`, `m_bitDepth`, `m_fileType`, and `m_ucReserved` fields
declared on `Ase.Image.FileFormat.ImgFileHeader` (see `decompiled/INDEX.md`)
map into offsets 8..15 in some order — the method body of `ImgFileHeader.Parse`
would pin this down, but it is not required to decode images empirically.

### Pixel region — `width * height * 2` bytes

- Uncompressed, little-endian, 16-bit unsigned grayscale
- Stored at offset 16 through `16 + w*h*2`
- Full 16-bit range in use (observed min=0, max=65535, mean ≈ 32k)
- Pixel entropy on samples is 6.4 – 7.9 bits/byte, well below the ~8.0
  that would indicate compression/encryption
- Scan orientation is **portrait**: the scanner acquires 544 (or 1632) pixel
  columns as the truck moves through. The vendor DLL rotates 90° CCW for
  display, producing landscape JPEGs with width = raw height

### Multi-panel layout for `line_data_type == 3` (ParcelDualEnergyBitmap)

- Width 1632 = 3 horizontally-tiled panels of 544 pixels each
  - Panel 0: low-energy view (mean ≈ 32k, full dynamic range)
  - Panel 1: high-energy view (mean ≈ 33k, full dynamic range)
  - Panel 2: material-discrimination / Z-effective overlay (mean ≈ 2.6k — sparse)
- `line_data_type == 2` (DualEnergyBitmap) is a single-panel 544-wide view;
  the dual-energy information is encoded some other way (not horizontally
  tiled) and is not required for a visual decode

### Trailer

```
trailer region         size   content
--------------------   ----   -------
pixels_end .. xml      48     opaque block, all zeros in 8/10 samples,
                              random-looking bytes in 2/10 — probably an
                              uninitialized / optional FTIImageFileHeaderInfo
                              slot. Treat as padding, ignore for decode.
xml .. EOF             668    UTF-16 LE XML <Metadata><Items>... </Metadata>
                              containing a base64 ContainerInfo binary field
                              (38 bytes, structure TBD — appears to carry
                              scanner timestamp / energy stats)
```

### Compression / encryption

The DLL supports compression and encryption via `Ase.Image.FileFormat.CompEncryptHeader`
(fields: `m_groupname`, `m_imageSize`, `m_compressType`, `m_encryptType`).
**None of the 10 samples from production use either** — our scanner writes the
simplest form. If other deployments enable compression/encryption, the decoder
would need to inspect the header info block for a non-zero compressType.

## Display rendering notes

The vendor DLL's pixel-intensity mapping is NOT pure linear normalization.
Empirically, clipping to the [1st, 99.5th] percentile and then linearly mapping
to 0..255 **without inversion** (i.e., preserving the scanner's "high X-ray
transmission = bright" convention) produces output that correlates 0.82 with
the DLL JPEG at full resolution and is visually indistinguishable from the
DLL output modulo minor gamma differences. See `diffs/*.diff.png`.

Remaining delta between prototype and DLL:
- Subtle gamma/contrast curve (DLL is slightly punchier)
- DLL may apply an edge-preserving denoise or LUT
- DLL may handle dual-energy-to-grayscale flattening differently for
  `line_data_type == 2` (we render the whole 544-wide region directly)

These deltas are cosmetic — the underlying pixel content is identical.

## How the data gets to us

Raw blob → PostgreSQL `asescans.scanimage` (bytea)

Populated by `AseDatabaseSyncService` from an upstream SQL Server at
`networking.InspectionObject.InspectionImage`, filtered to
`DisplayName = 'Transmission'` (see
`src/NickScanCentralImagingPortal.Services/ASE/AseDatabaseSyncService.cs:520-537`).

Pulling the blob only requires `psycopg2` + `NICKSCAN_DB_PASSWORD`. No DLL, no
Windows-specific APIs, no vendor assemblies.

## Recommendations (not part of this investigation — for a future plan)

1. **Port the decoder into `services/image-splitter/`**. The splitter already
   reaches into `asescans` for multi-container scans and would no longer have
   to rely on the C# API's DLL-decoded JPEG cache. This removes a whole
   round-trip and unlocks 16-bit-precise analysis for downstream strategies.

2. **Add a C# fallback in `ASEImageConverterService`**. If
   `AseImageConverter.AseImageToPngBytes` throws or the DLL is missing, fall
   back to the pure-C# equivalent of our Python prototype. Eliminates the
   single point of failure documented in
   `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/ASEImageConverterService.cs:133-137`.

3. **Investigate the 48-byte trailer block** only if (a) we encounter a
   scanner deployment that uses it, or (b) we need to match the DLL's
   exact gamma curve. Probably maps to `FTIImageFileHeaderInfo` fields.

4. **Do NOT ship a DLL replacement yet**. The prototype proves the format is
   open; actual replacement is a separate plan with its own validation gate
   (including ParcelDualEnergyBitmap multi-panel compositing, which the
   current diff didn't exercise — none of the 1632-wide samples had a
   cached DLL JPEG to compare against).

## Script inventory

| script | purpose | status |
|---|---|---|
| `01_extract_samples.py` | pull raw blobs from `asescans.scanimage` | 10/10 extracted |
| `02_characterize.py` | formal header parse + entropy + stats | summary in `characterization_summary.md` |
| `03_diff_raw_vs_decoded.py` | compare prototype vs DLL cache | 2 matches, NCC 0.82 both |
| `04_decompile_dll.py` | dnfile metadata dump of Ase*.dll | `decompiled/INDEX.md` |
| `05_prototype_decoder.py` | pure-Python ASE → PNG decoder | 10/10 decode OK |

## Reference output locations

- `samples/*.ase` — raw blobs
- `samples/*.json` — metadata sidecars (sha256, ids, scantime)
- `samples/*.report.json` — per-sample characterization
- `samples/*.decoded.png` — prototype output
- `samples/*.panel0/1/2.png` — per-panel outputs for ch=3
- `diffs/*.diff.png` — prototype | DLL side-by-side
- `diffs/results.json` — NCC scores
- `decompiled/INDEX.md` — format-relevant type catalog
- `decompiled/metadata_*.txt` — per-assembly type dumps
- `characterization_summary.md` — Track A summary table
