// corpus-run.sh 的輸出 → Markdown 表。
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
const load = f => JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')).summary;
const files = fs.readdirSync(dir).filter(f => f.endsWith('.json')).sort();
if (files.length === 0) throw new Error('no reports in ' + dir);
// 欄位跟著報告走：多一種 finding、多一項檢查，這裡不用改
const first = load(files[0]), checks = first.checks.map(c => c.check), kinds = Object.keys(first.counts);
console.log(`| project | files | candidates | resolution | ${checks.map(c => c + ' res.').join(' | ')} | ${kinds.join(' | ')} |`);
console.log('|---|' + '---:|'.repeat(3 + checks.length + kinds.length));
for (const f of files) {
  const s = load(f);
  const cell = c => { const x = s.checks.find(y => y.check === c); return x.candidates === 0 ? '–' : `${x.resolution}`; };
  console.log(`| ${f.replace('.json', '')} | ${s.files} | ${s.candidates} | ${s.resolution} | ${checks.map(cell).join(' | ')} | ${kinds.map(k => s.counts[k]).join(' | ')} |`);
}
