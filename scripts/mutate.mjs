// 變異測試：故意改壞一處、跑測試，測試全綠的就是沒人守。
//
//   node scripts/mutate.mjs <工作資料夾> [幾處=60] [亂數種子=1] [第幾個/共幾個=0/1] [只改檔名符合這個樣式的]
//
// 在複製品上改，不動這個資料夾。平行跑就開好幾個，給不同的「第幾個/共幾個」。一處約 20 秒。
// 剩下活著的不一定要補：迴圈上限、防呆、換句話說一樣的條件，改了結果不變。
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const [work, count = '60', seed = '1', part = '0/1', only = ''] = process.argv.slice(2);
if (!work) { console.error('node scripts/mutate.mjs <work folder> [count] [seed] [part/parts]'); process.exit(2); }
const [index, parts] = part.split('/').map(Number);
const repo = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const copyTo = path.resolve(work, `mutate-${index}`);

(function copy(from, to) {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    if (['bin', 'obj', '.git', 'prototype', 'artifacts'].includes(e.name)) continue;
    const a = path.join(from, e.name), b = path.join(to, e.name);
    if (e.isDirectory()) copy(a, b); else fs.copyFileSync(a, b);
  }
})(repo, copyTo);

const operators = [
  [/ == /g, ' != '], [/ != /g, ' == '], [/ && /g, ' || '], [/ \|\| /g, ' && '], [/ is null/g, ' is not null'], [/ is not null/g, ' is null'],
  [/return true;/g, 'return false;'], [/return false;/g, 'return true;'], [/ or "([A-Za-z]+)"/g, ''], [/"([A-Z][A-Za-z]+)", /g, ''],
  [/ > 0/g, ' >= 0'], [/ >= /g, ' > '], [/ < /g, ' <= '], [/\(!/g, '('], [/IsTrue/g, 'IsFalse'], [/IsFalse/g, 'IsTrue'], [/\.Any\(/g, '.All('],
  [/continue;/g, ';'], [/\+ 1/g, '+ 2'], [/Verdict\.Blocked/g, 'Verdict.NotJudged'],
];
const sites = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const file = path.join(dir, e.name);
    if (e.isDirectory()) { walk(file); continue; }
    if (!e.name.endsWith('.cs') || only && !new RegExp(only).test(e.name)) continue;
    fs.readFileSync(file, 'utf8').split('\n').forEach((line, i) => {
      if (/^\s*(\/\/|using |\[Generated)/.test(line) || /JsonPropertyName|Regex\(/.test(line)) return;
      for (const [pattern, to] of operators)
        for (const m of line.matchAll(pattern)) sites.push({ file, i, at: m.index, from: m[0], to });
    });
  }
})(path.join(copyTo, 'src', 'XamlA11y.Core'));

let state = Number(seed);
const random = () => (state = (state * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
for (let i = sites.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [sites[i], sites[j]] = [sites[j], sites[i]]; }
const mine = sites.filter((_, i) => i % parts === index);

// 有時間上限的測試在滿載的機器上會自己紅，不能算「抓到」
const filter = ['quadratic', 'explode', 'Branching_cycles_finish'].map(t => 'FullyQualifiedName!~' + t).join('&');
const dotnet = args => {
  const r = spawnSync('dotnet', args, { cwd: copyTo, stdio: 'ignore', timeout: 120_000 });
  // 改壞的程式跑出無窮迴圈：測試行程不會自己走，會鎖住 DLL 讓後面全部建不起來
  if (r.error && process.platform === 'win32')
    spawnSync('powershell', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'mutate-${index}.tests' -and $_.Name -in 'testhost.exe','dotnet.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -Confirm:$false }`], { stdio: 'ignore' });
  return r.status;
};
const build = ['build', 'tests/XamlA11y.Tests', '-c', 'Release', '--nologo', '-v', 'q'];
const test = ['test', 'tests/XamlA11y.Tests', '-c', 'Release', '--nologo', '--no-build', '-v', 'q', '--filter', filter];
if (dotnet(build) !== 0 || dotnet(test) !== 0) { console.error('the tests are not green to begin with'); process.exit(1); }

let killed = 0, survived = 0, invalid = 0;
for (const m of mine) {
  if (killed + survived >= Number(count)) break;
  const original = fs.readFileSync(m.file, 'utf8'), lines = original.split('\n'), line = lines[m.i];
  lines[m.i] = line.slice(0, m.at) + m.to + line.slice(m.at + m.from.length);
  fs.writeFileSync(m.file, lines.join('\n'));
  if (dotnet(build) !== 0) invalid++;
  else if (dotnet(test) === 0) {
    survived++;
    console.log(`survived ${path.relative(path.join(copyTo, 'src', 'XamlA11y.Core'), m.file).replace(/\\/g, '/')}:${m.i + 1} [${m.from}→${m.to}] ${line.trim().slice(0, 160)}`);
  } else killed++;
  fs.writeFileSync(m.file, original);
}
console.log(`killed ${killed}, survived ${survived}, did not compile ${invalid}`);
