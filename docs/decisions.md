# Decision Log

This document records the rationale behind the current plan and distinguishes explicit user decisions from proposals. Behavior and numerical parameters are defined in [product.md](product.md); acceptance scenarios are in [user-stories.md](user-stories.md).

## Statuses

- **Confirmed by the user** — supported by an explicit instruction or answer.
- **Planned; requires clarification before implementation** — a proposal from the baseline plan, not separate technical approval.
- **Open** — no decision has been made.

## Confirmed by the user

| ID | Decision | Rationale and implications |
|---|---|---|
| D-01 | Standalone desktop application, initially Windows only | The user preferred desktop to an extension. The application launches the browser on the PC. |
| D-02 | Strictly local storage | User data does not require a cloud service. |
| D-03 | Topic folders containing websites and credentials | The main task is returning to a topic and opening one website or an entire folder. |
| D-04 | Master password for unlocking | Selected by the user. Forgotten master passwords cannot be recovered in the MVP. |
| D-05 | Manual website sign-in | The user chose default-browser links and copying credentials instead of autofill. |
| D-06 | Manual export of an encrypted file | Selected by the user for backup and transfer. |
| D-07 | A production product intended for tens of thousands of users | Reliability, data protection, and reproducible releases are considered from the start. |
| D-08 | Originally planning and documentation only; refined by D-13 and D-14 | The user stopped premature implementation. Explicit task authorization is now recorded below; unresolved technical proposals still need confirmation. |
| D-09 | Project documents belong in `docs/` | The MVP plan was renamed to `product.md`. Agent instructions remain in the root `AGENTS.md`. |
| D-10 | Initially exclude `security.md`, `design.md`, and `tasks.md`; partially superseded by D-11 | Retained for historical context. The exclusion of `security.md` remains in effect. |
| D-11 | Add `tasks.md` and a compact `design.md` in `docs/` | The user requested a detailed checklist and a summary of the attached `DESIGN_SYSTEM.md` to reduce context usage. Supersedes D-10 for these two files; does not authorize coding or confirm P-01–P-08. |
| D-12 | English project documentation and application interface | The user explicitly requested translation of all `docs/` documents and specified English as the application language. Application-owned text, including installer text, is English; user content is preserved. This does not add a language switcher or authorize implementation. |

### Workflow decision

**D-13 — Confirmed by the user:** add the `next-task` project chat command. Each explicit invocation selects one unfinished `X.Y` item, executes it, and runs fast checks or expanded stage-completion checks when it is the final item. Then offer documentation completion marks, commit, and push. Invocation authorizes the selected work but does not confirm unresolved product choices or automatically authorize finalization. See [next-task.md](next-task.md). This refines D-08: creating the command remains documentation work; explicitly invoking it authorizes the selected task.

**D-14 — Confirmed by the user (2026-10-25):** the explicit `next-task` invocation satisfies task 1.1. The user approved its completion mark and an initial documentation commit and push. The user also requested a single pass through all of stage 1 provided it needs no code changes. This authorizes stage 1 documentation and decision preparation together, without starting stage 2 or silently accepting P-01–P-08.

## Planned; requires clarification before implementation


| ID | Proposal | What needs confirmation |
|---|---|---|
| P-01 | Windows 11 x64; C# / .NET 10 LTS, WPF, SQLite with SQLCipher | Exact minimum Windows version, architectures, and stack. The user has confirmed Windows in general. |
| P-02 | Three-pane window, flat folders, alphabetical sorting | Fit with the user's usual workflow. |
| P-03 | One username/password pair per entry | Separate entries are proposed for multiple accounts. |
| P-04 | Lock after 5 minutes; clear copied passwords after 30 seconds | Exact intervals, temporary password reveal, and handling of unsaved edits when locking. |
| P-05 | Confirm opening more than 10 websites | Threshold usability and partial launch failures. |
| P-06 | Restore replaces the catalog after creating a backup | Merging catalogs is excluded from the baseline plan. |
| P-07 | Signed installer with the .NET runtime; manual updates | Distribution and update method, signing certificate. |
| P-08 | Search within 300 ms for 10,000 entries | Measurement method and test configuration are described in `product.md`; performance has not been verified. |

## Open questions

These questions do not prevent product discussion. Resolve each before implementing the affected behavior; an agent must not silently substitute its own answer.

- Application and publisher names for public release.
- Master password rules, password reveal duration, and unsaved edits during locking.
- Duplicate folder/entry names, empty searches, browser errors, and launching the application again while it is already running.
- Backup compatibility across versions and interrupted updates.

## Maintaining this log

Record new decisions with their rationale and implications. Preserve superseded decisions with a replacement note and a reference to the new decision. Update affected requirements and scenarios together; do not mark a proposal confirmed without explicit evidence.
