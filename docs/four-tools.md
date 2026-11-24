# Running XamlContrast, XamlKeys, XamlFit and XamlA11y together

Four tools, four questions: can you **see** it, can it be **found**, does it **fit**, can you **reach** it.
They share exit codes (0 pass, 1 findings, 2 usage, 3 not trustworthy), the shape of the GitHub Action and
the idea of a coverage gate. They do not share files, so they can run in one job.

`samples/suite` has one problem for each; the `four-tools` job in `.github/workflows/ci.yml` runs all four
actions on it and checks that each one ran, failed on its own problem and left its own report.

## One job

```yaml
permissions:
  contents: read
  pull-requests: write      # one comment per tool
  security-events: write    # only for sarif: true

jobs:
  xaml:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v4

      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v1.8.3
        if: ${{ !cancelled() }}
        with: { root: src, version: 1.8.3 }

      - uses: HSU-YU-MING/cornhsu-xamlkeys@v0.3.1
        if: ${{ !cancelled() }}
        with: { root: src, version: 0.3.1 }

      - uses: HSU-YU-MING/cornhsu-xamlfit@v0.12.0
        if: ${{ !cancelled() }}
        with: { root: src, version: 0.12.0, resx: src/Resources }

      - uses: HSU-YU-MING/cornhsu-xamla11y@v0.1.0
        if: ${{ !cancelled() }}
        with: { root: src, version: 0.1.0, baseline: a11y-baseline.json }
```

`if: ${{ !cancelled() }}` lets the later tools run when an earlier one failed, so one push shows all four
results. The job is still red when any of them is.

| | report files | artifact and PR comment | SARIF tool |
|---|---|---|---|
| XamlContrast | `xamlcontrast-report.*` | `xamlcontrast-report` | XamlContrast |
| XamlKeys | `xamlkeys-report.*` | `xamlkeys-report` | XamlKeys |
| XamlFit | `xamlfit-report.*` | `xamlfit-report` | XamlFit |
| XamlA11y | `xamla11y-report.*` | `xamla11y-report` | XamlA11y |

## Which tool reports what

| Problem | Reported by | The others |
|---|---|---|
| Icon or text too pale | XamlContrast | |
| `AutomationProperties.Name="{StaticResource Typo}"` | XamlKeys | XamlA11y counts it as named: the key is XamlKeys' question |
| Button caption clipped in German | XamlFit | |
| Icon button with only a ToolTip | XamlA11y | |
| Focus rectangle present but too pale | XamlContrast (`nonText.uiComponents`) | XamlA11y only asks whether one is drawn |
| Third-party control nobody knows | XamlFit and XamlA11y | each has its own `treatAs`: it can be a Button for its name and something else for its layout |

## Config files

`xamlcontrast.config.json`, `xamlkeys.config.json`, `xamlfit.config.json` and `xamla11y.config.json` live side
by side. Each tool reads only its own.

## Ignore comments on the same element

Each tool has its own comment and each needs a reason: `xamlcontrast-ignore`, `xamlkeys-ignore`,
`xamlfit-ignore`, `xamla11y-ignore`.

XamlA11y finds its comment in any position among the comments before an element, but not past text or a
commented-out element: the comment in front of that one was about that one. **XamlContrast 1.8.3 and
XamlFit 0.12.0 only look at the comment closest to the element**, so put `xamla11y-ignore` first:

```xml
<!-- xamla11y-ignore: the text above is the accessible way to open it -->
<!-- xamlcontrast-ignore: decorative -->
<TextBlock Text="»" Foreground="#DDDDDD" MouseLeftButtonUp="OnOpen"/>
```

For the same reason an element cannot carry both a `xamlcontrast-ignore` and a `xamlfit-ignore` today —
whichever is further away has no effect. Put one of the two in that tool's baseline.

## Order of adoption

1. XamlKeys first: a missing key throws at run time, and the other three read the same resources.
2. XamlContrast and XamlFit next.
3. XamlA11y with `--write-baseline` on day one. Its first run is usually the longest list of the four.
