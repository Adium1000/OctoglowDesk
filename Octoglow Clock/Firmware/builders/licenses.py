# -*- coding: utf-8 -*-
"""Read the firmware license catalog for both generated outputs.

Firmware/licenses/components.json and the matching license texts are the
single source of truth. licenses_json() feeds the clock's GET /licenses API
and the browser demo; notices_md() generates the repository's human-readable
THIRD-PARTY-NOTICES.md. Edit the data files, then run !buildall.py.
"""
import json
import os

HERE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "licenses")

# Grupurile, in ordinea din pagina. Titlurile din pagina sunt in portal.html
# (traduse ca restul interfetei); acestea sunt doar pentru THIRD-PARTY-NOTICES.md.
GROUPS = [("own", "Octoglow"), ("lib", "Libraries"), ("sys", "System (ESP-IDF)"), ("ui", "Fonts and icons")]


def load():
    data = json.load(open(os.path.join(HERE, "components.json"), encoding="utf-8"))
    used = []
    for it in data["items"]:
        assert it["g"] in dict(GROUPS), it["name"]
        for lic in it["lic"]:
            assert lic in data["names"], (it["name"], lic)
            if lic not in used:
                used.append(lic)
    data["texts"] = {lic: open(os.path.join(HERE, lic + ".txt"), encoding="utf-8").read() for lic in used}
    return data


def licenses_json():
    return json.dumps(load(), ensure_ascii=False, separators=(",", ":"))


def notices_md():
    d = load()
    out = [
        "# Third-party notices",
        "",
        "Octoglow is released under the [MIT License](../LICENSE). The firmware also contains the software below, each under",
        "its own license. The clock shows the same list, with every license text, under **About → Licenses**.",
        "",
        "This file is generated from `Firmware/licenses/` by `python Firmware/builders/!buildall.py`; edit `components.json` there, not this file.",
        "",
    ]
    for gid, title in GROUPS:
        items = [it for it in d["items"] if it["g"] == gid]
        if gid == "own" or not items:
            continue
        out += [f"## {title}", "", "| Component | Version | License | Copyright |", "|---|---|---|---|"]
        for it in items:
            out.append(f"| [{it['name']}]({it['url']}) | {it.get('ver', '')} | {', '.join(it['lic'])} | "
                       + "<br>".join(it["by"]) + " |")
        out.append("")
    out += ["ESP-IDF contains further open-source components, each under its own license; Espressif lists them at",
            "<https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/COPYRIGHT.html>.", "", "## License texts", ""]
    for lic, text in d["texts"].items():
        out += [f"### {d['names'][lic]} ({lic})", "", "```text", text.rstrip("\n"), "```", ""]
    return "\n".join(out)
