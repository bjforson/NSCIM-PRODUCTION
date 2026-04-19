# ASE Image Format Research

Investigation tooling for understanding the proprietary ASE scanner image format
stored in `asescans.scanimage` (PostgreSQL `bytea`), with the goal of decoding
without the vendor `Ase.Image.dll`.

Not wired into any production code path. Scripts read-only against the DB
except for writing sample files to `./samples/`.

## Layout

```
tools/ase_format_research/
  README.md                       this file
  FINDINGS.md                     living format spec / hypothesis log
  01_extract_samples.py           pull N blobs from asescans -> samples/*.ase + .json
  02_characterize.py              Track A: black-box binary analysis
  03_diff_raw_vs_decoded.py       confirm format hypothesis against DLL output
  04_decompile_dll.py             Track B: decompile Ase*.dll with ilspycmd
  05_prototype_decoder.py         pure-python decoder prototype (written last)
  samples/                        extracted raw blobs + metadata sidecars
  decompiled/                     ILSpy output, indexed
```

## Prereqs

- Windows host with Python 3.11+
- `NICKSCAN_DB_PASSWORD` set in the environment (already set on the NSCIM host)
- Image-splitter venv active (or `pip install psycopg2-binary pillow numpy`)
- For Track B: `dotnet tool install -g ilspycmd` (optional; falls back to `dnlib`)

## Run order

```
python 01_extract_samples.py --count 10
python 02_characterize.py
python 04_decompile_dll.py
python 03_diff_raw_vs_decoded.py
# then, only if 02+04 converge on a spec:
python 05_prototype_decoder.py
```

All intermediate findings accumulate in `FINDINGS.md`.
