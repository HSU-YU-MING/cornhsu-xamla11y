# `xamla11y.config.json`

Looked for in the scanned root, or passed with `--config`. Comments and trailing commas are allowed.
A field name or value the tool does not understand is an error (exit 2) that names the field: a typo
that is silently skipped would look like a setting that works.

```jsonc
{
  "schemaVersion": 1,
  "exclude": ["/ThirdParty/"],
  "minCoverage": 80,
  "treatAs": { "ExtendedButton": "Button", "WatermarkTextBox": "TextBox" },
  "rules": { "not-in-tab-order": "off", "symbol-name": "warn" },
  "themeShowsFocus": true,
  "standard": "en301549",
  "level": "AA",
  "language": "zh-Hant"
}
```

| Field | Type | |
|---|---|---|
| `schemaVersion` | `1` | optional |
| `exclude` | list of text | files whose path relative to the root (starting with `/`) contains the text, ignoring case |
| `minCoverage` | 0–100 | exit 3 below this resolution; default 60, 0 accepts any. `--min-coverage` wins |
| `treatAs` | name → WPF control | "this control gets its accessible name the way that WPF control does". Local names, no prefix. You vouch for it: the class must not override `OnCreateAutomationPeer` or set its own name in code |
| `rules` | finding → `off`, `info`, `warn`, `fail` | severity of each kind of finding. `off` also stops counting it; a rule with every kind off does not count in the resolution, and a run with every rule off exits 3. Wins over `standard` and `level` |
| `themeShowsFocus` | bool | the theme your app merges draws keyboard focus even where `FocusVisualStyle` is removed, and you have seen it. Only matters for themes that were not measured |
| `standard` | `wcag22`, `wcag21`, `wcag20`, `en301549`, `en301549-v4`, `section508` | default `wcag22`. Findings whose criteria are not in it are off ([clauses](standards.md#choosing-one)). `--standard` wins |
| `level` | `A`, `AA` | default `AA`, which includes A. `--level` wins |
| `language` | `en`, `zh-Hant` | language of the console, Markdown and HTML reports; also how the GitHub Action's comment gets its language. `--lang` wins |

Classes in the scanned C# are followed without configuration (`class ExtendedButton : Button`), unless the
file that declares them mentions `AutomationProperties` or `OnCreateAutomationPeer`, or the name is declared
twice with different bases.

`treatAs` means the same thing as in `xamlfit.config.json`, but the two files are separate: a control can be
a Button for its name and something else for its layout.

**On CI the config file is part of the pull request.** It can lower the gate. Pass `--config` with a file
outside the author's reach if that matters to you.
