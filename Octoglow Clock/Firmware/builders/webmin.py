# -*- coding: utf-8 -*-
"""Minify web pages and gzip them without changing their behavior.

Used by build_web.py. Remove safe-to-drop CSS/JavaScript comments and
whitespace while preserving strings, templates, regular expressions, HTML
content and automatic-semicolon-insertion boundaries. Optional esprima token
verification checks JavaScript equivalence. best_gzip() prefers 7-Zip or
zopfli when available, falling back to Python's zlib implementation.
The original HTML files are never rewritten by this module.
"""
import gzip
import os
import re
import shutil
import struct
import subprocess
import tempfile
import zlib

# ----------------------------------------------------------------------- CSS


def minify_css(src):
    out = []
    i, n = 0, len(src)
    space = False          # whitespace seen since the last thing written

    def last():
        return out[-1][-1] if out else ""

    def emit(tok):
        nonlocal space
        prev = last()
        if space and prev and prev not in "{};,:(" and tok[0] not in "{};,)":
            out.append(" ")
        space = False
        if tok == "}" and prev == ";":
            out[-1] = out[-1][:-1]
            if not out[-1]:
                out.pop()
        out.append(tok)

    while i < n:
        c = src[i]
        if c in " \t\r\n\f":
            space = True
            i += 1
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
        elif c in "\"'":
            j = i + 1
            while j < n and src[j] != c:
                j += 2 if src[j] == "\\" else 1
            emit(src[i:j + 1])
            i = j + 1
        elif src.startswith("url(", i) and i + 4 < n and src[i + 4] not in "\"'":
            j = src.find(")", i)
            emit(src[i:j + 1])
            i = j + 1
        elif src.startswith("--", i) and last() in "{;" and re.match(r"--[\w-]+\s*:", src[i:i + 80]):
            # A custom property: its value is kept as written (only the ends
            # are trimmed, which the browser does anyway).
            m = re.match(r"--[\w-]+\s*:", src[i:i + 80])
            j = i + m.end()
            depth = 0
            while j < n:
                d = src[j]
                if d in "\"'":
                    k = j + 1
                    while k < n and src[k] != d:
                        k += 2 if src[k] == "\\" else 1
                    j = k
                elif d in "([{":
                    depth += 1
                elif d in ")]}":
                    if depth == 0:
                        break
                    depth -= 1
                elif d == ";" and depth == 0:
                    break
                j += 1
            name = src[i:i + m.end()].replace(" ", "").replace("\t", "").replace("\n", "").replace("\r", "")
            value = src[i + m.end():j].strip()
            assert "/*" not in value, "comentariu intr-o proprietate --custom"
            emit(name + value)
            i = j
        else:
            j = i + 1
            while j < n and src[j] not in " \t\r\n\f\"'{};,:()/" and not src.startswith("--", j):
                j += 1
            if c in "{};,:()/":
                j = i + 1
            emit(src[i:j])
            i = j
    return "".join(out)


# ------------------------------------------------------------------------ JS

_WS = " \t\r\n\f\v ﻿  "
_REGEX_AFTER_WORD = {"return", "typeof", "case", "do", "else", "in", "of", "void", "delete", "throw", "new",
                     "instanceof", "yield", "await"}


def _is_word(ch):
    return ch.isalnum() or ch in "_$" or ord(ch) > 0x7F


def _skip_string(src, i):
    q = src[i]
    j = i + 1
    while src[j] != q:
        j += 2 if src[j] == "\\" else 1
    return j + 1


def _skip_template(src, i):
    """From the opening backtick to just after the closing one, substitutions included."""
    j = i + 1
    while True:
        c = src[j]
        if c == "\\":
            j += 2
        elif c == "`":
            return j + 1
        elif c == "$" and src[j + 1] == "{":
            j = _skip_code_until_brace(src, j + 2)
        else:
            j += 1


