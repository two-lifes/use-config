# Concise Design System

A working summary of the attached `DESIGN_SYSTEM.md`. It retains core rules and tokens while omitting repetition, CSS examples, and graphical-editor patterns. This describes the future UI; it does not authorize implementation.

Behavior comes from [product.md](product.md) and [user-stories.md](user-stories.md); decision statuses are in [decisions.md](decisions.md). Priority: user workflow → readability and accessibility → existing patterns → visual rules. The adaptation below targets the proposed WPF stack without changing its approval status.

## 1. Character and layout

- One dark theme: a calm, information-dense desktop tool with flat surfaces. Work takes priority over decoration; minimalism does not mean empty screens or oversized spacing.
- Current plan: folders on the left, websites in the center, details on the right; compact search and actions above. Prefer a list/table to large cards.
- Use a neutral background for selection. Group forms by meaning; replace nested borders with spacing and separators.
- Panels have predictable dimensions; the working area expands. When space narrows, reduce spacing and secondary areas first; use horizontal scrolling only where content requires it.
- Use real data and concrete labels. Empty states explain the next action; development fixtures must be clearly identified.
- All application-owned text is English. Preserve user-entered content in its original language.

## 2. Color tokens

| Token | Value | Purpose |
|---|---|---|
| `bg-canvas` | `#0c0c0d` | Overall background |
| `bg-sidebar` | `#101012` | Navigation |
| `bg-surface` | `#141416` | Panels |
| `bg-elevated` | `#1b1b1e` | Menus, fields, selection |
| `bg-hover` | `#222226` | Hover |
| `border-default` / `border-strong` | `#2b2b30` / `#3b3b41` | Standard / stronger border |
| `text-primary` / `text-secondary` | `#f0f0f1` / `#ababaf` | Primary / secondary text |
| `text-muted` / `text-on-accent` | `#828288` / `#101011` | Low-priority text / text on accent |
| `accent-primary` / `accent-hover` | `#faff7f` / `#eff574` | Primary action / hover |
| `status-success` | `#0c9762` | Success or activity |
| `status-warning` / `status-danger` | `#d6b06c` / `#d7817b` | Warning / error |
| `focus-ring` | `#faff7f` | Keyboard focus |

Keep 90–95% of the UI neutral. Use yellow for at most one visually primary action per working area, without large accent fills. Semantic colors are not decoration; convey status with text or a meaningful icon as well.

Contrast: normal text ≥ 4.5:1, large text ≥ 3:1, and meaningful control boundaries and states ≥ 3:1 where applicable. Check actual combinations and adjust tokens for their role when necessary; the palette alone does not guarantee accessibility.

## 3. Typography and dimensions

Outfit is the reference; system Segoe UI is acceptable without external downloads. Use Consolas or an already bundled JetBrains Mono for technical text. Fonts must work offline and support Cyrillic user content as well as English UI text.

| Property | Starting point |
|---|---|
| Window/section heading | 20–24, weight 500–600 |
| Panel heading / body text | 13–14, weight 500 / 400–500 |
| Secondary / technical text | 12–13 |
| Occasional small labels | 11–12 |
| Line height | 1.35–1.5 times font size |
| Control height | 34; compact 28; inputs 34–38 |
| Corner radius: control / panel / dialog | 6 / 8 / 10 |
| Spacing scale | 4 / 8 / 12 / 16 / 20 / 24 / 32 |
| Label-to-field / panel padding / between areas | 6–8 / 12–16 / 16–24 |
| Sidebar width / top bar height | 216 / 48 as starting points |
| Outer padding / border width | 24 / 1 |
| List or table row height / cell padding | 36–44 / 10–14 |
| Monochrome icons | 14–16; stroke approximately 1.75–2 |

The source uses CSS pixels; use these as starting values in WPF DIPs and verify display scaling. Centralize colors, dimensions, and styles in WPF resources rather than copying CSS or adding a web stack. Deviations are acceptable for readability and workflow. Avoid pervasive weights of 700–900, uppercase, and letter spacing; use tabular numerals in tables.

## 4. Components and states

- **Buttons:** primary uses an accent fill and dark text; secondary uses a neutral fill and border; ghost is transparent. Destructive actions are normally neutral, with red appropriate in confirmation. Use icons only to aid recognition, from one monochrome set.
- **Inputs:** external labels are required; placeholders do not replace them. Errors explain the cause and remedy. Passwords are masked by default; product requirements govern reveal and copy behavior.
- **Lists:** consistent rows, subtle hover, clear selection, restrained separators. Truncated text must have an accessible full value without revealing passwords.
- **Dialogs:** reserve for decisions that interrupt work; use inline forms or panels for routine editing. Support Escape, modal focus containment, and focus restoration. Closing must follow the agreed policy for unsaved input.
- **Hover / pressed:** change tone or border. **Focus:** a visible keyboard outline, initially 2 units wide with a 2-unit offset. **Disabled:** visibly distinct and explained where possible.
- **Loading:** local indicator. **Empty:** explanation and next action. **Error:** specific message and recovery. **Success:** brief, restrained confirmation.
- **Motion:** meaningful feedback only, 120–160 ms, ease-out. Respect the Windows setting for disabling animations, corresponding to `prefers-reduced-motion` in the source.

## 5. Exclusions

No gradients, neon, glow, glass effects, heavy blur, decorative background grids or illustrations, unjustified large radii or pill buttons, oversized headings, nested cards, unnecessary KPIs/badges/icons, invented statuses or charts, marketing slogans, pulsing, spring effects, hover scaling, or per-row entrance animations. Shadows are permitted only to distinguish menus and dialogs.

## 6. UI completion checks

- [ ] Consistent tokens and components; actions and data take visual priority.
- [ ] Verified at 1280×800, 1440×900, and 1920×1080; additionally verify Windows scaling, long English labels, and multilingual user content in WPF.
- [ ] No clipped fields, overlaps, or unintended horizontal overflow.
- [ ] Contrast, keyboard navigation, focus, disabled animations, and all component states verified.
- [ ] No decorative noise or workflow regressions hidden by visual changes.

This document is sufficient for routine UI work. Consult the full `DESIGN_SYSTEM.md` only for a detail omitted from this summary.
