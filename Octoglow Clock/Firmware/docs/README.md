# Firmware build guide

Run these commands from `Octoglow Clock/Firmware/`:

```powershell
python 'builders/!buildall.py'
python 'builders/!buildall.py' --check
```

The first command regenerates `web_assets.h`, `demo/index.html`, and
`../THIRD-PARTY-NOTICES.md`. The second checks that all three are current
without changing files. The firmware sketch is `Octoglow.ino`; compiling and
flashing it is a separate Arduino step.

`builders/build_web.py` embeds the web pages, bundled fonts, and license
catalog into `web_assets.h`. `builders/build_demo.py` creates the standalone
browser demo from `demo/demo_mock.js`. `builders/webmin.py` and `builders/licenses.py` are their helper
modules. Each Python file has an English module docstring explaining its job.
`demo/clock_designs.html` is a separate clock-face preview and is not embedded
in the device firmware.

To download new font subsets when the web builder reports missing glyphs,
explicitly run `python 'builders/!buildall.py' --refresh-fonts`. This requires
internet and replaces files in `fonts/`; a normal build does not contact Google.
For debugging unminified pages, use `--no-minify`.

More detail: [web assets](WEB-ASSETS.md) and [performance](PERFORMANCE.md).