def _skip_code_until_brace(src, j):
    """Inside ${ ... }: skip code up to the matching }."""
    depth = 0
    while True:
        c = src[j]
        if c in "\"'":
            j = _skip_string(src, j)
        elif c == "`":
            j = _skip_template(src, j)
        elif src.startswith("//", j):
            j = src.index("\n", j)
        elif src.startswith("/*", j):
            j = src.index("*/", j) + 2
        elif c == "{":
            depth += 1
            j += 1
        elif c == "}":
            if depth == 0:
                return j + 1
            depth -= 1
            j += 1
        else:
            j += 1


def _skip_regex(src, i):
    j = i + 1
    in_class = False
    while True:
        c = src[j]
        if c == "\\":
            j += 2
            continue
        if c == "\n":
            raise ValueError("regex neterminat la %d: %r" % (i, src[i:i + 60]))
        if c == "[":
            in_class = True
        elif c == "]":
            in_class = False
        elif c == "/" and not in_class:
            j += 1
            break
        j += 1
    while j < len(src) and src[j].isalpha():
        j += 1
    return j


def js_tokens(src):
    """(kind, text) with kind in ws, nl, comment, str, tpl, re, word, punct."""
    i, n = 0, len(src)
    prev_kind, prev_text = None, ""
    while i < n:
        c = src[i]
        if c in _WS:
            j = i
            while j < n and src[j] in _WS:
                j += 1
            text = src[i:j]
            yield ("nl" if ("\n" in text or " " in text or " " in text) else "ws", text)
            i = j
            continue
        if src.startswith("//", i):
            j = src.find("\n", i)
            j = n if j < 0 else j
            yield ("comment", src[i:j])
            i = j
            continue
        if src.startswith("/*", i):
            j = src.index("*/", i) + 2
            yield ("comment", src[i:j])
            i = j
            continue
        if c in "\"'":
            j = _skip_string(src, i)
            kind = "str"
        elif c == "`":
            j = _skip_template(src, i)
            kind = "tpl"
        elif c == "/":
            regex_ok = (prev_kind is None or (prev_kind == "punct" and prev_text not in (")", "]", "}"))
                        or (prev_kind == "word" and prev_text in _REGEX_AFTER_WORD))
            if regex_ok:
                j = _skip_regex(src, i)
                kind = "re"
            else:
                j = i + 1
                kind = "punct"
        elif _is_word(c):
            j = i + 1
            while j < n and _is_word(src[j]):
                j += 1
            kind = "word"
        else:
            # An HTML comment opener in code would swallow the rest of the line.
            assert not src.startswith("<!--", i), "'<!--' in codul JS, la %d" % i
            j = i + 1
            kind = "punct"
        prev_kind, prev_text = kind, src[i:j]
        yield (kind, src[i:j])
        i = j


def _needs_space(a, b):
    """Would the last character of a and the first of b read differently when joined?"""
    if _is_word(a) and _is_word(b):
        return True
    if a in "+-" and b in "+-":
        return True
    if a == "/" or b == "/" or a == "." or b == ".":
        return True
    if (a == "<" and b == "!") or (a == "-" and b == ">"):
        return True
    return False


class Unsafe(Exception):
    """The script could not be read with certainty; build with --no-minify."""


