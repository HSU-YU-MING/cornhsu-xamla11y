# Which standard, and how strictly

[中文](標準對照.md)

**WCAG 2.2, levels A and AA.** XamlA11y looks for violations of seven of its 55 success criteria in WPF XAML, all about
names, the keyboard, visible focus and target size. It is a tool that finds problems. A run without findings is not a
statement of conformance with WCAG, EN 301 549, Section 508 or any law: the other 48 criteria are not checked, and the
seven are checked only as far as XAML decides them.

WCAG is written for the web. For desktop software, [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) (a W3C note, guidance
rather than requirements) says how to read it. It applies 2.1.1, 2.4.7, 2.5.3 as written and 2.5.8 and 4.1.2 with
replaced words; the 24 CSS pixels of 2.5.8 are *effective pixels* on Windows, which is WPF's own unit (DIP).

## How a finding is decided

1. **What is asked** is the text of the success criterion.
2. **What WPF does** is measured, not assumed: the name a UI Automation client reads, where Tab stops, which pixels change
   when focus arrives, the bounding rectangle. `prototype/UiaOracle` compares every rule with real WPF windows
   ([what was measured](M1-UIA實測.md), Chinese).
3. **Axe-Windows**, the engine of Microsoft's Accessibility Insights, is the second opinion. Its rules reference Section 508
   502.3.1 for names and WCAG 4.1.2 and 2.1.1 for the rest.

`fail` means: the XAML itself decides it, and the criterion leaves no room. `warn` and `info` mean: it may be fine, for a
reason XAML does not show.

| Finding | WCAG 2.2 | Default | Why this criterion | When it is not a violation |
|---|---|---|---|---|
| `missing-name` | 4.1.2 (A), 1.1.1 (A) | fail | 4.1.2: the name can be programmatically determined. 1.1.1 when the content is an icon: a control has a name that describes its purpose. | The name is set in code the tool cannot see (it says *unknown* when it sees that code). |
| `text-not-in-name` | 4.1.2 (A), 2.5.3 (A) | fail | 2.5.3: the name contains the text that is presented visually. The name is empty. | As above. |
| `meaningless-name` | 4.1.2 (A), 1.1.1 (A) | fail | A private-use character, white space, a type name or path data does not describe a purpose. | — |
| `symbol-name` | 1.1.1 (A) | info | `×` or an emoji may or may not describe the purpose. | When the symbol's own name is what the control does. |
| `input-no-name` | 4.1.2 (A), 1.3.1 (A) | fail | 1.3.1: a label next to a field is a relationship that has to be there programmatically too. | The name is set in code. |
| `focus-not-visible` | 2.4.7 (AA) | fail | The keyboard focus indicator is visible in some mode of operation. | Focus is drawn by code or by a theme that was not measured (*unknown*, or `themeShowsFocus`). |
| `mouse-only` | 2.1.1 (A) | fail | All functionality is operable through a keyboard interface. | The same action is available elsewhere, or the input depends on the path of the pointer. |
| `not-in-tab-order` | 2.1.1 (A) | warn | Taken out of the tab order, a control is only reachable some other way. | There is another way: a shortcut, a menu, arrow keys. |
| `small-target` | 2.5.8 (AA) | warn | Smaller than 24 × 24 and without the spacing exception. | The equivalent, inline, user agent or essential exceptions. |
| `list-no-name` | 4.1.2 (A), 1.3.1 (A) | warn | A list is a user interface component; Axe-Windows reports it under Section 508 502.3.1. | Some auditors accept a list without a name when it is the only one and its purpose is obvious. |

Two common claims this tool does not make:

- **3.3.2 Labels or Instructions** asks for a label that people can see. A field with a visible label and no accessible name
  meets 3.3.2 and fails 1.3.1 and 4.1.2.
- **2.4.3 Focus Order** is about the order. A control that is not in the order at all is 2.1.1.

## A ToolTip is not a name, but it may be heard

