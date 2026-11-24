// README 兩份的「老實說看得到多少」表：從 corpus-run.sh 的輸出重貼。
//
//   node scripts/readme-corpus.mjs <輸出目錄>            # 比對，不一致 exit 1
//   node scripts/readme-corpus.mjs <輸出目錄> --update   # 重貼
import fs from 'node:fs';
import path from 'node:path';

const [dir, update] = process.argv.slice(2);
// [JSON 檔名, 顯示名稱, 種類]
const ROWS = [
  ['Cornea', 'Cornea', 'own'], ['Kindling', 'Kindling', 'own'], ['QuillNest', 'QuillNest', 'own'],
  ['a11yinsights', 'Accessibility Insights', ''], ['bettergi', 'BetterGI', ''], ['bloxstrap', 'Bloxstrap', ''], ['dngrep', 'dnGrep', ''], ['dnspy', 'dnSpy', ''],
  ['dopamine', 'Dopamine', ''], ['eartrumpet', 'EarTrumpet', ''], ['flowlauncher', 'Flow.Launcher', ''], ['geekdesk', 'GeekDesk', ''], ['handbrake', 'HandBrake', ''],
  ['maa', 'MAA', ''], ['networkmanager', 'NETworkManager', ''], ['nugetpe', 'NuGet Package Explorer', ''], ['playnite', 'Playnite', ''], ['quicklook', 'QuickLook', ''],
  ['screentogif', 'ScreenToGif', ''], ['snoop', 'Snoop', ''], ['synctrayzor', 'SyncTrayzor', ''], ['textgrab', 'Text-Grab', ''], ['twitchdownloader', 'TwitchDownloader', ''],
  ['v2rayn', 'v2rayN', ''], ['visualhft', 'VisualHFT', ''], ['wpfsamples', 'WPF-Samples (Microsoft)', ''],
  ['avalondock', 'AvalonDock', 'lib'], ['fluentribbon', 'Fluent.Ribbon', 'lib'], ['handycontrol', 'HandyControl', 'lib'], ['mahapps', 'MahApps.Metro', 'lib'],
  ['materialdesign', 'MaterialDesignInXaml', 'lib'], ['modernwpf', 'ModernWpf', 'lib'], ['wpfui', 'WPF-UI', 'lib'],
];
const LABEL = { en: { own: ' (maintainer app)', lib: ' (library)', '': '' }, zh: { own: ' (作者的 App)', lib: ' (控制項庫)', '': '' } };

const rows = lang => ROWS.map(([file, name, kind]) => {
  const s = JSON.parse(fs.readFileSync(path.join(dir, file + '.json'), 'utf8')).summary, c = s.counts;
  const cells = [s.files, `**${s.resolution.toFixed(1)}%**`, c['missing-name'] + c['text-not-in-name'] + c['meaningless-name'], c['input-no-name'], c['focus-not-visible'], c['mouse-only'], c['not-in-tab-order'] + c['small-target']];
  return `| ${name}${LABEL[lang][kind]} | ${cells.join(' | ')} |`;
});

let stale = 0;
for (const [file, lang, header] of [['README.md', 'en', '| project | XAML files |'], ['README.zh-Hant.md', 'zh', '| 專案 | XAML 檔 |']]) {
  const text = fs.readFileSync(file, 'utf8');
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  const lines = text.split(/\r?\n/);
  const at = lines.findIndex(l => l.startsWith(header));
  if (at < 0) throw new Error(`${file}: table not found`);
  let end = at + 2;
  while (lines[end]?.startsWith('|')) end++;
  const next = [...lines.slice(0, at + 2), ...rows(lang), ...lines.slice(end)].join(eol);
  if (next === text) continue;
  stale++;
  if (update) fs.writeFileSync(file, next); else console.log(`${file}: the corpus table is out of date`);
}
process.exit(stale && !update ? 1 : 0);
