// nvda.mjs 各語言錄的合成測試用的一份（只留 NVDA 念的，不留工具的判定）。
//
//   node prototype/UiaOracle/nvda-merge.mjs <中文.json> <英文.json> > tests/XamlA11y.Tests/nvda-facts.json
//   node prototype/UiaOracle/nvda-merge.mjs --walk <中文.json> <英文.json> > tests/XamlA11y.Tests/nvda-walk-facts.json
import fs from 'node:fs';

const walk = process.argv[2] === '--walk';
const [zh, en] = process.argv.slice(walk ? 3 : 2).map(f => JSON.parse(fs.readFileSync(f, 'utf8')));
const said = c => c.heard[0]?.said.trim() ?? null;
const cases = zh.cases.map((c, i) => {
  if (en.cases[i]?.title !== c.title) throw new Error(`case ${i}: '${c.title}' is not in both files`);
  if (walk) {
    const stops = en.cases[i].heard;
    if (stops.map(s => s.name).join() !== c.heard.map(s => s.name).join()) throw new Error(`case '${c.title}': the two runs stopped at different places`);
    return { title: c.title, xaml: c.xaml, resources: c.resources, stops: c.heard.map((s, k) => ({ name: s.name, zh: s.said.trim(), en: stops[k].said.trim() })) };
  }
  return { title: c.title, xaml: c.xaml, zh: said(c), en: said(en.cases[i]) };
});
process.stdout.write(JSON.stringify({ nvda: zh.nvda, languages: ['zh_TW', 'en'], cases }, null, 1) + '\n');
