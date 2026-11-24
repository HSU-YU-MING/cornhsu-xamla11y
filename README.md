# <img src="https://raw.githubusercontent.com/HSU-YU-MING/cornhsu-xamla11y/main/icon.png" width="28" alt=""/> XamlA11y

**Static accessibility audit for WPF XAML. Finds controls a screen reader cannot name and things a keyboard cannot reach — without launching the app — and fails CI on new ones.**

[![NuGet](https://img.shields.io/nuget/v/Cornhsu.XamlA11y)](https://www.nuget.org/packages/Cornhsu.XamlA11y)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[繁體中文](README.zh-Hant.md)

```bash
dotnet tool install -g Cornhsu.XamlA11y

xamla11y audit ./src                                    # look at what is there
xamla11y audit ./src --write-baseline a11y-baseline.json   # accept today's findings
xamla11y audit ./src --baseline a11y-baseline.json         # from now on, fail only on new ones
```

> Requires the **.NET 10 runtime**. Reads XML, not WPF: it runs on Linux, Windows and macOS.
> The tool is **0.x**: pin the version, a release can change which findings you get.
>
> **WPF only.** WinUI/UWP, Avalonia and MAUI files are recognised and reported as *not checked*.

**Run it with `--write-baseline` first.** Most WPF projects have never named a single input, so the
first run can list dozens of findings. A gate that is red from day one gets switched off; the baseline
lets you stop the number from growing and pay it down when you touch each view.

---

## What it finds

| Finding | WCAG | Default | |
|---|---|---|---|
| `missing-name` | 4.1.2, 1.1.1 | fail | A button, check box, tab, menu item… that a screen reader announces as just "button". **A ToolTip is not a name** — WPF exposes it as the help text. |
| `text-not-in-name` | 4.1.2 | fail | It *shows* text, but the text is inside a `StackPanel`, `Border` or `AccessText`. WPF only takes the name from a string, a `TextBlock` or a `ContentControl`. |
| `meaningless-name` | 4.1.2, 1.1.1 | fail | The name is an icon-font character, white space, `System.Windows.Controls.TabItem`, or geometry path data. |
| `input-no-name` | 4.1.2, 1.3.1, 3.3.2 | fail | A `TextBox`, `PasswordBox`, `RichTextBox`, `ComboBox`, `Slider` or `DatePicker` nothing names. **`<Label Target=…>` does not name it** — see below. |
| `focus-not-visible` | 2.4.7 | fail | `FocusVisualStyle` removed, and neither the template nor any style draws focus instead. Reported once, at the setter. |
| `mouse-only` | 2.1.1 | fail | A `Border`, `Image`, `TextBlock`… with a click handler and no keyboard way in. |
| `not-in-tab-order` | 2.1.1, 2.4.3 | warn | `IsTabStop="False"` or `Focusable="False"` on a control with no access key, `IsDefault`/`IsCancel` or other visible way in. |
| `small-target` | 2.5.8 | warn | A size *you set* under 24 DIP, too close to its neighbour for the spacing exception. |
| `list-no-name` | 4.1.2, 1.3.1 | warn | A `ListBox`, `ListView`, `TreeView` or `DataGrid` nothing names. The `GroupBox` around it does not. |
| `symbol-name` | 1.1.1 | info | Named only `×`, `…` or an emoji. |

`xamla11y rules` prints the same list with the fix for each.

This is **not** a claim of WCAG, EN 301 549 or European Accessibility Act conformance. A static tool
sees part of what those ask for, and only part of that is decided by XAML.

## `Label.Target` does not label anything

This is the most common surprise, so it gets its own heading. In WPF

```xml
<Label Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox"/>
```

gives the access key a target and **gives the TextBox no accessible name**. Measured with a UI Automation
client on .NET Framework 4.8 and .NET 8, 9, 10 and 11; Axe-Windows reports it too. Write this instead:

```xml
<Label x:Name="NameLabel" Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox" AutomationProperties.LabeledBy="{Binding ElementName=NameLabel}"/>
```

Watermarks do not name a field either (MahApps.Metro 2.4, HandyControl 3.5, WPF-UI 4.3) — with one
exception: MaterialDesignThemes 5.3 turns `HintAssist.Hint` into the name, and the tool knows that.

## Example

`samples/demo` is broken on purpose:

```
xamla11y audit — 1 XAML file(s), 19 check(s)
  resolution       92.9%  (17 judged, 1 blocked; 1 not judged)
  suppressed       1
    R1 names of controls       4 finding(s),    4 ok,    1 blocked,    1 not judged
    R2 names of inputs         1 finding(s),    1 ok,    0 blocked,    0 not judged
    R3 visible focus           1 finding(s),    0 ok,    0 blocked,    0 not judged
    R4 mouse-only elements     1 finding(s),    0 ok,    0 blocked,    0 not judged
    R5 tab order               1 finding(s),    0 ok,    0 blocked,    0 not judged
    R6 target size             2 finding(s),    0 ok,    0 blocked,    0 not judged
    R7 names of lists          1 finding(s),    0 ok,    0 blocked,    0 not judged
  unknown controls FancyButton ×1  (declare them in treatAs)
  blocked    1  R1: a control this tool does not know (declare it in treatAs)
    missing-name       1
    text-not-in-name   1
    meaningless-name   1
    symbol-name        1
    input-no-name      1
    focus-not-visible  1
    mouse-only         1
    not-in-tab-order   1
    small-target       2
    list-no-name       1

  fail focus-not-visible  MainWindow.xaml:11
       The style 'FlatButton' removes FocusVisualStyle and nothing else shows keyboard focus: its template has no focus trigger and no Focused state
  fail missing-name       MainWindow.xaml:26
       <Button> has no accessible name: its content is <Path>, which gives no text; its ToolTip is the help text, not the name
  fail text-not-in-name   MainWindow.xaml:31
       <Button> shows "Save" but has no accessible name: the text is inside <StackPanel>; WPF only takes the name from a string, a TextBlock or a ContentControl
  fail meaningless-name   MainWindow.xaml:39
       <Button> has an unreadable accessible name (U+E74D): its name is an icon-font character from the private use area
  fail input-no-name      MainWindow.xaml:46
       <TextBox> has no accessible name; <Label Target> at line 45 does not give it one (use AutomationProperties.LabeledBy)
  fail mouse-only         MainWindow.xaml:49
       <Border> reacts to MouseLeftButtonUp (OnOpen) but the keyboard cannot reach it
  warn not-in-tab-order   MainWindow.xaml:54
       <Button> has IsTabStop="False", so Tab never reaches it
  warn small-target       MainWindow.xaml:58
       <Button> is 18 DIP wide and 0 DIP away from <Button>; targets under 24 need more space around them
  warn small-target       MainWindow.xaml:59
       <Button> is 18 DIP wide and 0 DIP away from <Button>; targets under 24 need more space around them
  warn list-no-name       MainWindow.xaml:64
       <ListBox> has no accessible name; the GroupBox header "Files" around it does not give it one
  info symbol-name        MainWindow.xaml:42
       <Button> is named only by the symbol "×"

  Static: the XAML was read, nothing was run; of the code, only class declarations and each XAML file's own code-behind (does it set a name, a content or a key handler?). Passing is not a claim of WCAG or EAA conformance.
```

## How it knows

Every rule was written from what WPF *does*, not from what it is documented to do, and the tool is
compared with the real thing: `prototype/UiaOracle` opens each piece of XAML in a real WPF window,
reads the UI Automation tree from a client on another thread (what a screen reader sees), walks the tab
order, moves keyboard focus and compares pixels, and runs Axe-Windows, the engine of Accessibility
Insights and of Visual Studio's checker.

| compared with | cases | answered | unknown | **contradictions** |
|---|---:|---:|---:|---:|
| UI Automation names, hand-written | 325 | 310 | 15 | **0** |
| UI Automation names, every control × content × way of naming | 8,218 | 7,843 | 375 | **0** |
| UI Automation names, distinct shapes from 19 real projects | 2,172 | 2,172 | 0 | **0** |
| UI Automation names, whole files of 49 public projects and 3 apps, with their styles and resources | 10,398 | 10,398 | – | **0** |
| names under MahApps, MaterialDesign, HandyControl, WPF-UI, Fluent | 97 | 74 | 23 | **0** |
| focus visible (pixels), default theme and the five themes | 107 | 84 | 23 | **0** |
| tab order | 29 | 27 | 2 | **0** |
| target size (bounding rectangle) | 17 | 10 | 7 | **0** |

"Unknown" is a correct answer; "fine" for something a screen reader cannot name is not. The oracle has
earned its keep: the first version of the rules got 177 cases wrong. What it cannot catch is XAML nobody wrote a case
for: two independent reviews then found some forty more ways to fool the tool, and each is now a test. The whole files
are 1,919 that a real WPF window could load without running any of the projects' code; elements that need their code, a
theme that was not measured or an open menu are left out of that row.

The recorded answers of WPF ship with the tests (`tests/XamlA11y.Tests/uia-facts.json`), so CI on Linux
checks the same facts. Details: [docs/M1-UIA實測.md](docs/M1-UIA實測.md) (Chinese).

## What it can see, honestly

**Resolution** = of the elements that should get an answer, the share that did. It is printed on every
run and gated by `--min-coverage` (60 by default): below that the run exits 3, *not trustworthy*, rather
than 0. Names that come from data (`Content="{Binding Title}"`) are counted but are not in the
denominator — no static tool can know them.

Out of the box, no configuration:

| project | XAML files | resolution | control names | inputs | focus | mouse-only | tab order, size (warn) | lists (warn) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Cornea (maintainer app) | 5 | **100.0%** | 0 | 19 | 0 | 0 | 0 | 1 |
| Kindling (maintainer app) | 16 | **100.0%** | 7 | 17 | 0 | 1 | 0 | 1 |
| QuillNest (maintainer app) | 32 | **99.5%** | 65 | 81 | 0 | 3 | 0 | 10 |
| Accessibility Insights | 71 | **59.4%** | 2 | 5 | 0 | 2 | 10 | 2 |
| BetterGI | 153 | **97.7%** | 430 | 551 | 0 | 1 | 8 | 20 |
| Bloxstrap | 40 | **89.9%** | 46 | 25 | 0 | 0 | 0 | 6 |
| dnGrep | 72 | **100.0%** | 53 | 90 | 0 | 0 | 0 | 4 |
| dnSpy | 124 | **99.3%** | 139 | 1365 | 0 | 1 | 94 | 42 |
| Dopamine | 150 | **38.5%** | 67 | 38 | 4 | 2 | 0 | 11 |
| EarTrumpet | 12 | **71.9%** | 11 | 9 | 0 | 0 | 8 | 10 |
| Flow.Launcher | 393 | **80.6%** | 28 | 107 | 0 | 3 | 4 | 17 |
| GeekDesk | 33 | **89.1%** | 14 | 23 | 0 | 2 | 0 | 7 |
| HandBrake | 58 | **100.0%** | 30 | 85 | 0 | 3 | 3 | 20 |
| MAA | 89 | **89.1%** | 68 | 165 | 0 | 4 | 1 | 24 |
| NETworkManager | 173 | **95.8%** | 246 | 475 | 0 | 0 | 24 | 54 |
| NuGet Package Explorer | 52 | **99.6%** | 40 | 55 | 0 | 0 | 0 | 11 |
| Playnite | 275 | **100.0%** | 142 | 228 | 0 | 0 | 5 | 41 |
| QuickLook | 56 | **89.6%** | 23 | 20 | 1 | 0 | 22 | 8 |
| ScreenToGif | 121 | **99.6%** | 368 | 347 | 0 | 2 | 0 | 14 |
| Snoop | 63 | **98.5%** | 23 | 41 | 0 | 0 | 8 | 12 |
| SyncTrayzor | 25 | **100.0%** | 17 | 12 | 0 | 1 | 0 | 3 |
| Text-Grab | 54 | **96.8%** | 179 | 59 | 1 | 1 | 26 | 4 |
| TwitchDownloader | 16 | **89.9%** | 44 | 56 | 0 | 26 | 0 | 1 |
| v2rayN | 24 | **89.5%** | 82 | 167 | 0 | 0 | 0 | 14 |
| VisualHFT | 49 | **100.0%** | 14 | 96 | 0 | 0 | 0 | 12 |
| WPF-Samples (Microsoft) | 745 | **99.2%** | 122 | 397 | 0 | 10 | 30 | 97 |
| AvalonDock (library) | 73 | **100.0%** | 19 | 18 | 0 | 0 | 4 | 2 |
| Fluent.Ribbon (library) | 70 | **29.2%** | 15 | 5 | 0 | 0 | 0 | 8 |
| HandyControl (library) | 342 | **91.5%** | 390 | 205 | 0 | 0 | 0 | 19 |
| MahApps.Metro (library) | 312 | **87.6%** | 161 | 379 | 1 | 0 | 0 | 54 |
| MaterialDesignInXaml (library) | 368 | **95.3%** | 444 | 239 | 0 | 0 | 0 | 100 |
| ModernWpf (library) | 298 | **81.2%** | 47 | 111 | 0 | 0 | 11 | 10 |
| WPF-UI (library) | 229 | **99.0%** | 70 | 34 | 0 | 0 | 0 | 4 |

The columns are findings, in pinned commits ([docs/corpus.tsv](docs/corpus.tsv)), read with their C#: without it
ScreenToGif is 36% instead of 99%, because `class ExtendedButton : Button` is not followed. Controls from a library nobody has told it about stay unknown until you declare them with
[`treatAs`](docs/config-schema.md). Resolution counts the four rules that can fail a build (names, inputs, focus,
mouse-only): the other three are almost never blocked and would only flatter the number.

**What it cannot see**

- Names, content and handlers set in code. It reads a XAML file's *own* code-behind, only to notice that
  it sets a control's name or content (the control is then unknown, not missing) and that a handler just drags the window.
- Implicit styles that reach into a `UserControl` from the resources of the window that hosts it.
- Names from data, and items generated from `ItemsSource`.
- Reading order, live regions, high contrast, whether a name is a *good* name, whether an image is decorative.
- Other containers without a name that Axe-Windows also reports: `ItemsControl` (mostly layout), `Menu`, `ToolBar`,
  `ProgressBar`, `Calendar`.
- Target size where the size or the neighbours are decided at run time: not judged.

## Options

| | |
|---|---|
| `--json`, `--sarif`, `--md <file>` | full report, code scanning, Markdown for a PR comment |
| `--msbuild` | MSBuild diagnostics for the Visual Studio error list: `XA0001` gated, `XA0002` not gated, `XA0003` not trustworthy |
| `--baseline`, `--write-baseline <file>` | only fail on new findings. Keys contain no line numbers |
| `--fail-on fail\|warn` | lowest severity that fails the run |
| `--min-coverage <0-100>` | exit 3 below this resolution |
| `--config <file>` | default `xamla11y.config.json` in the root — [schema](docs/config-schema.md) |
| `--exclude <text>` | skip paths that contain this |

Exit codes: **0** pass · **1** findings at or above `--fail-on` · **2** usage, config or input error ·
**3** result not trustworthy.

To accept one element, put a comment with a reason right before it. Other comments may sit in
between; another element or text may not. Without a reason, or in front of text or a commented-out element, it
does nothing and the run says so:

```xml
<!-- xamla11y-ignore: the whole row is the control, this check box only mirrors it -->
<CheckBox IsChecked="{Binding IsSelected}"/>
```

## GitHub Action

```yaml
permissions:
  contents: read
  pull-requests: write     # for the PR comment
  # security-events: write # for sarif: true

steps:
  - uses: actions/checkout@v4
  - uses: HSU-YU-MING/cornhsu-xamla11y@v0.1.0
    with:
      root: src
      version: 0.1.0
      baseline: a11y-baseline.json
```

Findings are annotated on their lines (GitHub shows the first ten or so per step; the report has all of them), the Markdown report becomes one PR comment that is updated in
place, the JSON report is uploaded as an artifact, and the job fails last, after all of that is out.

| Input | Default | |
|---|---|---|
| `root` | `.` | folder to scan |
| `working-directory` | `.` | `root`, `config` and `baseline` are relative to it |
| `version` | latest | tool version; pin it |
| `config` | file in root | `xamla11y.config.json` |
| `baseline` | | committed baseline file |
| `fail-on` | `fail` | `fail` or `warn` |
| `min-coverage` | config, else `60` | |
| `comment` | `true` | post the report on the PR |
| `upload-report` | `true` | upload JSON and Markdown as an artifact |
| `sarif` | `false` | upload to code scanning |
| `artifact-name` | `xamla11y-report` | also keys the PR comment |

## With XamlContrast, XamlKeys and XamlFit

| | asks |
|---|---|
| [XamlContrast](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast) | can you see it? (colour contrast) |
| [XamlKeys](https://github.com/HSU-YU-MING/cornhsu-xamlkeys) | can it be found? (resource keys) |
| [XamlFit](https://github.com/HSU-YU-MING/cornhsu-xamlfit) | does it fit? (text width) |
| **XamlA11y** | can you reach it, and can it be read out? |

Same exit codes, same action inputs, separate report files, artifacts, PR comments and SARIF tools, so the
four can run in one job: [docs/four-tools.md](docs/four-tools.md). A missing `{StaticResource}` behind an
`AutomationProperties.Name` is XamlKeys' finding; XamlA11y treats the name as present.

## Compared with

- **Accessibility Insights for Windows, Visual Studio's Accessibility Checker** — both run Axe-Windows on a
  *running* app, so they see what static analysis cannot (data, code, real sizes) and only the screens
  somebody opened. Use both.
- **[Bennewitz.Ninja.XamlQuality](https://github.com/JanusMael/Bennewitz.Ninja.Inspections)** — rules you
  call from a test project, mainly for Avalonia. It asks for an explicit `AutomationProperties.Name` on
  every interactive control; XamlA11y follows how WPF computes names, so a button with text is fine as it is.

## License

MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
