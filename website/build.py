"""Builds the Agent Arcade website: wraps every page in pages/ in the shared layout and writes site/<name>.html.

    python website/build.py

No dependencies beyond Python 3. Each page starts with a small header:

    ---
    title: Pricing
    description: One sentence for search results.
    nav: pricing
    ---

Values in SITE can be used in any page as {{key}}. The product names live here, so a rename is one edit.
"""
import html
import json
import re
from pathlib import Path

HERE = Path(__file__).parent
PAGES = HERE / 'pages'
OUT = HERE / 'site'

SITE = {
    'name': 'Agent Arcade',
    'voice': 'Agent Arcade Voice',
    'remote': 'Agent Arcade Remote',
    'version': '1.3.1',
    'released': '15 Oct 2026',
    'zip_size': '63 MB',
    'sha256': '2f4d6ef0bcfa4ba3c9fcc3e0668ebceb0d6be852f0ee257e1108d6a89141ee75',
    'github': 'https://github.com/danular/AgentArcade',
    'download': 'https://github.com/danular/AgentArcade/releases/latest/download/AgentArcade-win-x64.zip',
    'releases': 'https://github.com/danular/AgentArcade/releases',
    'discuss': 'https://github.com/danular/AgentArcade/discussions',
    'issues': 'https://github.com/danular/AgentArcade/issues',
    'license': 'https://github.com/danular/AgentArcade/blob/main/LICENSE.md',
    'licensing': 'https://github.com/danular/AgentArcade/blob/main/LICENSING.md',
    'notices': 'https://github.com/danular/AgentArcade/blob/main/THIRD-PARTY-NOTICES.md',
    'year': '2026',
}

NAV = [('phone', 'phone.html', 'Phone'), ('voice', 'voice.html', 'Voice'), ('pricing', 'pricing.html', 'Pricing'),
       ('security', 'security.html', 'Security'), ('docs', 'docs.html', 'Docs'), ('changelog', 'changelog.html', 'Changelog')]

DISCLAIMER = (
    'Agent Arcade is an independent product from the Netherlands. It is not affiliated with, endorsed by or sponsored by '
    'Anthropic, OpenAI, Google, GitHub, Microsoft, Anysphere or Tailscale. It runs those companies’ official command-line '
    'tools on your PC, and each tool stays under its maker’s terms and your own account. Claude and Claude Code are '
    'trademarks of Anthropic, PBC. OpenAI, ChatGPT and Codex are trademarks of OpenAI. Google and Gemini are trademarks of '
    'Google LLC. GitHub and GitHub Copilot are trademarks of GitHub, Inc. Cursor is a trademark of Anysphere, Inc. '
    'Tailscale is a trademark of Tailscale Inc. Windows is a trademark of Microsoft Corporation. Android is a trademark of '
    'Google LLC. All other trademarks belong to their owners.'
)

DOWNLOAD_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11m0 0-4.5-4.5M12 15l4.5-4.5M5 19h14"/></svg>'

LAYOUT = '''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>%%title_full%%</title>
<meta name="description" content="%%description%%">
<meta name="theme-color" content="#0c0c0e">
<meta property="og:type" content="website">
<meta property="og:title" content="%%title_full%%">
<meta property="og:description" content="%%description%%">
<meta property="og:image" content="assets/img/og.png">
<meta name="twitter:card" content="summary_large_image">
<link rel="icon" type="image/png" sizes="32x32" href="assets/img/icon-32.png">
<link rel="icon" type="image/png" sizes="64x64" href="assets/img/icon-64.png">
<link rel="apple-touch-icon" href="assets/img/icon-180.png">
<link rel="preload" href="assets/fonts/source-serif-4-latin-400-normal.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="assets/css/site.css">
%%head_extra%%</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<header class="nav">
  <div class="wrap">
    <a class="brand" href="index.html"><img src="assets/img/icon-64.png" alt="" width="30" height="30">{{name}}</a>
    <nav class="nav-links" aria-label="Main">%%nav_links%%</nav>
    <div class="nav-cta">
      <a class="btn btn-ghost btn-sm" href="{{github}}">GitHub</a>
      <a class="btn btn-accent btn-sm" href="download.html">Download</a>
    </div>
    <button class="menu-btn" type="button" aria-label="Menu" aria-expanded="false">&#9776;</button>
  </div>
</header>
<main id="main">
%%body%%
</main>
<footer class="footer">
  <div class="wrap">
    <div class="foot-cols">
      <div class="stack">
        <a class="brand" href="index.html"><img src="assets/img/icon-64.png" alt="" width="30" height="30">{{name}}</a>
        <p class="muted small">All your agents. One window. Free for personal use; Pro for work.</p>
      </div>
      <div><h4>Product</h4><ul><li><a href="download.html">Download</a></li><li><a href="phone.html">Phone remote</a></li><li><a href="voice.html">Voice</a></li><li><a href="pricing.html">Pricing</a></li><li><a href="changelog.html">Changelog</a></li></ul></div>
      <div><h4>Resources</h4><ul><li><a href="docs.html">Docs</a></li><li><a href="security.html">Security</a></li><li><a href="pricing.html#need-pro">Do I need Pro?</a></li><li><a href="{{github}}">Source on GitHub</a></li></ul></div>
      <div><h4>Community</h4><ul><li><a href="{{discuss}}">Discussions</a></li><li><a href="{{issues}}">Report a bug</a></li><li><a href="{{releases}}">All releases</a></li></ul></div>
      <div><h4>Legal</h4><ul><li><a href="legal.html#license">License</a></li><li><a href="legal.html#privacy">Privacy</a></li><li><a href="legal.html#terms">Terms of sale</a></li><li><a href="legal.html#trademarks">Trademarks</a></li><li><a href="legal.html#imprint">Imprint</a></li></ul></div>
    </div>
    <p class="disclaimer">&copy; {{year}} {{name}}. %%disclaimer%%</p>
  </div>
</footer>
<div class="mbar"><a class="btn btn-accent" href="download.html">Download</a><a class="btn btn-ghost" href="pricing.html">Pricing</a></div>
%%scripts%%</body>
</html>
'''