def minify_js(src):
    out = []
    gap = None             # None, "ws" or "nl" between the last token written and the next
    last = ""              # last character written
    # If a string or a regex were ever misread, code and text would swap roles
    # and the brackets seen as code would stop pairing up. They must pair.
    stack = []
    pairs = {")": "(", "]": "[", "}": "{"}
    for kind, text in js_tokens(src):
        if kind == "punct":
            if text in "([{":
                stack.append(text)
            elif text in pairs:
                if not stack or stack.pop() != pairs[text]:
                    raise Unsafe("paranteze nepotrivite langa: %r" % "".join(out[-12:]))
        if kind == "ws":
            gap = gap or "ws"
            continue
        if kind == "nl":
            gap = "nl"
            continue
        if kind == "comment":
            # A comment that spans lines counts as a line break; a // comment is
            # followed by one anyway.
            if "\n" in text:
                gap = "nl"
            else:
                gap = gap or "ws"
            continue
        first = text[0]
        if out and gap == "nl":
            if last in ";{,([" or first == "}":
                if _needs_space(last, first):
                    out.append(" ")
            else:
                out.append("\n")
        elif out and gap == "ws" and _needs_space(last, first):
            out.append(" ")
        out.append(text)
        last = text[-1]
        gap = None
    if stack:
        raise Unsafe("paranteze ramase deschise: %s" % "".join(stack[-8:]))
    return "".join(out)


def verify_js(original, minified):
    """Same program? Parsed by esprima (a separate parser) both ways: the token
    lists must match, and a line break may be missing only where it cannot mean
    anything. Returns the number of tokens, or None when esprima is not there."""
    try:
        import esprima
    except ImportError:
        return None
    import sys
    sys.setrecursionlimit(max(sys.getrecursionlimit(), 20000))   # long a + b + c + ... chains
    a = esprima.parseScript(original, tokens=True, loc=True).tokens
    b = esprima.parseScript(minified, tokens=True, loc=True).tokens
    assert len(a) == len(b), ("numar diferit de tokeni", len(a), len(b))
    for k in range(len(a)):
        assert a[k].type == b[k].type and a[k].value == b[k].value, ("token diferit", k, a[k].value, b[k].value)
        if k:
            was = a[k].loc.start.line != a[k - 1].loc.end.line
            now = b[k].loc.start.line != b[k - 1].loc.end.line
            if was != now:
                assert was and (a[k - 1].value[-1] in ";{,([" or a[k].value == "}"), \
                    ("rand nou schimbat", a[k - 1].value, a[k].value)
    return len(a)


_SCRIPT = re.compile(r"<script[^>]*>(.*?)</script\s*>", re.I | re.S)


def verify_page(original, minified):
    """verify_js() over every <script> of a page: the token count, or None
    without esprima."""
    a = _SCRIPT.findall(original)
    b = _SCRIPT.findall(minified)
    assert len(a) == len(b), "numar diferit de scripturi"
    total = 0
    for x, y in zip(a, b):
        n = verify_js(x, y)
        if n is None:
            return None
        total += n
    return total


# ---------------------------------------------------------------------- HTML

_OPEN = re.compile(r"<(style|script|pre|textarea)\b([^>]*)>|<!--", re.I)


def _minify_markup(text, state):
    """Strip the indentation of lines that start in text or between attributes.
    state: 't' text, 'g' inside a tag, '"' or "'" inside a quoted value,
    'u' inside an unquoted value, 'e' after = (before the value)."""
    out = []
    lines = text.split("\n")
    for k, line in enumerate(lines):
        start = state
        i, n = 0, len(line)
        while i < n:
            c = line[i]
            if state == "t":
                if c == "<" and i + 1 < n and (line[i + 1].isalpha() or line[i + 1] in "/!"):
                    state = "g"
            elif state == "g":
                if c == ">":
                    state = "t"
                elif c == "=":
                    state = "e"
            elif state == "e":
                if c in "\"'":
                    state = c
                elif c == ">":
                    state = "t"
                elif c not in " \t":
                    state = "u"
            elif state == "u":
                if c in " \t":
                    state = "g"
                elif c == ">":
                    state = "t"
            elif c == state:       # closing quote
                state = "g"
            i += 1
        if state == "u":           # an unquoted value ends with the line
            state = "g"
        # Only whitespace that touches a line break goes: the first piece
        # continues the line of the element before it, the last one runs up to
        # the element after it.
        if k > 0 and start in "tg":
            line = line.lstrip(" \t")
        if k < len(lines) - 1 and state in "tg":
            line = line.rstrip(" \t\r")
        out.append((line, start))
    # Drop lines left empty, but never the first or the last piece: those carry
    # the text right next to the neighbouring <style>/<script>.
    kept = [ln for k, (ln, st) in enumerate(out) if ln or st not in "tg" or k == 0 or k == len(out) - 1]
    return "\n".join(kept), state


