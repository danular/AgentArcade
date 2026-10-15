// Renders promo.html frame by frame in headless Chrome and encodes MP4s with ffmpeg.
// The page exposes window.seek(seconds) which draws the exact frame for that time,
// so the output never drops or stretches frames.
//
//   node render.mjs                 -> out/agent-arcade-promo-16x9.mp4 and -9x16.mp4
//   node render.mjs 16x9            -> one format
//   node render.mjs 16x9 --still 12 -> out/still-16x9-12s.png (check a single frame)

import { spawn } from 'node:child_process'
import { existsSync, mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import puppeteer from 'puppeteer-core'
import ffmpegPath from 'ffmpeg-static'

const here = path.dirname(fileURLToPath(import.meta.url))
const out = path.join(here, 'out')
mkdirSync(out, { recursive: true })

const FPS = 30
const DURATION = 30
const FORMATS = { '16x9': [1920, 1080], '9x16': [1080, 1920], '1x1': [1080, 1080] }

const CHROME = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
].find(existsSync)

const argv = process.argv.slice(2)
const stillAt = argv.includes('--still') ? Number(argv[argv.indexOf('--still') + 1]) : null
const formats = argv.filter(a => FORMATS[a])
const todo = formats.length ? formats : ['16x9', '9x16']

const browser = await puppeteer.launch({ executablePath: CHROME, headless: true, args: ['--hide-scrollbars', '--force-color-profile=srgb'] })
try {
  for (const fmt of todo) {
    const [w, h] = FORMATS[fmt]
    const page = await browser.newPage()
    await page.setViewport({ width: w, height: h, deviceScaleFactor: 1 })
    const url = pathToFileURL(path.join(here, 'promo.html')).href + `?format=${fmt}`
    await page.goto(url, { waitUntil: 'networkidle0' })
    await page.evaluate(() => document.fonts.ready)
    await page.waitForFunction('window.seek !== undefined')

    if (stillAt !== null) {
      await page.evaluate(t => window.seek(t), stillAt)
      const file = path.join(out, `still-${fmt}-${stillAt}s.png`)
      await page.screenshot({ path: file })
      console.log(file)
      await page.close()
      continue
    }

    const file = path.join(out, `agent-arcade-promo-${fmt}.mp4`)
    const audio = path.join(here, 'out', 'music.wav')
    const ff = spawn(ffmpegPath, [
      '-y', '-loglevel', 'error',
      '-f', 'image2pipe', '-framerate', String(FPS), '-i', '-',
      ...(existsSync(audio) ? ['-i', audio, '-c:a', 'aac', '-b:a', '192k', '-shortest'] : []),
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
      file,
    ], { stdio: ['pipe', 'inherit', 'inherit'] })
    const done = new Promise((res, rej) => ff.on('close', c => (c === 0 ? res() : rej(new Error('ffmpeg exited ' + c)))))

    const frames = FPS * DURATION
    for (let f = 0; f < frames; f++) {
      await page.evaluate(t => window.seek(t), f / FPS)
      const buf = await page.screenshot({ type: 'jpeg', quality: 95 })
      if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r))
      if (f % 150 === 0) process.stdout.write(`${fmt} ${Math.round((f / frames) * 100)}%\n`)
    }
    ff.stdin.end()
    await done
    console.log(file)
    await page.close()
  }
} finally {
  await browser.close()
}