def page(path):
    text = path.read_text(encoding='utf-8')
    m = re.match(r'---\n(.*?)\n---\n', text, re.S)
    meta = dict(line.split(':', 1) for line in m.group(1).splitlines() if ':' in line)
    meta = {k.strip(): v.strip() for k, v in meta.items()}
    return meta, text[m.end():]


def fill(text):
    text = text.replace('{{download_icon}}', DOWNLOAD_ICON)
    return re.sub(r'\{\{(\w+)\}\}', lambda m: SITE[m.group(1)], text)


def json_ld():
    data = {
        '@context': 'https://schema.org', '@type': 'SoftwareApplication', 'name': SITE['name'],
        'operatingSystem': 'Windows 11', 'applicationCategory': 'DeveloperApplication', 'softwareVersion': SITE['version'],
        'downloadUrl': SITE['download'],
        'offers': [
            {'@type': 'Offer', 'name': 'Personal', 'price': '0', 'priceCurrency': 'EUR'},
            {'@type': 'Offer', 'name': 'Pro', 'price': '25', 'priceCurrency': 'EUR',
             'priceSpecification': {'@type': 'UnitPriceSpecification', 'price': '25', 'priceCurrency': 'EUR',
                                    'valueAddedTaxIncluded': False, 'unitText': 'per user per month'}},
        ],
    }
    return '<script type="application/ld+json">' + json.dumps(data) + '</script>\n'


def build():
    for src in sorted(PAGES.glob('*.html')):
        meta, body = page(src)
        name = src.stem
        title = meta.get('title', SITE['name'])
        title_full = title if name == 'index' else f"{title} | {SITE['name']}"
        current = ' aria-current="page"'
        nav_links = ''.join(
            '<a href="' + href + '"' + (current if meta.get('nav') == key else '') + '>' + label
            + ('<span class="pro-pill">Pro</span>' if key == 'voice' else '') + '</a>'
            for key, href, label in NAV)
        scripts = ''
        if meta.get('cockpit') == 'yes':
            scripts += '<script src="assets/js/cockpit.js"></script>\n'
        scripts += '<script src="assets/js/site.js"></script>\n'
        head_extra = ''
        if meta.get('cockpit') == 'yes':
            head_extra += '<link rel="stylesheet" href="assets/css/cockpit.css">\n'
        if name == 'index':
            head_extra += json_ld()
        if meta.get('noindex') == 'yes':
            head_extra += '<meta name="robots" content="noindex">\n'
        parts = {'title_full': html.escape(fill(title_full)), 'description': html.escape(fill(meta.get('description', ''))),
                 'nav_links': nav_links, 'body': body.strip(), 'scripts': scripts, 'head_extra': head_extra,
                 'disclaimer': html.escape(DISCLAIMER)}
        out = re.sub(r'%%(\w+)%%', lambda m: parts[m.group(1)], LAYOUT)
        (OUT / f'{name}.html').write_text(fill(out), encoding='utf-8', newline='\n')
        print('wrote', f'site/{name}.html')


if __name__ == '__main__':
    build()
