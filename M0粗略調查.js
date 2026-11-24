// 粗略的 M0 調查：各專案裡「第四支」每條規則的候選數。只看 XAML 文字，不解析樣式與範本 —— 數字是上限的量級，不是 finding。
const fs = require('fs'), path = require('path');
const roots = process.argv.slice(2);
function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name.startsWith('.') || ['bin', 'obj', 'node_modules', 'packages'].includes(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p); else if (e.name.toLowerCase().endsWith('.xaml')) yield p;
  }
}
// 一個元素的開標籤（含屬性），粗略
const tagRe = /<([A-Za-z_][\w.:]*)(\s[^<>]*?)?(\/?)>/gs;
for (const root of roots) {
  const c = { files: 0, buttons: 0, iconButtons: 0, iconButtonsNamed: 0, iconButtonsTooltipOnly: 0, focusNull: 0, mouseOnNonControl: 0, tabStopFalse: 0, smallTargets: 0, images: 0, imagesNamed: 0, textBoxes: 0, textBoxesNamed: 0 };
  for (const f of walk(root)) {
    c.files++;
    const t = fs.readFileSync(f, 'utf8');
    for (const m of t.matchAll(tagRe)) {
      const name = m[1], attrs = m[2] || '', selfClose = m[3] === '/';
      const local = name.split(':').pop();
      const has = a => new RegExp('\\s' + a.replace('.', '\\.') + '\\s*=').test(attrs);
      const val = a => (attrs.match(new RegExp('\\s' + a.replace('.', '\\.') + '\\s*=\\s*"([^"]*)"')) || [])[1];
      const named = has('AutomationProperties.Name') || has('AutomationProperties.LabeledBy');
      if (/(^|[^A-Za-z])(Button|ToggleButton|RepeatButton)$/.test(local) && !local.includes('.')) {
        c.buttons++;
        const content = val('Content');
        // 圖示按鈕：沒有 Content 屬性的字、而且自己閉合或內容是 Image/Path/Viewbox（粗略看後面 300 字）
        const after = selfClose ? '' : t.slice(m.index + m[0].length, m.index + m[0].length + 400);
        const textContent = content && !content.startsWith('{StaticResource') && !content.startsWith('{DynamicResource Vector') ;
        const iconish = !textContent && (selfClose ? !content : /^\s*<(Image|Path|Viewbox|Canvas|Rectangle|Ellipse|[\w:]*Icon|[\w:]*Symbol)\b/.test(after));
        if (iconish) {
          c.iconButtons++;
          if (named) c.iconButtonsNamed++;
          else if (has('ToolTip') || /<[\w:]*Button\.ToolTip>/.test(after)) c.iconButtonsTooltipOnly++;
        }
      }
      if (/FocusVisualStyle\s*=\s*"\{x:Null\}"/.test(attrs) || (local === 'Setter' && val('Property') === 'FocusVisualStyle' && val('Value') === '{x:Null}')) c.focusNull++;
      if (/^(Border|Grid|StackPanel|Image|TextBlock|Rectangle|Ellipse|Path|Canvas|DockPanel)$/.test(local)
          && /\s(MouseLeftButtonDown|MouseLeftButtonUp|PreviewMouseLeftButtonDown|PreviewMouseLeftButtonUp|MouseDown|MouseUp)\s*=/.test(attrs)) c.mouseOnNonControl++;
      if (/(Button|CheckBox|RadioButton|ComboBox|TextBox|Slider|ListBox|MenuItem)$/.test(local) && /\s(IsTabStop|Focusable)\s*=\s*"False"/i.test(attrs)) c.tabStopFalse++;
      if (/(Button|CheckBox|RadioButton|ToggleButton)$/.test(local)) {
        const w = parseFloat(val('Width')), h = parseFloat(val('Height'));
        if ((w && w < 24) || (h && h < 24)) c.smallTargets++;
      }
      if (local === 'Image') { c.images++; if (named) c.imagesNamed++; }
      if (/^(TextBox|PasswordBox)$/.test(local) || /:(TextBox|PasswordBox)$/.test(name)) { c.textBoxes++; if (named) c.textBoxesNamed++; }
    }
  }
  console.log(path.basename(root).padEnd(12), JSON.stringify(c));
}