WPF exposes a ToolTip as the UI Automation *HelpText*. NVDA reads HelpText as the description, after the type ("button,
Delete"), so some users do hear it (run with NVDA 2026.2, see [what NVDA says](M1-UIA實測.md#5d-nvda-怎麼念)). 4.1.2 asks for the *name*, and
Axe-Windows reports the same control. The finding stays a `fail`.

## The other standards

| | Uses | What differs |
|---|---|---|
| **EN 301 549** (EU; chapter 11 is software) | V3.2.1 (2021): WCAG 2.1 A and AA. V4.1.1 was published in September 2026 and uses WCAG 2.2; the Commission's [AccessibleEU](https://accessible-eu-centre.ec.europa.eu/content-corner/news/european-accessibility-standard-en-301-549-has-been-updated-2026-09-07_en) wrote on 2026-09-07 that V3.2.1 stays the reference until V4.1.1 is cited in the Official Journal. | Under V3.2.1, 2.5.8 is not required (`"small-target": "off"`). |
| **Section 508** (US) | [WCAG 2.0 A and AA](https://www.access-board.gov/ict/) for software (E207.2), plus 502.3.1: role, state, name and description programmatically determinable. | 2.5.3 and 2.5.8 are not in WCAG 2.0. |
| **網站無障礙規範** (Taiwan) | The 115 revision aligns with WCAG 2.2 and takes effect on 2026-11-30. | Its scope is public *web sites*; desktop software is not mentioned. |

Whether a product falls under one of these is a legal question about the product and where it is sold. This page does not
answer it.

## What is not checked, and who can

Other tools of the family cover part of the list: [XamlContrast](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast)
1.4.3 and 1.4.11, [XamlFit](https://github.com/HSU-YU-MING/cornhsu-xamlfit) text that is cut off in another language.
The rest needs a person. The ones that go wrong most in desktop software:

| WCAG 2.2 | Check it like this |
|---|---|
| 1.3.2 Meaningful Sequence (A) | Read the window with a screen reader: does the order make sense? |
| 1.4.1 Use of Color (A) | Is anything (an error, a state) shown only by color? |
| 1.4.4 Resize Text (AA) | WPF does not follow the Windows text size. Does the app offer its own scaling to 200 %? |
| 1.4.11 Non-text Contrast (AA) | Borders, icons and the focus indicator against their background, 3:1. |
| 1.4.13 Content on Hover or Focus (AA) | Can a ToolTip or popup be dismissed, hovered, and does it stay? |
| 2.1.1 Keyboard (A) | Put the mouse away and do every task. Handlers attached in code are invisible to this tool. |
| 2.1.2 No Keyboard Trap (A) | Can Tab or Esc leave every control, also editors and grids? |
| 2.1.4 Character Key Shortcuts (A) | Single-letter shortcuts: can they be turned off? |
| 2.4.3 Focus Order (A) | Does Tab follow the meaning? Where is focus after a dialog closes? |
| 2.4.6 Headings and Labels (AA) | Do the names say what the control does? A name that exists passes this tool. |
| 2.4.11 Focus Not Obscured (AA) | Does a sticky bar or popup cover the focused control? |
| 2.5.7 Dragging Movements (AA) | Is there a way without dragging? |
| 3.2.1 On Focus, 3.2.2 On Input (A) | Does something unexpected happen when a control gets focus or a value? |
| 3.3.1 Error Identification (A), 3.3.3 Error Suggestion (AA) | Is the error said in text, and tied to the field? |
| 3.3.7 Redundant Entry (A), 3.3.8 Accessible Authentication (AA) | Is anything asked twice? Does signing in need a puzzle? |
| 4.1.2, the rest (A) | Roles, states and values of custom controls: `OnCreateAutomationPeer`. |
| 4.1.3 Status Messages (AA) | Is "Saved" announced? `AutomationProperties.LiveSetting` and `RaiseAutomationEvent`. |

The levels and names on this page are checked against [W3C's data](https://www.w3.org/WAI/WCAG22/wcag.json)
(`tests/XamlA11y.Tests/wcag22.json`).
