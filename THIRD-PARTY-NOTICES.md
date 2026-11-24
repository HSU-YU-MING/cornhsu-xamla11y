# Third-party notices

XamlA11y is MIT licensed. The package contains no third-party code: it depends only on the .NET runtime.

## Bundled data

### `data/known-controls.json`

How controls of MahApps.Metro 2.4.11, MaterialDesignThemes 5.3.0, HandyControl 3.5.1 and WPF-UI 4.3.0 get their UI
Automation names, and whether their templates show keyboard focus once `FocusVisualStyle` is removed. **Measured on the
maintainer's machine** by opening each control in a WPF window (`prototype/UiaOracle`, modes `themes` and `focus-themes`).
It holds control names and yes/no answers, not XAML or code. The libraries are MIT licensed and are not redistributed.

### `tests/XamlA11y.Tests/uia-facts.json` (not in the package)

XAML written for this project and the answers WPF gave for it.

## Used in development, not redistributed

- WPF itself (MIT, .NET Foundation) — run, and its source read to confirm what was measured.
- Axe.Windows (MIT, Microsoft) — the second opinion in `prototype/UiaOracle`.
- xunit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Microsoft.SourceLink.GitHub (MIT, build only).
