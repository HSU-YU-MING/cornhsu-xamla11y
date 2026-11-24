# Roadmap

Not promises; the order may change with feedback. A rule gets in when real projects show it matters.

- **Lists and grids without a name** (`ListBox`, `ListView`, `TreeView`, `DataGrid`). Axe-Windows reports them; left out
  of 0.1 because it would double the first run's list in most projects.
- **One combined GitHub Action** for XamlContrast, XamlKeys, XamlFit and XamlA11y. The inputs already match; see
  [docs/four-tools.md](docs/four-tools.md).
- **Duplicate access keys** in one window.
- **Names that repeat** (five buttons called "…", a list of buttons called "Edit").
- `PresentationFramework.Fluent` in the measured themes.
- 1.0 freezes the CLI, exit codes, config, JSON, baseline keys and action inputs.

Considered and left out: images without a name (decorative or not cannot be decided statically), text scaling (WPF does not
follow the Windows setting), reading order (needs layout).
