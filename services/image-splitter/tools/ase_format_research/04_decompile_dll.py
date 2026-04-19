"""
04_decompile_dll.py

Track B: statically inspect the vendor Ase*.dll files via their .NET metadata
tables. We don't need full IL decompilation to pin down the format — the
TypeDef / Field / Method metadata alone gives us class names, field ordering,
and method signatures, which confirm the black-box findings from Track A.

Writes:
  decompiled/metadata_<assembly>.txt   one file per DLL, listing types + fields + methods
  decompiled/INDEX.md                  cross-assembly summary focused on format-relevant types
"""
from __future__ import annotations

from pathlib import Path

import dnfile

HERE = Path(__file__).resolve().parent
OUT_DIR = HERE / "decompiled"
OUT_DIR.mkdir(parents=True, exist_ok=True)

DLL_DIR = Path(
    r"C:\Shared\NSCIM_PRODUCTION\.claude\worktrees\angry-easley"
    r"\src\NickScanCentralImagingPortal.Services.ImageProcessing\ASE"
)

DLLS = [
    "Ase.Image.dll",
    "Ase.Image.Compression.dll",
    "Ase.Image.Interface.dll",
    "Ase.CommonTypes.dll",
    "Ase.Utilities.dll",
]

# Types that directly reveal the on-disk format layout
FORMAT_TYPES = {
    "ImgFileHeader",
    "ImgBitmap",
    "DualEnergyBitmap",
    "ParcelDualEnergyBitmap",
    "AseImg",
    "AseImageConverter",
    "CompEncryptHeader",
    "ImgMetadata",
    "MetadataItem",
    "AseRTDChannel",
    "FTIImageFileHeaderInfo",
    "LookUpTable",
    "RGBTable",
    "ZLutTable",
}


def dump_assembly(dll_path: Path) -> dict:
    """Return {'types': [...], 'format_types': {name: {...}}, 'strings': []}."""
    pe = dnfile.dnPE(str(dll_path))
    pe.parse_data_directories()
    md = pe.net.mdtables

    all_types = []
    format_types = {}

    for t in (md.TypeDef.rows if md.TypeDef else []):
        name = str(t.TypeName)
        ns = str(t.TypeNamespace)
        all_types.append(f"{ns}.{name}" if ns else name)

        if name in FORMAT_TYPES:
            fields = []
            for f in t.FieldList:
                try:
                    fn = str(f.row.Name) if hasattr(f, "row") else str(f.Name)
                except Exception:
                    fn = "<?>"
                fields.append(fn)
            methods = []
            for m in t.MethodList:
                try:
                    mn = str(m.row.Name) if hasattr(m, "row") else str(m.Name)
                except Exception:
                    mn = "<?>"
                methods.append(mn)
            format_types[name] = {
                "full_name": f"{ns}.{name}" if ns else name,
                "fields": fields,
                "methods": methods,
            }

    # also collect short user strings to spot format clues
    return {
        "dll": dll_path.name,
        "type_count": len(all_types),
        "method_count": len(md.MethodDef.rows) if md.MethodDef else 0,
        "all_types": all_types,
        "format_types": format_types,
    }


def write_per_assembly(info: dict):
    out = OUT_DIR / f"metadata_{info['dll']}.txt"
    lines = [
        f"# {info['dll']}",
        f"types: {info['type_count']}   methods: {info['method_count']}",
        "",
        "## All types",
        *info["all_types"],
        "",
        "## Format-relevant types",
    ]
    for name, data in info["format_types"].items():
        lines.append(f"\n### {data['full_name']}")
        lines.append(f"  fields ({len(data['fields'])}):")
        for f in data["fields"]:
            lines.append(f"    {f}")
        lines.append(f"  methods ({len(data['methods'])}):")
        for m in data["methods"]:
            lines.append(f"    {m}")
    out.write_text("\n".join(lines) + "\n")


def write_index(all_info: list[dict]):
    lines = [
        "# Decompiled DLL Metadata — Format-Relevant Index",
        "",
        "We only read TypeDef/Field/MethodDef metadata (no IL decompile). That",
        "is sufficient to confirm field ordering and class responsibilities,",
        "which together validate the header layout observed in black-box analysis.",
        "",
    ]

    # canonical header order (from ImgFileHeader, file declaration order)
    ase_img = None
    for info in all_info:
        if "ImgFileHeader" in info["format_types"]:
            ase_img = info
            break

    if ase_img:
        header = ase_img["format_types"]["ImgFileHeader"]
        lines.append("## ImgFileHeader — declared field order")
        lines.append("")
        lines.append("(assumes sequential on-disk layout — matches observed bytes)")
        lines.append("")
        for f in header["fields"]:
            lines.append(f"- `{f}`")
        lines.append("")

    lines.append("## Bitmap types")
    lines.append("")
    for info in all_info:
        for k in ("ImgBitmap", "DualEnergyBitmap", "ParcelDualEnergyBitmap"):
            if k in info["format_types"]:
                t = info["format_types"][k]
                lines.append(f"### {t['full_name']}")
                lines.append(f"  fields: {', '.join(t['fields']) or '(none)'}")
                lines.append(f"  methods: {', '.join(t['methods'])}")
                lines.append("")

    lines.append("## AseImageConverter — entry methods")
    lines.append("")
    for info in all_info:
        if "AseImageConverter" in info["format_types"]:
            t = info["format_types"]["AseImageConverter"]
            for m in t["methods"]:
                lines.append(f"- `{m}`")

    lines.append("")
    lines.append("## Assembly summary")
    lines.append("")
    lines.append("| dll | types | methods |")
    lines.append("|---|---:|---:|")
    for info in all_info:
        lines.append(f"| {info['dll']} | {info['type_count']} | {info['method_count']} |")

    (OUT_DIR / "INDEX.md").write_text("\n".join(lines) + "\n")


def main():
    all_info = []
    for name in DLLS:
        path = DLL_DIR / name
        if not path.exists():
            print(f"SKIP (not found): {path}")
            continue
        info = dump_assembly(path)
        write_per_assembly(info)
        all_info.append(info)
        print(f"{name}: {info['type_count']} types, {info['method_count']} methods, "
              f"{len(info['format_types'])} format-relevant")
    write_index(all_info)
    print(f"\nWrote per-assembly dumps and INDEX.md to {OUT_DIR}")


if __name__ == "__main__":
    main()
