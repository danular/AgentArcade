"""Builds the Agent Arcade website: wraps every page in pages/ in the shared layout and writes site/<name>.html.

    python website/build.py

No dependencies beyond Python 3. Each page starts with a small header:

    ---
    title: Pricing
    description: One sentence for search results.
    nav: pricing
    ---

Values in SITE can be used in any page as {{key}}. The product names live here, so a rename is one edit.
SITE['base'] is the public address (GitHub Pages, deployed by .github/workflows/pages.yml); canonical and og: URLs,
sitemap.xml and the 404 page's links are built from it.

The download facts ({{version}}, {{released}}, {{zip_size}}, {{sha256}}, {{apk_size}}, {{apk_sha256}} and the links
{{download}} and {{apk}}) come from the newest GitHub release, read from the releases API, so they can't go stale:
pages.yml rebuilds the site when a release is published. Only the API's JSON is read; a release asset is never
fetched (GitHub counts every download). GITHUB_TOKEN, when set, is sent to stay clear of the API's rate limit. When the
API can't be reached the build warns and uses FALLBACK; in CI (CI=true) it stops instead.
"""
import html
import json
import os
import re
import sys
import urllib.request
from datetime import datetime
from pathlib import Path
from urllib.parse import quote, urlparse

HERE = Path(__file__).parent
PAGES = HERE / 'pages'
OUT = HERE / 'site'

REPO = 'danular/AgentArcade'
RELEASE_API = f'https://api.github.com/repos/{REPO}/releases/latest'
# Every release carries exactly these two names (release.ps1).
ZIP_ASSET = 'AgentArcade-win-x64.zip'
APK_ASSET = 'AgentArcadeRemote.apk'
# The Android app's application id (applicationId in the Android project), for the Obtainium link.
APK_ID = 'nl.danular.remote'

# Only for a local build that can't reach the releases API: the 1.3.2 release.
FALLBACK = {
    'tag': 'v1.3.2',
    'released': '15 Oct 2026',
    'zip_size': '63 MB',
    'sha256': 'f81efd2f44cf021f1d2a4324a76988b0dd9fb4dac21013826ff611137ada2c22',
    'apk_size': '3.8 MB',
    'apk_sha256': '846153b823f219cf54def52f8542fbcaf87d92dbf9fd79988c9c7d464906df18',
}

SITE = {
    'name': 'Agent Arcade',
    'base': 'https://danular.github.io/AgentArcade/',
    'voice': 'Agent Arcade Voice',
    'remote': 'Agent Arcade Remote',
    'github': f'https://github.com/{REPO}',
    # The releases API has no field for the APK's own version, so it is typed here; change it with a new APK.
    'apk_version': '1.1.1',
    # SHA-256 of the APK's signing certificate. It is the same for every version: Android refuses an update signed
    # with another key.
    'apk_cert': '01e68ee0fb88710fb42780eae6e5532e9982d21a7546e9a60e8114c064086dce',
    'releases': 'https://github.com/danular/AgentArcade/releases',
    'discuss': 'https://github.com/danular/AgentArcade/discussions',
    # The business behind Agent Arcade, as in the public KvK register (imprint on legal.html). Add the btw-id when known.
    'company': '3Dmatters',
    'kvk': '93618085',
    'branch_number': '000059146834',
    'contact': 'daanschuurmans25@hotmail.com',
    'issues': 'https://github.com/danular/AgentArcade/issues',
    'license': 'https://github.com/danular/AgentArcade/blob/main/LICENSE.md',
    'licensing': 'https://github.com/danular/AgentArcade/blob/main/LICENSING.md',
    'notices': 'https://github.com/danular/AgentArcade/blob/main/THIRD-PARTY-NOTICES.md',
    'year': '2026',
}


