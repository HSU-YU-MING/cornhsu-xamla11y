# Contributing

The most useful thing you can send is a place where the tool is wrong, as the smallest XAML that shows it:

- **False alarm** — it reports something a screen reader or the keyboard handles fine. Open the window, look at the
  element in Accessibility Insights (Inspect) and paste its *Name*.
- **Missed** — Accessibility Insights reports it and XamlA11y says nothing (and does not list it as blocked either).

Code: `dotnet test XamlA11y.slnx`. A change to a rule needs a case in `prototype/UiaOracle/facts/` first, and
`uiaoracle check` without a WRONG (Windows only; it opens small windows and takes the focus for a few minutes).
`CLAUDE.md` (Chinese) lists the traps.

English or 中文, whichever is easier.
