# Roadmap

Not promises; the order may change with feedback. A rule gets in when real projects show it matters.

- **Other containers without a name** (`ProgressBar`, `Calendar`, `Menu`, `ToolBar`). Axe-Windows reports them too.
- **One combined GitHub Action** for XamlContrast, XamlKeys, XamlFit and XamlA11y. The inputs already match; see
  [docs/four-tools.md](docs/four-tools.md).
- **Duplicate access keys** in one window.
- **Names that repeat** (five buttons called "…", a list of buttons called "Edit"). Axe-Windows reports focusable siblings
  with the same name; two of the maintainer's three apps have them.
- `PresentationFramework.Fluent` in the measured themes.
- 1.0 freezes the CLI, exit codes, config, JSON, baseline keys and action inputs.

Considered and left out: images without a name (decorative or not cannot be decided statically), text scaling (WPF does not
follow the Windows setting), reading order (needs layout).
