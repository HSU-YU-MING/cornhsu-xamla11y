# How it was checked, and how much it sees

[中文](驗證.md)

## How it knows

Every rule was written from what WPF *does*, not from what it is documented to do, and the tool is
compared with the real thing: `prototype/UiaOracle` opens each piece of XAML in a real WPF window,
reads the UI Automation tree from a client on another thread (what a screen reader sees), walks the tab
order, moves keyboard focus and compares pixels, and runs Axe-Windows, the engine of Accessibility
Insights and of Visual Studio's checker.

| compared with | cases | answered | unknown | **contradictions** |
|---|---:|---:|---:|---:|
| what NVDA 2026.2 says when Tab reaches it, in English and in Traditional Chinese | 86 | 81 | 5 | **0** |
| the real Tab key through whole windows: where it stops (`walk`), and what NVDA says there in both languages | 88 | 69 | 19 | **0** |
| UI Automation names, hand-written | 383 | 368 | 15 | **0** |
| UI Automation names, every control × content × way of naming | 8,218 | 7,843 | 375 | **0** |
| UI Automation names, distinct shapes from 19 real projects | 2,172 | 2,172 | 0 | **0** |
| UI Automation names, whole files of 49 public projects and 3 apps, with their styles and resources | 10,398 | 10,398 | – | **0** |
| names under MahApps, MaterialDesign, HandyControl, WPF-UI, Fluent | 97 | 74 | 23 | **0** |
| focus visible (pixels), default theme and the five themes | 107 | 84 | 23 | **0** |
| tab order | 29 | 27 | 2 | **0** |
| the order of all Tab stops in a window (`walk`) | 92 | 88 | 4 | **0** |
| whether the name contains the visible text (`name-differs`) | 27 | 22 | 5 | **0** |
| items made from data (UI Automation name) | 39 | 39 | 0 | **0** |
| Tab stops that are not in the UI Automation tree | 56 | 55 | 1 | **0** |
| target size (bounding rectangle) | 17 | 10 | 7 | **0** |

"Unknown" is a correct answer; "fine" for something a screen reader cannot name is not. The oracle has
earned its keep: the first version of the rules got 177 cases wrong. What it cannot catch is XAML nobody wrote a case
for: two independent reviews then found some forty more ways to fool the tool, and each is now a test. The whole files
are 1,919 that a real WPF window could load without running any of the projects' code; elements that need their code, a
theme that was not measured or an open menu are left out of that row.

The recorded answers of WPF and of NVDA ship with the tests (`tests/XamlA11y.Tests/uia-facts.json`, `nvda-facts.json`, `nvda-walk-facts.json`), so
CI on Linux checks the same facts. "NVDA says" in the reports is only written for what was run. Narrator and JAWS were not run. Details: [WPF實測.md](WPF實測.md) (Chinese).

## How much it sees

**Resolution** = of the elements that should get an answer, the share that did. It is printed on every
run and gated by `--min-coverage` (60 by default): below that the run exits 3, *not trustworthy*, rather
than 0. Names that come from data (`Content="{Binding Title}"`) are counted but are not in the
denominator — no static tool can know them.

Out of the box, no configuration:

