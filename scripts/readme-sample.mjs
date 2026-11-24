// verify-readme-sample.sh 的另一半：比對或重貼 README「## Example」之後第一個 ``` 區塊。
import fs from 'node:fs';

const update = process.argv[2] === '--update';
const actual = process.env.SAMPLE.replace(/\r\n/g, '\n').trimEnd();
const file = 'README.md';
const text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
const m = /(## Example[\s\S]*?\n```\n)([\s\S]*?)(\n```)/.exec(text);
if (!m) { console.error('cannot locate the sample block in README.md'); process.exit(1); }
if (m[2].trimEnd() === actual) { console.log('README sample matches the tool output.'); process.exit(0); }
if (update) {
  // 取代用函式：輸出裡的 $ 不能被當成取代樣式
  fs.writeFileSync(file, text.replace(m[0], () => m[1] + actual + m[3]));
  console.log('README sample updated.');
  process.exit(0);
}
console.error('README sample is out of date (run: bash scripts/verify-readme-sample.sh --update)');
const a = m[2].split('\n'), b = actual.split('\n');
for (let i = 0; i < Math.max(a.length, b.length); i++)
  if (a[i] !== b[i]) console.error(`line ${i + 1}\n  README: ${a[i] ?? '(none)'}\n  tool:   ${b[i] ?? '(none)'}`);
process.exit(1);
