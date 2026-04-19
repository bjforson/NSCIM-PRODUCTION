"""
02_characterize.py

Track A: parse every .ase sample in ./samples and emit a consistent binary
characterization report per file + an overall summary.

Produces:
  samples/<asescan_id>.report.json
  characterization_summary.md

The characterization is based on the format reverse-engineered in the first
pass (see FINDINGS.md). This script is the formal, reproducible version.
"""
from __future__ import annotations

import base64
import json
import math
import re
import struct
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
SAMPLES_DIR = HERE / "samples"

MAGIC = b"IM\x00\x00"
XML_PROLOG_UTF16LE = b"<\x00?\x00x\x00m\x00l\x00"  # "<?xml" as UTF-16 LE


def shannon_entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    total = len(data)
    return -sum((c / total) * math.log2(c / total) for c in counts.values())


def parse_header(data: bytes) -> dict:
    """
    Observed 16-byte header layout:
      [0..4)   magic "IM\0\0"
      [4..6)   width      u16 LE
      [6..8)   height     u16 LE
      [8..10)  channels   u16 LE  (2 for 544-wide single-view, 3 for 1632-wide tri-panel)
      [10..12) reserved0  u16 LE  (always 0 in 10/10 samples)
      [12..14) bpp        u16 LE  (always 2 -> 16-bit pixels)
      [14..16) reserved1  u16 LE  (always 0 in 10/10 samples)
    """
    if not data.startswith(MAGIC):
        return {"magic_ok": False, "first4": data[:4].hex()}
    w, h, ch, r0, bpp, r1 = struct.unpack_from("<HHHHHH", data, 4)
    return {
        "magic_ok": True,
        "width": w,
        "height": h,
        "channels": ch,
        "reserved0": r0,
        "bytes_per_pixel": bpp,
        "reserved1": r1,
        "header_len": 16,
    }


def find_xml_trailer(data: bytes) -> tuple[int, str | None]:
    idx = data.find(XML_PROLOG_UTF16LE)
    if idx < 0:
        return -1, None
    try:
        xml = data[idx:].decode("utf-16-le", errors="replace")
    except Exception:
        xml = None
    return idx, xml


def extract_container_info(xml: str | None) -> dict | None:
    if not xml:
        return None
    m = re.search(r"<Value>([A-Za-z0-9+/=]+)</Value>\s*<Name>ContainerInfo</Name>", xml)
    if not m:
        return None
    raw = base64.b64decode(m.group(1))
    return {"length": len(raw), "hex": raw.hex()}


def characterize(path: Path) -> dict:
    data = path.read_bytes()
    total = len(data)
    header = parse_header(data)
    report = {
        "file": path.name,
        "size": total,
        "magic_hex": data[:4].hex(),
        "header": header,
    }

    if not header.get("magic_ok"):
        return report

    w = header["width"]
    h = header["height"]
    bpp = header["bytes_per_pixel"]
    header_len = header["header_len"]
    pixels_end = header_len + w * h * bpp
    xml_start, xml = find_xml_trailer(data)

    report["pixels_offset"] = header_len
    report["pixels_end"] = pixels_end
    report["xml_offset"] = xml_start
    report["xml_length"] = total - xml_start if xml_start >= 0 else 0
    report["gap_between_pixels_and_xml"] = (
        xml_start - pixels_end if xml_start >= 0 else None
    )
    report["pixels_expected_bytes"] = w * h * bpp
    report["pixels_fits_exactly"] = pixels_end <= (xml_start if xml_start >= 0 else total)

    # Entropy of header, pixel region, gap, xml — helps spot compression or encryption
    pixels_bytes = data[header_len:pixels_end]
    gap_bytes = data[pixels_end:xml_start] if xml_start >= 0 else b""

    report["entropy_header"] = round(shannon_entropy(data[:header_len]), 3)
    report["entropy_pixels"] = round(shannon_entropy(pixels_bytes[:262144]), 3)
    report["entropy_gap"] = round(shannon_entropy(gap_bytes), 3) if gap_bytes else None
    report["gap_all_zero"] = gap_bytes == b"\x00" * len(gap_bytes) if gap_bytes else None
    report["gap_hex"] = gap_bytes.hex() if gap_bytes else None

    # Pixel-value stats (first pass)
    import numpy as np  # local import so scripts without pixel work stay lean
    arr = np.frombuffer(pixels_bytes, dtype="<u2").reshape(h, w)
    report["pixel_stats"] = {
        "min": int(arr.min()),
        "max": int(arr.max()),
        "mean": round(float(arr.mean()), 2),
        "std": round(float(arr.std()), 2),
    }
    # per-channel stats if channels > 1 and width divides evenly
    ch = header["channels"]
    if ch > 1 and w % ch == 0:
        panel_w = w // ch
        per_ch = []
        for i in range(ch):
            panel = arr[:, i * panel_w:(i + 1) * panel_w]
            per_ch.append(
                {
                    "panel_index": i,
                    "panel_width": panel_w,
                    "min": int(panel.min()),
                    "max": int(panel.max()),
                    "mean": round(float(panel.mean()), 2),
                    "std": round(float(panel.std()), 2),
                }
            )
        report["channel_stats"] = per_ch

    # XML metadata
    report["xml_preview"] = (xml[:400] if xml else None)
    report["container_info_binary"] = extract_container_info(xml)

    return report


def main():
    samples = sorted(SAMPLES_DIR.glob("*.ase"))
    if not samples:
        print(f"No .ase samples in {SAMPLES_DIR}. Run 01_extract_samples.py first.")
        return

    reports = []
    for p in samples:
        rep = characterize(p)
        reports.append(rep)
        out = p.with_suffix(".report.json")
        out.write_text(json.dumps(rep, indent=2))

    # summary markdown
    lines = ["# ASE Characterization Summary", ""]
    lines.append(f"- Samples analyzed: {len(reports)}")
    lines.append(f"- Samples with magic 'IM\\0\\0': "
                 f"{sum(1 for r in reports if r['header'].get('magic_ok'))}")

    widths = Counter(r["header"]["width"] for r in reports if r["header"].get("magic_ok"))
    channels = Counter(r["header"]["channels"] for r in reports if r["header"].get("magic_ok"))
    bpps = Counter(r["header"]["bytes_per_pixel"] for r in reports if r["header"].get("magic_ok"))
    lines.append(f"- Width distribution: {dict(widths)}")
    lines.append(f"- Channel distribution: {dict(channels)}")
    lines.append(f"- Bytes-per-pixel: {dict(bpps)}")
    lines.append("")
    lines.append("## Per-sample")
    lines.append("")
    lines.append("| file | size | w | h | ch | bpp | pix_entropy | gap | gap_zero | xml_len |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|---:|:-:|---:|")
    for r in reports:
        h = r["header"]
        lines.append(
            f"| {r['file'][:12]}… | {r['size']} | {h.get('width','?')} | {h.get('height','?')} | "
            f"{h.get('channels','?')} | {h.get('bytes_per_pixel','?')} | "
            f"{r.get('entropy_pixels','?')} | {r.get('gap_between_pixels_and_xml','?')} | "
            f"{'Y' if r.get('gap_all_zero') else 'N'} | {r.get('xml_length','?')} |"
        )

    (HERE / "characterization_summary.md").write_text("\n".join(lines) + "\n")
    print(f"Wrote {len(reports)} JSON reports and characterization_summary.md")


if __name__ == "__main__":
    main()
