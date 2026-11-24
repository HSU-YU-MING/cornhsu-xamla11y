# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [0.1.0] - Unreleased

First version.

- `xamla11y audit <root>`: fifteen kinds of finding in four groups — names of controls, inputs and lists (WCAG 4.1.2,
  1.1.1, 1.3.1, 2.5.3), items read as their type name and silent tab stops (4.1.2), controls that sound alike (2.4.6), names that differ from the visible text (2.5.3),
  visible focus (2.4.7), mouse-only elements (2.1.1), tab order (2.1.1), target size (2.5.8).
- Every finding names its WCAG 2.2 criteria with their level; `docs/standards.md` says why, when it is not a violation, how
  EN 301 549, Section 508 and Taiwan's rules differ, and what a person still has to check. `--standard` and `--level` choose
  WCAG 2.2, 2.1 or 2.0, EN 301 549 V3.2.1 or V4.1.1, or Section 508; HTML, JSON and SARIF carry the clause numbers.
- `--html`: one self-contained page with why, how to fix, what NVDA says (only where it was run), the clauses and the checks a
  person has to do. `docs/fixes.md`: before and after for
  every finding, linked from SARIF and HTML. `--lang zh-Hant` for the console, Markdown and HTML reports.
- Names follow what WPF does, measured with a UI Automation client on .NET Framework 4.8 and .NET 8–11 and
  checked against Axe-Windows and against what NVDA says: `Label.Target` gives no name, a ToolTip gives no name, text inside a panel
  gives no name, template text gives no name, `MaterialDesignThemes` 5.3's `HintAssist.Hint` does.
- Focus under MahApps.Metro 2.4, MaterialDesignThemes 5.3, HandyControl 3.5 and WPF-UI 4.3 was measured in
  real windows; other themes and versions are unknown unless `themeShowsFocus` vouches for them.
- Subclasses are followed through the project's own C# (`class ExtendedButton : Button`); `treatAs` declares the rest.
- `xamla11y fix`: connects the label, text or ToolTip that is already there, checks every change in memory first, writes only with `--write`.
  `xamla11y walk`: the Tab stops of one file in order, with the name and what NVDA reads, compared with real WPF windows.
- A `.csproj`, `.sln` or single `.xaml` in place of the folder; `--only`. `--prune-baseline` drops what is fixed and accepts nothing new.
- The reports say what was accepted with `xamla11y-ignore` and why, which of those comments are no longer needed, and how many controls are built in C# (not checked).
- JSON, SARIF, Markdown, MSBuild output, baseline without line numbers, resolution gate (exit 3), and a
  GitHub Action with the same inputs as XamlContrast, XamlKeys and XamlFit.
