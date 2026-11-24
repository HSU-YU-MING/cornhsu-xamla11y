// 真的 NVDA 走過 uiaoracle 的 nvda-window，記下每一案念了什麼。
//
//   node prototype/UiaOracle/nvda.mjs <guidepup 的資料夾> <uiaoracle.exe> [輸出.json] [語言，如 en、zh_TW]
//
// guidepup 的資料夾：裝過 @guidepup/guidepup 的 node_modules（先 npx @guidepup/setup install nvda）。
// ⚠ 會出聲、會搶焦點；要互動桌面，不進 CI。按鍵是真的送到前景視窗：視窗一失去前景就停手，不繼續按。
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { spawn, execFileSync } from 'node:child_process';

const [modules, exe, out = 'nvda-facts.json', lang] = process.argv.slice(2);
// NVDA 的 --lang 只管這一次，不改設定；guidepup 沒有這個參數，替它啟動的 nvda.exe 加上去
const cp = createRequire(import.meta.url)('node:child_process');
const spawned = cp.spawn;
if (lang) cp.spawn = (file, args, options) => spawned(file, /nvda.exe$/i.test(file) ? [...args, `--lang=${lang}`] : args, options);
const { nvda } = createRequire(path.join(path.resolve(modules), 'x.js'))('@guidepup/guidepup');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const running = name => execFileSync('tasklist', ['/FI', `IMAGENAME eq ${name}`, '/NH'], { encoding: 'utf8' }).toLowerCase().includes(name.toLowerCase());
if (running('nvda.exe')) { console.error('NVDA is already running; close it first'); process.exit(2); }
const settingsWereOpen = running('SystemSettings.exe');

const host = spawn(exe, ['nvda-window', 'nvda.txt'], { stdio: ['pipe', 'pipe', 'inherit'] });
const cases = [];
let active = false, focus = null, ready = false, lost = 0;
let buffer = '';
host.stdout.setEncoding('utf8');
host.stdout.on('data', d => {
  buffer += d;
  const lines = buffer.split(/\r?\n/);
  buffer = lines.pop();
  for (const l of lines) {
    const [kind, a, b, c, d] = l.split('\t');
    if (kind === 'case') cases.push({ title: b, tool: c, xaml: d });
    else if (kind === 'ready') ready = true;
    else if (kind === 'active') active = true;
    else if (kind === 'lost') { active = false; lost++; }
    else if (kind === 'focus') focus = { at: a, type: b };
  }
});
const stop = async (code, why) => {
  if (why) console.error(why);
  try { await nvda.stop(); } catch { /* 沒起來就算了 */ }
  try { host.stdin.write('quit\n'); } catch { /* 已經關了 */ }
  await sleep(500);
  host.kill();
  process.exit(code);
};

while (!ready) await sleep(100);
await nvda.start();
await sleep(2500);
// NVDA 啟動時 Windows 常彈出「設定」搶焦點；本來就開著的不要關
if (!settingsWereOpen && running('SystemSettings.exe')) { try { execFileSync('taskkill', ['/IM', 'SystemSettings.exe', '/F'], { stdio: 'ignore' }); } catch { /* 沒關掉也沒差 */ } }
await sleep(500);
for (let i = 0; i < 6 && !active; i++) { host.stdin.write('start\n'); await sleep(900); }
host.stdin.write('start\n');
await sleep(1500);
if (!active) await stop(3, 'the window did not get the foreground (click it and run again)');

const heard = new Map();
await nvda.clearSpokenPhraseLog();
for (let i = 0; i < cases.length * 3; i++) {
  if (!active) await stop(4, `the window lost the foreground after ${heard.size} case(s); stopped pressing keys`);
  const before = (await nvda.spokenPhraseLog()).length;
  // capture: true 才會等全部念完；預設只拿第一句，清單項目的名字在第二句
  await nvda.press('Tab', { capture: true });
  if (!active) await stop(4, `the window lost the foreground after ${heard.size} case(s); stopped pressing keys`);
  const said = (await nvda.spokenPhraseLog()).slice(before).join(' | ');
  if (focus?.at === 'end') break;
  if (focus && focus.at !== '-1') {
    const list = heard.get(focus.at) ?? [];
    list.push({ type: focus.type, said });
    heard.set(focus.at, list);
    // ToolBar、DataGrid 的 Tab 會在裡面繞圈：跳到下一案
    if (list.length >= 3 && Number(focus.at) + 1 >= cases.length) break;
    if (list.length >= 3) { host.stdin.write(`goto ${Number(focus.at) + 1}` + String.fromCharCode(10)); await sleep(1500); await nvda.clearSpokenPhraseLog(); }
  }
}
const result = cases.map((c, i) => ({ ...c, heard: heard.get(String(i)) ?? [] }));
fs.writeFileSync(out, JSON.stringify({ nvda: String(await nvda.version), language: lang ?? 'system', cases: result }, null, 1) + '\n');
for (const r of result) console.log(`${r.title.padEnd(34)} ${r.tool.padEnd(18)} ${r.heard.map(h => `${h.type}: ${h.said}`).join('  ||  ') || '(no tab stop)'}`);
await stop(0);
