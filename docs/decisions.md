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
| D-08 | Originally planning and documentation only; refined by D-13 and D-14 | The user stopped premature implementation. Explicit task authorization is now recorded below; stage 1 technical choices were subsequently confirmed in D-17–D-20. |
| D-09 | Project documents belong in `docs/` | The MVP plan was renamed to `product.md`. Agent instructions remain in the root `AGENTS.md`. |
| D-10 | Initially exclude `security.md`, `design.md`, and `tasks.md`; partially superseded by D-11 | Retained for historical context. The exclusion of `security.md` remains in effect. |
| D-11 | Add `tasks.md` and a compact `design.md` in `docs/` | The user requested a detailed checklist and a summary of the attached `DESIGN_SYSTEM.md` to reduce context usage. Supersedes D-10 for these two files; does not authorize coding or confirm P-01–P-08. |
| D-12 | English project documentation and application interface | The user explicitly requested translation of all `docs/` documents and specified English as the application language. Application-owned text, including installer text, is English; user content is preserved. This does not add a language switcher or authorize implementation. |
| D-15 | Broader Windows compatibility, master passwords of 4–128 characters, website descriptions, and quick search | The user explicitly requested these changes and less intrusive protection on 2026-10-25. Descriptions are stored and searched alongside names and URLs. The exact Windows minimums and relaxed timer values were subsequently confirmed in D-16; remaining choices were subsequently resolved in D-17–D-20. |
| D-16 | Windows 10 22H2 and Windows 11 21H2+; 15-minute inactivity lock, 30-second password reveal, 60-second clipboard expiry | The user selected both recommended options explicitly on 2026-10-25. Encryption and locking on Windows lock/sleep remain required. Windows 7/8.1 are outside the selected target. Actual runtime/native dependency and installer compatibility still needs verification. |
| D-17 | Approved foundation and catalog structure | Explicitly selected by the user during the stage 1 review: x64, C# / .NET 10 LTS, WPF, SQLite with SQLCipher, three panes, flat folders, name sorting, and one credential pair per entry. Stage 2 verifies actual runtime/native dependency compatibility on both agreed Windows targets. |
| D-18 | Encrypted drafts and password masking | The user selected encrypted draft preservation, then approved the complete closure plan. Drafts remain separate from saved entries, resume only after unlocking, and are removed on save or explicit discard. A draft-write failure never delays locking and is reported after unlocking. Manual locking offers Save and lock / Discard and lock / Cancel. Reveal ends after 30 seconds, on selecting another site, or on lock; application focus loss alone is not a trigger. This replaces the rejected automatic draft-discard proposal. |
| D-19 | Catalog edge cases and restore policy | The user accepted the remaining decision package: unique folder names, duplicate website names/URLs allowed, one per-user application instance, agreed empty/no-results search behavior, confirmation above 10 sites, continued bulk attempts and failure-only retry, validated replacement with a prior encrypted backup, supported-format migrations, and recoverable old/new states on interrupted operations. |
| D-20 | Release policy, search measurement, and stage 1 closure | The user accepted a signed per-user x64 MSI with bundled runtime/native dependencies, manual updates preserving data, vault retention on uninstall, and search p95 ≤ 300 ms for 10,000 entries using the method in product.md. Use Config is the working name. Final name, publisher, and certificate are explicitly deferred until before stage 11. The user then instructed implementation of the closure plan and authorized checking 1.1–1.8 and C1 without starting stage 2. |

### Workflow decision

**D-13 — Confirmed by the user:** add the `next-task` project chat command. Each explicit invocation selects one unfinished `X.Y` item, executes it, and runs fast checks or expanded stage-completion checks when it is the final item. Then offer documentation completion marks, commit, and push. Invocation authorizes the selected work but does not confirm unresolved product choices or automatically authorize finalization. See [next-task.md](next-task.md). This refines D-08: creating the command remains documentation work; explicitly invoking it authorizes the selected task.

**D-14 — Confirmed by the user (2026-10-25):** the explicit `next-task` invocation satisfies task 1.1. The user approved its completion mark and an initial documentation commit and push. The user also requested a single pass through all of stage 1 provided it needs no code changes. This authorizes stage 1 documentation and decision preparation together, without starting stage 2 or silently accepting P-01–P-08.

## Former proposals — resolved in stage 1

These IDs are retained for existing references and decision history. They are approved planning decisions, not claims of completed implementation or testing.

| ID | Final status | Decision |
|---|---|---|
| P-01 | Confirmed; D-16–D-17 | Windows 10 22H2 / Windows 11 21H2+, x64, C# / .NET 10 LTS, WPF, SQLCipher. Choose exact provider/native packages and verify compatibility and licenses in 2.2–2.3. |
| P-02 | Confirmed; D-17 | Three panes, one folder level, sorting by name. |
| P-03 | Confirmed; D-17 | One credential pair per entry; separate entries for multiple accounts. |
| P-04 | Confirmed; D-15–D-16, D-18 | Master passwords 4–128 characters; inactivity 15 minutes, reveal 30 seconds, clipboard expiry 60 seconds; encrypted drafts and manual-lock choices as specified in product.md. |
| P-05 | Confirmed; D-19 | Confirm above 10 sites; handle empty folders and partial failures as specified in product.md. |
| P-06 | Confirmed; D-19 | Restore replaces after validation, confirmation, and an encrypted backup. No merging; preserve recoverable state during interrupted operations. |
| P-07 | Confirmed; D-20 | Signed per-user x64 MSI, bundled runtime/native dependencies, manual updates; identity and signing prerequisites due before stage 11. |
| P-08 | Confirmed; D-20 | Search p95 ≤ 300 ms for 10,000 entries. Verification is future work in stage 10, using the measurement method in product.md. |

## Deferred release prerequisites

Final application name, actual publisher identity, and signing certificate must be resolved **before stage 11**. Use Config is the working name. Do not invent an identity or treat an unsigned package as a signed release. This deferral was explicitly accepted by the user and does not block C1.

The exact SQLCipher provider/native package selection and technical feasibility checks are implementation tasks 2.2–2.3, not unresolved stage 1 product choices. If verification exposes an incompatibility, record the evidence and revisit the affected technical decision before changing the agreed target.

## Stage 1 completion

On 2026-10-25 the user answered the remaining foundation, draft, and release questions, then explicitly approved implementation of the complete stage 1 closure plan. Tasks 1.1–1.8 and C1 are complete. Product requirements, user stories, design guidance, and tasks reflect D-15–D-20; the task list retains its original flat X.Y structure.

The earlier recommendation to discard drafts on automatic locking was rejected and replaced by D-18. Earlier password and timer recommendations were superseded by D-15–D-16. The former requirement to resolve publisher identity in stage 1 was replaced by the explicit stage 11 deadline in D-20.

Verification: documentation-only review in the cloud workspace, covering local links and anchors, English documentation, unique task IDs and unchanged ordering, consistent requirements, and absence of active obsolete timer/draft rules. Application code and runtime behavior have not been implemented or tested. Stage 2 was not started; code lint/build/runtime checks do not apply to this closure.

## Maintaining this log

Record new decisions with their rationale and implications. Preserve superseded decisions with a replacement note and a reference to the new decision. Update affected requirements and scenarios together; do not mark a proposal confirmed without explicit evidence.