def obtainium_link():
    """The 'Add to Obtainium' link: obtainium://app/ and the URL-encoded app config (wiki.obtainium.imranr.dev/deep_links).
    Obtainium then follows this repo's GitHub releases and installs only the APK with the stable name; the filter
    skips the Windows zip and any other APK. additionalSettings is itself a JSON string, as in Obtainium's exports.
    Version detection is off because a release's tag is the PC app's version, not the APK's: Obtainium could never
    match it to the installed app and would keep offering the same APK."""
    settings = {'apkFilterRegEx': SITE['apk_filter'], 'versionDetection': False}
    config = {'id': APK_ID, 'url': SITE['github'], 'author': REPO.split('/')[0], 'name': SITE['remote'],
              'additionalSettings': json.dumps(settings, separators=(',', ':'))}
    return 'obtainium://app/' + quote(json.dumps(config, separators=(',', ':')), safe='')


# Obtainium's APK filter (a regular expression on the release file's name): only the stable APK name.
SITE['apk_filter'] = '^' + re.escape(APK_ASSET) + '$'
SITE['obtainium'] = obtainium_link()

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
<meta property="og:url" content="%%url%%">
<meta property="og:image" content="{{base}}assets/img/og.png">
<meta name="twitter:card" content="summary_large_image">
<link rel="icon" type="image/png" sizes="32x32" href="assets/img/icon-32.png">
<link rel="icon" type="image/png" sizes="64x64" href="assets/img/icon-64.png">
<link rel="apple-touch-icon" href="assets/img/icon-180.png">
<link rel="preload" href="assets/fonts/source-serif-4-latin-400-normal.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="assets/css/site.css">
%%canonical%%%%head_extra%%</head>
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
      <div><h2>Product</h2><ul><li><a href="download.html">Download</a></li><li><a href="phone.html">Phone remote</a></li><li><a href="voice.html">Voice</a></li><li><a href="pricing.html">Pricing</a></li><li><a href="changelog.html">Changelog</a></li></ul></div>
      <div><h2>Resources</h2><ul><li><a href="docs.html">Docs</a></li><li><a href="security.html">Security</a></li><li><a href="pricing.html#need-pro">Do I need Pro?</a></li><li><a href="{{github}}">Source on GitHub</a></li></ul></div>
      <div><h2>Community</h2><ul><li><a href="{{discuss}}">Discussions</a></li><li><a href="{{issues}}">Report a bug</a></li><li><a href="{{releases}}">All releases</a></li></ul></div>
      <div><h2>Legal</h2><ul><li><a href="legal.html#license">License</a></li><li><a href="legal.html#privacy">Privacy</a></li><li><a href="legal.html#terms">Terms of sale</a></li><li><a href="legal.html#trademarks">Trademarks</a></li><li><a href="legal.html#imprint">Imprint</a></li></ul></div>
    </div>
    <p class="disclaimer">&copy; {{year}} {{name}}. %%disclaimer%%</p>
  </div>