def minify_page(html):
    out = []
    pos = 0
    state = "t"
    while True:
        m = _OPEN.search(html, pos)
        if not m:
            chunk, state = _minify_markup(html[pos:], state)
            out.append(chunk)
            break
        chunk, state = _minify_markup(html[pos:m.start()], state)
        assert state == "t", "eticheta speciala gasita in interiorul altei etichete"
        out.append(chunk)
        if m.group(0) == "<!--":
            end = html.index("-->", m.start()) + 3
            out.append(html[m.start():end])          # comments stay: they split text nodes
            pos = end
            continue
        tag = m.group(1).lower()
        close = re.compile(r"</" + tag + r"\s*>", re.I).search(html, m.end())
        body = html[m.end():close.start()]
        attrs = m.group(2)
        if tag == "style":
            body = minify_css(body)
        elif tag == "script" and "src" not in attrs.lower() and not re.search(r"type\s*=\s*[\"']?(?!text/javascript|module)", attrs, re.I):
            body = minify_js(body)
        out.append(m.group(0) + body + close.group(0))
        pos = close.end()
    return "".join(out)


# ---------------------------------------------------------------------- gzip


def _wrap_gzip(deflate, raw):
    return (b"\x1f\x8b\x08\x00\x00\x00\x00\x00\x02\xff" + deflate +
            struct.pack("<II", zlib.crc32(raw) & 0xFFFFFFFF, len(raw) & 0xFFFFFFFF))


def _strip_gzip(blob):
    """The raw deflate stream inside a gzip file."""
    assert blob[:3] == b"\x1f\x8b\x08"
    flg = blob[3]
    i = 10
    if flg & 4:
        i += 2 + struct.unpack("<H", blob[i:i + 2])[0]
    if flg & 8:
        i = blob.index(b"\0", i) + 1
    if flg & 16:
        i = blob.index(b"\0", i) + 1
    if flg & 2:
        i += 2
    return blob[i:-8]


def _find_7z():
    for p in (shutil.which("7z"), shutil.which("7za"),
              os.path.join(os.environ.get("ProgramFiles", ""), "7-Zip", "7z.exe"),
              os.path.join(os.environ.get("ProgramFiles(x86)", ""), "7-Zip", "7z.exe")):
        if p and os.path.isfile(p):
            return p
    return None


def best_gzip(raw):
    """(gzip bytes, name of the compressor that won). Same bytes for the same
    input and tool, with no time stamp, so an unchanged page gives an unchanged
    web_assets.h."""
    best, how = gzip.compress(raw, compresslevel=9, mtime=0), "zlib"
    candidates = []
    try:
        import zopfli.gzip as zg
        candidates.append(("zopfli", lambda: _strip_gzip(zg.compress(raw, numiterations=15))))
    except ImportError:
        pass
    sevenz = _find_7z()
    if sevenz:
        def run7z():
            d = tempfile.mkdtemp(prefix="octoglow-gz-")
            try:
                src = os.path.join(d, "a")
                dst = os.path.join(d, "a.gz")
                open(src, "wb").write(raw)
                subprocess.run([sevenz, "a", "-tgzip", "-mx=9", "-mfb=258", "-mpass=15", dst, src],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
                return _strip_gzip(open(dst, "rb").read())
            finally:
                shutil.rmtree(d, ignore_errors=True)
        candidates.append(("7-Zip", run7z))
    for name, fn in candidates:
        try:
            blob = _wrap_gzip(fn(), raw)
        except Exception:
            continue
        if gzip.decompress(blob) == raw and len(blob) < len(best):
            best, how = blob, name
    return best, how
