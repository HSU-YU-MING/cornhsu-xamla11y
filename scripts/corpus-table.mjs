// corpus-run.sh 的輸出 → Markdown 表。
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
const kinds = ['missing-name', 'text-not-in-name', 'meaningless-name', 'symbol-name', 'input-no-name', 'focus-not-visible', 'mouse-only', 'not-in-tab-order', 'small-target', 'list-no-name'];
console.log(`| project | files | candidates | resolution | ${['R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7'].map(r => r + ' res.').join(' | ')} | ${kinds.join(' | ')} |`);
console.log('|---|' + '---:|'.repeat(10 + kinds.length));
for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.json')).sort()) {
  const s = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')).summary;
  const rule = r => { const x = s.rules.find(y => y.rule === r); return x.candidates === 0 ? '–' : `${x.resolution}`; };
  console.log(`| ${f.replace('.json', '')} | ${s.files} | ${s.candidates} | ${s.resolution} | ${['R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7'].map(rule).join(' | ')} | ${kinds.map(k => s.counts[k]).join(' | ')} |`);
}
