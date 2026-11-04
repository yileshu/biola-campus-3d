// Opens the Web build in headless Chromium, runs the in-game screenshot tour (?shot=all) and
// saves one PNG per fixed spot plus the full browser console log.
//
//   node Tools/capture/capture.mjs <url> <outDir> [label]
//
// The game logs "BIOLA_SHOT_READY <name> ..." when a spot has finished streaming and holds the
// camera still for a few seconds; we screenshot on that message. Diagnostics lines start with
// "[BiolaDiag]".
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const [url, outDir = 'screenshots', label = ''] = process.argv.slice(2);
if (!url) { console.error('usage: capture.mjs <url> <outDir> [label]'); process.exit(2); }
fs.mkdirSync(outDir, { recursive: true });

const TOTAL_TIMEOUT_MS = 35 * 60 * 1000;
const consoleLines = [];
const shots = [];
let done = false;

const browser = await chromium.launch({
  headless: true,
  args: [
    '--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--ignore-gpu-blocklist',
    '--enable-webgl', '--enable-features=SharedArrayBuffer', '--disable-background-timer-throttling',
    '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows',
  ],
});
const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
const page = await context.newPage();

const redact = (s) => String(s).replace(/key=[A-Za-z0-9_\-]+/g, 'key=***').replace(/AI[z]a[0-9A-Za-z_\-]{20,}/g, '***');
const log = (s) => { const line = `[${new Date().toISOString()}] ${redact(s)}`; consoleLines.push(line); console.log(line); };
let pending = Promise.resolve();

page.on('console', (msg) => {
  const text = msg.text();
  log(`console.${msg.type()}: ${text}`);
  const m = text.match(/BIOLA_SHOT_READY (\S+)/);
  if (m) {
    const name = m[1];
    pending = pending.then(async () => {
      const file = path.join(outDir, `${name}.png`);
      try {
        // Software (SwiftShader) rendering on a GPU-less runner can take seconds per frame.
        await page.screenshot({ path: file, timeout: 180000 });
        shots.push({ name, file, info: text });
        log(`saved ${file}`);
      } catch (e) {
        log(`screenshot ${name} failed: ${e.message}`);
      }
      try {
        // Tell the game it may move on (read by BiolaShot.jslib).
        await page.evaluate(() => { window.biolaShotAck = (window.biolaShotAck | 0) + 1; });
      } catch (e) {
        log(`ack ${name} failed: ${e.message}`);
      }
    });
  }
  if (text.includes('BIOLA_SHOT_DONE')) done = true;
});
page.on('pageerror', (e) => log(`pageerror: ${e.message}`));
page.on('requestfailed', (r) => {
  const u = r.url().replace(/key=[^&]+/, 'key=***');
  log(`requestfailed: ${u} ${r.failure()?.errorText ?? ''}`);
});
page.on('response', (r) => {
  if (r.status() >= 400) log(`http ${r.status()}: ${r.url().replace(/key=[^&]+/, 'key=***')}`);
});

const sep = url.includes('?') ? '&' : '?';
const target = `${url}${sep}shot=all`;
log(`opening ${target}`);
const started = Date.now();
await page.goto(target, { waitUntil: 'load', timeout: 120000 });

// coi-serviceworker reloads the page once to become cross-origin isolated.
let lastUrl = page.url();
while (!done && Date.now() - started < TOTAL_TIMEOUT_MS) {
  await page.waitForTimeout(2000);
  if ((Date.now() - started) % 60000 < 2000) {
    const iso = await page.evaluate(() => window.crossOriginIsolated).catch(() => 'n/a');
    log(`waiting… ${Math.round((Date.now() - started) / 1000)}s, shots=${shots.length}, crossOriginIsolated=${iso}`);
  }
  if (page.url() !== lastUrl) { lastUrl = page.url(); log(`navigated: ${lastUrl}`); }
}
await pending;
if (!done) {
  log('TIMEOUT before BIOLA_SHOT_DONE — saving a fallback screenshot');
  await page.screenshot({ path: path.join(outDir, 'timeout.png') }).catch(() => {});
}

const diag = consoleLines.filter((l) => l.includes('[BiolaDiag]') || l.includes('BIOLA_SHOT'));
fs.writeFileSync(path.join(outDir, 'console.log'), consoleLines.join('\n') + '\n');
fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify({ label, url: target, done, shots, diagnostics: diag }, null, 2));
await browser.close();
console.log(`done=${done} shots=${shots.length}`);
process.exit(shots.length > 0 ? 0 : 1);
