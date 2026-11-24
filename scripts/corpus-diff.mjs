// 兩次 corpus-run.sh 的輸出差在哪：消失的、多出來的 finding（每一類印幾筆），可解析率變了的專案。
//
//   node scripts/corpus-diff.mjs <前> <後> [每類幾筆]
import fs from 'node:fs';
import path from 'node:path';

const [before, after, limit = '12'] = process.argv.slice(2);
const load = dir => {
  const findings = new Map(), summaries = new Map();
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.json'))) {
    const name = f.replace('.json', ''), j = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
    summaries.set(name, j.summary);
    for (const x of j.findings) findings.set(`${name} | ${x.file}:${x.line} | ${x.kind} | ${x.element}`, x);
  }
  return { findings, summaries };
};
const A = load(before), B = load(after);
const group = (from, other) => {
  const out = new Map();
  for (const [k, x] of from) if (!other.has(k)) out.set(x.kind, [...(out.get(x.kind) ?? []), `${k}  ${x.message}`]);
  return out;
};
for (const [title, changes] of [['gone', group(A.findings, B.findings)], ['added', group(B.findings, A.findings)]])
  for (const [kind, list] of changes) {
    console.log(`${title} ${kind} ${list.length}`);
    for (const l of list.slice(0, Number(limit))) console.log('   ' + l.slice(0, 260));
  }
const moved = [...A.summaries].filter(([n, s]) => B.summaries.has(n) && Math.abs(B.summaries.get(n).resolution - s.resolution) > 0.4).map(([n, s]) => `${n} ${s.resolution} → ${B.summaries.get(n).resolution}`);
console.log(`resolution: ${moved.join(', ') || 'no change'}`);
