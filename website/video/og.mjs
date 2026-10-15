// Renders og.html to ../site/assets/img/og.png (1200x630).
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import puppeteer from 'puppeteer-core'
const here = path.dirname(fileURLToPath(import.meta.url))
const browser = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true, args: ['--allow-file-access-from-files'] })
const page = await browser.newPage()
await page.setViewport({ width: 1200, height: 630 })
await page.goto(pathToFileURL(path.join(here, 'og.html')).href, { waitUntil: 'networkidle0' })
await page.evaluate(() => document.fonts.ready)
const file = path.join(here, '..', 'site', 'assets', 'img', 'og.png')
await page.screenshot({ path: file })
console.log(file)
await browser.close()