| project | XAML files | resolution | control names | inputs | focus | mouse-only | tab order, size (warn) | lists, widgets (warn) | item names, silent stops (warn) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Cornea (maintainer app) | 5 | **100.0%** | 0 | 19 | 0 | 0 | 0 | 6 | 0 |
| Kindling (maintainer app) | 16 | **100.0%** | 7 | 17 | 0 | 1 | 0 | 6 | 4 |
| QuillNest (maintainer app) | 32 | **99.5%** | 65 | 81 | 0 | 3 | 0 | 12 | 11 |
| Accessibility Insights | 71 | **59.4%** | 2 | 5 | 0 | 2 | 10 | 5 | 0 |
| BetterGI | 153 | **97.7%** | 430 | 551 | 0 | 1 | 8 | 31 | 46 |
| Bloxstrap | 40 | **89.9%** | 46 | 25 | 0 | 0 | 0 | 9 | 5 |
| dnGrep | 72 | **100.0%** | 53 | 90 | 0 | 0 | 0 | 9 | 8 |
| dnSpy | 124 | **99.3%** | 139 | 1365 | 0 | 1 | 94 | 54 | 2 |
| Dopamine | 150 | **38.5%** | 67 | 38 | 4 | 2 | 0 | 11 | 20 |
| EarTrumpet | 12 | **71.9%** | 11 | 9 | 0 | 0 | 8 | 10 | 7 |
| Flow.Launcher | 393 | **80.7%** | 28 | 107 | 0 | 5 | 4 | 20 | 25 |
| GeekDesk | 33 | **89.1%** | 14 | 23 | 0 | 2 | 0 | 7 | 5 |
| HandBrake | 58 | **100.0%** | 30 | 85 | 0 | 3 | 3 | 30 | 18 |
| MAA | 89 | **89.1%** | 68 | 165 | 0 | 4 | 1 | 32 | 18 |
| NETworkManager | 173 | **95.8%** | 246 | 475 | 0 | 0 | 24 | 54 | 17 |
| NuGet Package Explorer | 52 | **99.6%** | 40 | 55 | 0 | 0 | 0 | 16 | 20 |
| Playnite | 275 | **100.0%** | 142 | 228 | 0 | 0 | 5 | 46 | 38 |
| QuickLook | 56 | **89.6%** | 23 | 20 | 1 | 0 | 22 | 12 | 1 |
| ScreenToGif | 121 | **99.6%** | 368 | 347 | 0 | 6 | 0 | 47 | 11 |
| Snoop | 63 | **98.5%** | 23 | 41 | 0 | 0 | 8 | 13 | 7 |
| SyncTrayzor | 25 | **100.0%** | 17 | 12 | 0 | 1 | 0 | 6 | 11 |
| Text-Grab | 54 | **94.4%** | 159 | 59 | 1 | 1 | 26 | 12 | 2 |
| TwitchDownloader | 16 | **89.9%** | 44 | 56 | 0 | 26 | 0 | 7 | 1 |
| v2rayN | 24 | **89.5%** | 82 | 167 | 0 | 0 | 0 | 21 | 4 |
| VisualHFT | 49 | **100.0%** | 14 | 96 | 0 | 0 | 0 | 13 | 7 |
| WPF-Samples (Microsoft) | 745 | **99.2%** | 122 | 397 | 0 | 10 | 30 | 128 | 16 |
| AvalonDock (library) | 73 | **100.0%** | 19 | 18 | 0 | 0 | 4 | 9 | 5 |
| Fluent.Ribbon (library) | 70 | **29.2%** | 16 | 4 | 0 | 0 | 0 | 10 | 6 |
| HandyControl (library) | 342 | **91.5%** | 390 | 205 | 0 | 0 | 0 | 48 | 5 |
| MahApps.Metro (library) | 312 | **87.6%** | 161 | 379 | 1 | 0 | 0 | 104 | 27 |
| MaterialDesignInXaml (library) | 368 | **95.3%** | 444 | 239 | 0 | 0 | 0 | 163 | 165 |
| ModernWpf (library) | 298 | **81.2%** | 47 | 111 | 0 | 1 | 11 | 14 | 3 |
| WPF-UI (library) | 229 | **99.0%** | 70 | 34 | 0 | 0 | 0 | 12 | 3 |

Of the 49 public projects, 46 have WPF XAML (one has none and two are WinUI: exit 3). The columns are findings, in pinned commits ([docs/corpus.tsv](corpus.tsv)), read with their C#: without it
ScreenToGif is 36% instead of 99%, because `class ExtendedButton : Button` is not followed. Controls from a library nobody has told it about stay unknown until you declare them with
[`treatAs`](config-schema.md). Resolution counts the four checks that can fail a build (names, inputs, focus,
mouse-only); the others do not fail a build and would only flatter the number.
