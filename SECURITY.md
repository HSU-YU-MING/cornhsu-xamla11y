# Security

XamlA11y **reads** `.xaml` files, class declarations in `.cs` files, each XAML file's own code-behind, project files
(for package versions) and `xamla11y.config.json`, and writes the reports you name. It loads no assemblies, instantiates
no XAML and runs nothing from the project it scans. **It makes no network requests.** It creates no directories.

What is left is that it parses input it does not trust: people run it in CI, on pull requests from strangers.

| | |
|---|---|
| External entities, entity expansion | DTDs are refused; such a file is a parse error |
| Deep nesting, cyclic styles, cyclic resources, cyclic `LabeledBy` | depth limits; a file nested more than 512 levels is a parse error |
| Symlinks and junctions | not followed, neither directories nor single files |
| Text from XAML in a CI log | control characters, Unicode line separators and bidirectional overrides are escaped; `##vso[` and `##[` are defused. In the action the output is also inside `::stop-commands::` |
| Text from XAML in a PR comment | always inside a code span, so links, images, mentions and HTML stay text. The comment is cut at whole lines |
| Regular expressions over repository text | fixed patterns with a time limit where they read code; names taken from XAML are escaped before use |
| Action inputs | reach the shell only through environment variables and are validated before anything is installed |

`xamla11y.config.json` and the baseline are **part of the pull request** when they live in the repository: they can turn the
gate off. Review changes to them like changes to the workflow, or pass `--config` from somewhere the author cannot reach.

Report a vulnerability through GitHub's private vulnerability reporting on this repository.
