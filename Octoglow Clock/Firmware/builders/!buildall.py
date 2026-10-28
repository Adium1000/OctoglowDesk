#!/usr/bin/env python3
"""Build every routine generated firmware/web artifact in the right order.

Run build_web.py to regenerate web_assets.h and the third-party notices, then
build_demo.py to regenerate demo/index.html. Both steps use the same firmware
sources and license catalog. Use --check to verify all outputs without writing.

Font subset generation is intentionally opt-in with --refresh-fonts because it
downloads from Google Fonts and can replace bundled WOFF2 files. When selected,
it runs before the web and demo builders so both outputs use the new fonts.

From Firmware/: python builders/!buildall.py [--check] [--no-minify]
                 python builders/!buildall.py --refresh-fonts
"""

import argparse
from pathlib import Path
import subprocess
import sys


BUILDER_DIR = Path(__file__).resolve().parent
FIRMWARE_DIR = BUILDER_DIR.parent


def run(script: str, *options: str) -> None:
    command = [sys.executable, "-B", str(BUILDER_DIR / script), *options]
    print(f"\n==> {script}", flush=True)
    subprocess.run(command, cwd=FIRMWARE_DIR, check=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check generated files without writing")
    parser.add_argument("--no-minify", action="store_true", help="Embed the original HTML for debugging")
    parser.add_argument("--refresh-fonts", action="store_true", help="Download new WOFF2 subsets before building")
    args = parser.parse_args()
    if args.check and args.refresh_fonts:
        parser.error("--check cannot be combined with --refresh-fonts")

    try:
        if args.refresh_fonts:
            run("make_fonts.py")
        web_options = []
        if args.check:
            web_options.append("--check")
        if args.no_minify:
            web_options.append("--no-minify")
        run("build_web.py", *web_options)
        run("build_demo.py", *(["--check"] if args.check else []))
    except subprocess.CalledProcessError as error:
        return error.returncode
    print("\nAll firmware web outputs are up to date." if args.check else "\nFirmware web build complete.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
