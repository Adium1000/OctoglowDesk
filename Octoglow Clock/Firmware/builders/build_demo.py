# -*- coding: utf-8 -*-
"""Build a standalone browser demo from the real clock interface.

Inject Firmware/demo/demo_mock.js and the license catalog into Firmware/Octoglow/portal.html,
embed the local WOFF2 fonts, and write Firmware/demo/index.html. The resulting page
works without a clock or backend; the source page is never changed.

From Firmware/: python builders/build_demo.py [--check]
For the routine combined build, use python builders/!buildall.py instead.
"""
import base64
import os
import sys
import io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(HERE, 'Octoglow', 'portal.html')
DEMO = os.path.join(HERE, 'demo')
MOCK = os.path.join(DEMO, 'demo_mock.js')
OUT = os.path.join(DEMO, 'index.html')
import licenses  # noqa: E402

src = open(SRC, encoding='utf-8', newline='').read()
mock = open(MOCK, encoding='utf-8', newline='').read()

# 0. Licentele (GET /licenses): aceeasi lista ca pe ceas, scrisa in mock.
lic = licenses.licenses_json()
assert '</' not in lic, 'textul licentelor ar inchide <script>'
assert mock.count('/*__DEMO_LICENSES__*/null') == 1
mock = mock.replace('/*__DEMO_LICENSES__*/null', lic)

# 0b. Fonturile: pe ceas vin de la /fonts/, in demo stau in pagina (data: URI),
#     ca demo-ul sa ramana un singur fisier care nu cere nimic din afara.
for fname in ('google-sans.woff2', 'roboto.woff2'):
    ref = 'url(/fonts/' + fname + ')'
    assert src.count(ref) == 1, ref
    b64 = base64.b64encode(open(os.path.join(HERE, 'fonts', fname), 'rb').read()).decode('ascii')
    src = src.replace(ref, 'url(data:font/woff2;base64,' + b64 + ')')

# 1. Scriptul intra imediat dupa <title>, deci in <head>: ruleaza inaintea
#    oricarui script al paginii, care sunt toate la finalul documentului.
anchor = '<title>Octoglow Clock</title>'
assert src.count(anchor) == 1, 'ancora <title> nu e unica'
src = src.replace(anchor, anchor + '\n<script>\n' + mock + '</script>', 1)

# 2. Iesirile catre "/" (delogare, reset din fabrica, sesiune expirata) ar
#    parasi pagina si ar duce la un 404. In demo nu exista unde sa mearga.
n = src.count('window.location.href = `/`')
assert n > 0
src = src.replace('window.location.href = `/`', '__demoNav()')

if '--check' in sys.argv:
    if not os.path.exists(OUT) or open(OUT, encoding='utf-8', newline='').read() != src:
        print('demo/index.html is stale; run python builders/!buildall.py')
        sys.exit(1)
    print('demo/index.html is up to date.')
    sys.exit(0)

changed = not os.path.exists(OUT) or open(OUT, encoding='utf-8', newline='').read() != src
if changed:
    open(OUT, 'w', encoding='utf-8', newline='').write(src)
print('demo/index.html written' if changed else 'demo/index.html already up to date',
      '({:,} bytes)'.format(len(src.encode('utf-8'))))
print('  mock injectat          {:,} octeti'.format(len(mock.encode('utf-8'))))
print('  navigari neutralizate  {}'.format(n))
