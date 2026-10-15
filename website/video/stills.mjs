// Renders check frames: node stills.mjs 16x9 1.5 3 5 ...  (one browser, many frames)
import path from 'node:path'
import { existsSync, mkdirSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'
import puppeteer from 'puppeteer-core'
const here = path.dirname(fileURLToPath(import.meta.url))
const out = path.join(here, 'out', 'stills'); mkdirSync(out, { recursive: true })
const [fmt, ...times] = process.argv.slice(2)
const size = { '16x9': [1920, 1080], '9x16': [1080, 1920] }[fmt]
const CHROME = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync)
const browser = await puppeteer.launch({ executablePath: CHROME, headless: true, args: ['--hide-scrollbars', '--allow-file-access-from-files'] })
const page = await browser.newPage()
const errs = []; page.on('pageerror', e => errs.push(e.message)); page.on('console', m => m.type() === 'error' && errs.push(m.text()))
await page.setViewport({ width: size[0], height: size[1] })
await page.goto(pathToFileURL(path.join(here, 'promo.html')).href + '?format=' + fmt, { waitUntil: 'networkidle0' })
await page.evaluate(() => document.fonts.ready)
for (const t of times) {
  await page.evaluate(x => window.seek(x), Number(t))
  const f = path.join(out, `${fmt}-${t}.png`); await page.screenshot({ path: f, type: 'png' }); console.log(f)
}
if (errs.length) console.log('ERRORS:', errs.join('\n'))
await browser.close()
