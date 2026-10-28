#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build the web payload embedded in the ESP32 firmware.

Read Firmware/Octoglow/portal.html, auth.html and onboarding.html, safely minify and
gzip them, and write Firmware/Octoglow/web_assets.h. Also embed the local WOFF2 fonts
and the JSON license catalog, then generate the clock's THIRD-PARTY-NOTICES.md.
The HTML sources are never modified. Run this after changing the pages, fonts
or licenses, and before compiling Octoglow.ino.

From Firmware/: python builders/build_web.py [--check | --no-minify]
For the routine combined build, use python builders/!buildall.py instead.
"""
import hashlib
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKETCH = os.path.join(HERE, "Octoglow")
OUT = os.path.join(SKETCH, "web_assets.h")
NOTICES = os.path.join(os.path.dirname(HERE), "THIRD-PARTY-NOTICES.md")
import licenses  # noqa: E402
import webmin  # noqa: E402

MINIFY = "--no-minify" not in sys.argv


def shrink(fname, raw):
    """The page as it goes into flash, and how it got there: minified, and
    checked when esprima is here. (The check is not written into web_assets.h,
    so the header is the same with or without esprima.)"""
    if not MINIFY:
        return raw, "neminificat"
    src = raw.decode("utf-8")
    try:
        out = webmin.minify_page(src)
        n = webmin.verify_page(src, out)
    except Exception as e:  # anything unsure: the page goes in as written
        print(f"ATENTIE: {fname} nu a putut fi minificat in siguranta ({type(e).__name__}: {e}); intra neminificat.")
        return raw, "neminificat"
    return out.encode("utf-8"), ("verificat" if n is not None else "neverificat")

# (fisier sursa, numele simbolului in C++)
ASSETS = [
    ("portal.html", "PORTAL_HTML_GZ"),
    ("auth.html", "AUTH_SHELL_GZ"),
    ("onboarding.html", "ONBOARDING_HTML_GZ"),
]

# (fisier din fonts/, adresa pe ceas, numele simbolului in C++)
FONTS = [
    ("google-sans.woff2", "/fonts/google-sans.woff2", "GSANS_WOFF2"),
    ("roboto.woff2", "/fonts/roboto.woff2", "ROBOTO_WOFF2"),
]


def sha16(blob):
    return hashlib.sha256(blob).hexdigest()[:16]


def emit_array(name, blob):
    lines = [f"const uint8_t {name}[] PROGMEM = {{"]
    for i in range(0, len(blob), 16):
        chunk = blob[i:i + 16]
        lines.append("  " + " ".join(f"0x{b:02x}," for b in chunk))
    lines.append("};")
    lines.append(f"const uint32_t {name}_LEN = {len(blob)};")
    return "\n".join(lines)


def missing_glyphs():
    """Caracterele din pagini pe care fonturile nu le au (vezi fonts/make_fonts.py)."""
    have = set(open(os.path.join(HERE, "fonts", "charset.txt"), encoding="utf-8").read())
    used = set()
    for fname, _ in ASSETS:
        used |= {c for c in open(os.path.join(SKETCH, fname), encoding="utf-8").read() if 0x7F < ord(c) < 0x2C00}
    return "".join(sorted(used - have))


def build():
    parts = [
        "// FISIER GENERAT - nu edita manual.",
        "// Sursa: " + ", ".join(a for a, _ in ASSETS) + ", fonts/, licenses/",
        "// Regenerate with: python builders/!buildall.py",
        "#pragma once",
        "#include <pgmspace.h>",
        "#include <stdint.h>",
        "",
    ]
    report = []

    fonts = []
    for fname, url, sym in FONTS:
        blob = open(os.path.join(HERE, "fonts", fname), "rb").read()
        fonts.append((fname, url, sym, blob, sha16(blob)))

    for fname, sym in ASSETS:
        raw = open(os.path.join(SKETCH, fname), "rb").read()
        for _, url, _, _, fsha in fonts:
            raw = raw.replace(url.encode() + b")", f"{url}?v={fsha[:8]})".encode())
        size = len(raw)
        raw, state = shrink(fname, raw)
        # The page's ETag: taken after the font hashes are in, so new fonts
        # also make the browser fetch the page again.
        src_sha = sha16(raw)
        # No time stamp in the gzip header, so an unchanged page produces an
        # identical header and the sketch does not get recompiled for no reason.
        blob, tool = webmin.best_gzip(raw)
        parts.append(f"// {fname}: {size} -> {len(raw)} -> {len(blob)} bytes ({tool})")
        parts.append(f"#define {sym}_SRC_SHA \"{src_sha}\"")
        parts.append(emit_array(sym, blob))
        parts.append("")
        report.append((fname, size, len(blob), f"{state}, {tool}"))

    lic = licenses.licenses_json().encode("utf-8")
    blob, tool = webmin.best_gzip(lic)
    parts.append(f"// licenses/ -> GET /licenses: {len(lic)} -> {len(blob)} bytes ({tool})")
    parts.append(f"#define LICENSES_JSON_GZ_SRC_SHA \"{sha16(lic)}\"")
    parts.append(emit_array("LICENSES_JSON_GZ", blob))
    parts.append("")
    report.append(("/licenses", len(lic), len(blob), tool))

    for fname, url, sym, blob, fsha in fonts:
        parts.append(f"// fonts/{fname} -> {url}: {len(blob)} bytes (woff2, already compressed)")
        parts.append(f"#define {sym}_SRC_SHA \"{fsha}\"")
        parts.append(emit_array(sym, blob))
        parts.append("")
        report.append((fname, len(blob), len(blob), "woff2"))
    return "\n".join(parts) + "\n", report


def write_if_changed(path, text):
    current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
    if current == text:
        return False
    open(path, "w", encoding="utf-8", newline="\n").write(text)
    return True


def main():
    text, report = build()
    notices = licenses.notices_md()
    check = "--check" in sys.argv

    if check:
        stale = [p for p, t in ((OUT, text), (NOTICES, notices))
                 if not os.path.exists(p) or open(p, encoding="utf-8").read() != t]
        if not stale:
            print("web_assets.h si THIRD-PARTY-NOTICES.md sunt la zi.")
            return 0
        print(", ".join(os.path.basename(p) for p in stale) + " NU sunt la zi - ruleaza: python builders/!buildall.py")
        return 1

    print("web_assets.h regenerat." if write_if_changed(OUT, text) else "web_assets.h era deja la zi, nimic de facut.")
    if write_if_changed(NOTICES, notices):
        print("THIRD-PARTY-NOTICES.md regenerat.")

    total_raw = sum(r[1] for r in report)
    total_gz = sum(r[2] for r in report)
    for fname, raw, gz, how in report:
        print(f"  {fname:17} {raw:>8,} -> {gz:>7,} ({gz / raw * 100:.1f}%)  {how}")
    print(f"  {'TOTAL':17} {total_raw:>8,} -> {total_gz:>7,} in flash")
    if any("neverificat" in r[3] for r in report):
        print("  (scripturile minificate nu au fost verificate: pentru verificare la fiecare rulare, pip install esprima)")

    miss = missing_glyphs()
    if miss:
        print(f"\nATENTIE: fonturile nu au {len(miss)} caractere folosite in pagini: {miss}\n"
              "  Se vad cu fontul sistemului. Ruleaza python builders/!buildall.py --refresh-fonts.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
