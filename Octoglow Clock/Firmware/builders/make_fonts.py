# -*- coding: utf-8 -*-
"""Refresh the firmware's subsetted Google Sans and Roboto font files.

Collect the characters used by the web pages, request matching variable WOFF2
subsets from Google Fonts, and replace Firmware/fonts/*.woff2 and charset.txt.
This step needs internet and is intentionally optional: run it only when the
web builder reports missing glyphs or the page character set changes.

From Firmware/: python builders/!buildall.py --refresh-fonts
The bundled fonts are licensed under SIL Open Font License 1.1.
"""
import os
import re
import sys
import urllib.parse
import urllib.request

FW = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HERE = os.path.join(FW, "fonts")
PAGES = ["portal.html", "auth.html", "onboarding.html"]
FAMILIES = [("Google Sans", "google-sans.woff2"), ("Roboto", "roboto.woff2")]
# Google trimite woff2 doar unui browser modern.
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"

# Mereu incluse, chiar daca paginile nu le folosesc azi: ASCII, toate
# diacriticele romanesti si punctuatia obisnuita. Un caracter care lipseste
# (un "ł" intr-un nume de retea) nu se pierde - browserul il ia din fontul
# sistemului - asa ca nu platim flash pentru alfabete intregi.
BASE = [(0x20, 0x7E), (0x102, 0x103), (0xC2, 0xC2), (0xE2, 0xE2), (0xCE, 0xCE), (0xEE, 0xEE),
        (0x15E, 0x15F), (0x162, 0x163), (0x218, 0x21B)]
EXTRA = "«»ß©®™€°±×÷½–—‘’“”„…•·→←↑↓−"


def page_chars():
    """Caracterele non-ASCII din pagini, fara emoji si simboluri pe care nu le
    are niciun font de text (ele vin oricum din fonturile sistemului)."""
    out = set()
    for p in PAGES:
        for c in open(os.path.join(FW, p), encoding="utf-8").read():
            if 0x7F < ord(c) < 0x2C00:
                out.add(c)
    return out


def charset():
    s = {chr(c) for a, b in BASE for c in range(a, b + 1)} | set(EXTRA)
    return "".join(sorted(s | page_chars()))


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    return urllib.request.urlopen(req, timeout=30).read()


def main():
    text = charset()
    for family, fname in FAMILIES:
        q = urllib.parse.urlencode({"family": family + ":wght@400..500", "text": text})
        css = fetch("https://fonts.googleapis.com/css2?" + q).decode("utf-8")
        urls = sorted(set(re.findall(r"url\((https://fonts\.gstatic\.com/[^)]+)\)", css)))
        assert len(urls) == 1, (family, css[:400])
        assert "font-weight: 400 500" in css, (family, css[:400])
        blob = fetch(urls[0])
        assert blob[:4] == b"wOF2", family
        open(os.path.join(HERE, fname), "wb").write(blob)
        print(f"  {fname:18} {len(blob):>7,} bytes")
    open(os.path.join(HERE, "charset.txt"), "w", encoding="utf-8", newline="\n").write(text + "\n")
    print(f"  charset.txt        {len(text)} caractere")
    return 0


if __name__ == "__main__":
    sys.exit(main())
