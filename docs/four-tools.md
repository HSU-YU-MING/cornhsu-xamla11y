# Running XamlContrast, XamlKeys, XamlFit and XamlA11y together

Four tools, four questions: can you **see** it, can it be **found**, does it **fit**, can you **reach** it.
They share exit codes (0 pass, 1 findings, 2 usage, 3 not trustworthy), the shape of the GitHub Action and
the idea of a coverage gate. They do not share files, so they can run in one job.

[cornhsu-xaml-audit](https://github.com/HSU-YU-MING/cornhsu-xaml-audit) runs all four in one step, at versions pinned in it, and
posts one PR comment. Its README has the workflow to copy; its CI runs the four of them on every change.

With other versions, or only some of them: call each tool's own action (its README has the snippet). They can stand in one job. Put
`if: ${{ !cancelled() }}` on each, so one push shows all the results.

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

Put one comment per tool in front of the element, in any order:

```xml
<!-- xamlcontrast-ignore: decorative -->
<!-- xamla11y-ignore: the text above is the accessible way to open it -->
<TextBlock Text="»" Foreground="#DDDDDD" MouseLeftButtonUp="OnOpen"/>
```

The four tools look for their comment the same way:

- Other comments and processing instructions in between are skipped. Content is not: an element, text, or a
  commented-out element (a comment that starts with `<`). A comment in front of that was about that.
- A comment that stops at text or at a commented-out element does nothing, and the run says so. So does one
  without a reason.
- With several comments of the same tool on one element, the closest one decides.

This needs XamlContrast 1.9.0, XamlKeys 0.4.0 and XamlFit 0.13.0 or later. XamlContrast 1.8.3 and XamlFit 0.12.0
only look at the comment closest to the element, so of two that are stacked the further one has no effect; XamlKeys
0.3.1 also skips text and commented-out elements, so its comment can hide the wrong element.

## Order of adoption

1. XamlKeys first: a missing key throws at run time, and the other three read the same resources.
2. XamlContrast and XamlFit next.
3. XamlA11y with `--write-baseline` on day one. Its first run is usually the longest list of the four.