</footer>
<div class="mbar"><a class="btn btn-accent" href="download.html">Download</a><a class="btn btn-ghost" href="pricing.html">Pricing</a></div>
%%scripts%%</body>
</html>
'''


MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']


def megabytes(size):
    mb = size / 1e6
    return f'{mb:.0f} MB' if mb >= 10 else f'{mb:.1f} MB'


def latest_release():
    """The newest release's download facts, from the releases API's JSON. Raises when an asset or its digest is missing."""
    headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28',
               'User-Agent': 'agent-arcade-website-build'}
    if os.environ.get('GITHUB_TOKEN'):
        headers['Authorization'] = 'Bearer ' + os.environ['GITHUB_TOKEN']
    with urllib.request.urlopen(urllib.request.Request(RELEASE_API, headers=headers), timeout=20) as response:
        release = json.load(response)
    tag = release['tag_name']
    if not re.fullmatch(r'v?\d+(\.\d+){1,3}', tag):
        raise ValueError(f'unexpected tag {tag!r}')
    published = datetime.fromisoformat(release['published_at'].replace('Z', '+00:00'))

    def asset(name):
        found = next((item for item in release.get('assets', []) if item.get('name') == name), None)
        if found is None:
            raise ValueError(f'release {tag} has no {name}')
        digest = found.get('digest') or ''
        if not re.fullmatch(r'sha256:[0-9a-f]{64}', digest):
            raise ValueError(f'{name} in release {tag} has no SHA-256 digest')
        return digest.removeprefix('sha256:'), megabytes(found['size'])

    sha256, zip_size = asset(ZIP_ASSET)
    apk_sha256, apk_size = asset(APK_ASSET)
    return {'tag': tag, 'released': f'{published.day} {MONTHS[published.month - 1]} {published.year}',
            'zip_size': zip_size, 'sha256': sha256, 'apk_size': apk_size, 'apk_sha256': apk_sha256}


def download_facts():
    try:
        facts = latest_release()
        print('release', facts['tag'], 'from', RELEASE_API)
    except (OSError, ValueError, KeyError, TypeError) as error:
        if os.environ.get('CI') == 'true':
            sys.exit(f'error: could not read the latest release from {RELEASE_API}: {error}')
        print(f'warning: could not read the latest release from {RELEASE_API} ({error}); '
              f'the site shows the {FALLBACK["tag"]} details from build.py instead', file=sys.stderr)
        facts = dict(FALLBACK)
    tag = facts['tag']
    facts['version'] = tag.removeprefix('v')
    # Versioned links, so the file always matches the version and SHA-256 shown next to it.
    facts['download'] = f'{SITE["github"]}/releases/download/{tag}/{ZIP_ASSET}'
    facts['apk'] = f'{SITE["github"]}/releases/download/{tag}/{APK_ASSET}'
    return facts


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
        # Pro can't be bought yet, so search engines get no Pro offer until checkout opens. Then add:
        # {'@type': 'Offer', 'name': 'Pro', 'price': '25', 'priceCurrency': 'EUR', 'priceSpecification':
        #  {'@type': 'UnitPriceSpecification', 'price': '25', 'priceCurrency': 'EUR', 'valueAddedTaxIncluded': False,
        #   'unitText': 'per user per month'}}
        'offers': [
            {'@type': 'Offer', 'name': 'Personal', 'price': '0', 'priceCurrency': 'EUR'},
        ],
    }
    return '<script type="application/ld+json">' + json.dumps(data) + '</script>\n'


def rooted(text):
    """404.html is served at any missing path, so its own links and assets must not be relative."""
    root = urlparse(SITE['base']).path
    return re.sub(r'(href|src)="(?![a-z]+:|/|#|\{\{)', lambda m: f'{m.group(1)}="{root}', text)


def build():
    urls = []
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
        url = SITE['base'] + ('' if name == 'index' else f'{name}.html')
        canonical = ''
        if name != '404':
            canonical = f'<link rel="canonical" href="{url}">\n'
            if meta.get('noindex') != 'yes':
                urls.append(url)
        parts = {'url': url, 'canonical': canonical, 'title_full': html.escape(fill(title_full)), 'description': html.escape(fill(meta.get('description', ''))),
                 'nav_links': nav_links, 'body': body.strip(), 'scripts': scripts, 'head_extra': head_extra,
                 'disclaimer': html.escape(DISCLAIMER)}
        out = re.sub(r'%%(\w+)%%', lambda m: parts[m.group(1)], LAYOUT)
        if name == '404':
            out = rooted(out)
        (OUT / f'{name}.html').write_text(fill(out), encoding='utf-8', newline='\n')
        print('wrote', f'site/{name}.html')
    sitemap = ''.join(f'  <url><loc>{u}</loc></url>\n' for u in urls)
    (OUT / 'sitemap.xml').write_text('<?xml version="1.0" encoding="UTF-8"?>\n'
                                     '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
                                     + sitemap + '</urlset>\n', encoding='utf-8', newline='\n')
    (OUT / 'robots.txt').write_text(f"User-agent: *\nAllow: /\nSitemap: {SITE['base']}sitemap.xml\n",
                                    encoding='utf-8', newline='\n')
    print('wrote site/sitemap.xml, site/robots.txt')


if __name__ == '__main__':
    SITE.update(download_facts())
    build()
