// Renders og.html to ../site/assets/img/og.png (1200x630, the website's og:image), or with --github to
// ../../docs/media/social-preview.png (1280x640, GitHub's size for a repository's social preview; set it in the
// repository's Settings > General > Social preview).
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import puppeteer from 'puppeteer-core'
const here = path.dirname(fileURLToPath(import.meta.url))
const github = process.argv.includes('--github')
const size = github ? { width: 1280, height: 640 } : { width: 1200, height: 630 }
const file = github ? path.join(here, '..', '..', 'docs', 'media', 'social-preview.png') : path.join(here, '..', 'site', 'assets', 'img', 'og.png')
const browser = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true, args: ['--allow-file-access-from-files'] })
const page = await browser.newPage()
await page.setViewport(size)
await page.goto(pathToFileURL(path.join(here, 'og.html')).href, { waitUntil: 'networkidle0' })
await page.evaluate(() => document.fonts.ready)
await page.screenshot({ path: file })
console.log(file)
await browser.close()
