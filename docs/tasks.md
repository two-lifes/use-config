# MVP Implementation Sequence

Status: stages 1 and 2 are complete. Stage 3 tasks 3.1, 3.2, 3.4 and 3.5 are complete: models, catalog selection/persistence, Markdown parsing/serialization and validation. The user authorized the 3.4–3.5 dependencies while completing 3.2 and requested completion marks. Task 3.3 is the next unchecked numbered item; C3 remains open. See the [implementation and verification record](technical-foundation.md#task-32-completion-and-markdown-dependencies).

Revision (2026-10-25, D-21): current requirements use plaintext Markdown and ZIP backups. Stage 1 checkmarks and its completion record are historical; superseded storage/protection choices below are not current requirements. Task 2.1 remains complete. No revised task or checkpoint is completed by this documentation update.

Sources: [product.md](product.md), [US-01–US-10](user-stories.md), [decision statuses D/P](decisions.md), and [design.md](design.md). Stage 1 choices were confirmed in D-15–D-20 and revised by D-21; checked planning items do not imply implemented features or verified Windows compatibility.

## Maintaining this plan

- User-mandated verification cap: at most **5 minutes total and 10 verification command invocations per task**, including retries, builds, formatting and smoke checks. A multi-item request shares this budget. Stop at either limit without resetting or bypassing it; report unverified work and leave unsupported checkmarks open. This takes precedence over exhaustive checkpoint reviews.

- Invoke `$next-task` (or send `next-task` as a chat command) to execute one numbered item using the [next-task skill](../.agents/skills/next-task/SKILL.md). Completion marks, commit, and push are proposed after execution and verification; creating the workflow does not start development.

- Follow the sequence: decisions → foundation → file storage → catalog → browser and search → backups → release. Verify UI and data integrity as features become available.
- Mark `[x]` only after completion and verification. Close checkpoints based on the stated outcome, not merely the existence of files.
- Record the check, date, and environment beside completed checkpoints; record the reason and next step for blockers. Leave unverified scenarios open.
- Resolve questions before implementing dependent behavior; independent work may continue. Update the decision log and related requirements when decisions change.
- This plan covers the MVP. Servers, synchronization, autofill, browser extensions, mobile clients, and shared access are excluded.
- Application-owned UI and installer text must be English (D-12); preserve user-entered content without translation.

## 1. Clarify the foundation and unresolved behavior (historical)

Dependency: none. Source: approved P-01–P-08 decisions and D-15–D-20 in the decision log.

Stage review (2026-10-25): all remaining choices were explicitly accepted by the user and reconciled across the documents. See the [completion record](decisions.md#stage-1-completion). Final release identity and signing prerequisites have an agreed deadline before stage 11.

- [x] 1.1. Obtain a separate explicit instruction to implement before starting stage 2.
- [x] 1.2. Confirm P-01: x64, C# / .NET 10 LTS, WPF, and SQLite with SQLCipher for Windows 10 22H2 and Windows 11 21H2+; verify actual compatibility in stage 2.
- [x] 1.3. Confirm P-02–P-03: three panes, flat folders, sorting, and one credential pair per entry.
- [x] 1.4. Define the 4–128-character master password range, 30-second reveal, 15-minute inactivity lock, 60-second clipboard expiry, early masking on site selection/lock, and encrypted drafts with manual-lock choices (P-04).
- [x] 1.5. Define repeated launches, duplicate names, empty queries, no results, partial launch failures, and confirmation above 10 sites (P-05).
- [x] 1.6. Define validated replacement with a prior encrypted backup, supported-format migrations, and recovery from interrupted restore/password changes (P-06).
- [x] 1.7. Agree on the search measurement method (P-08), signed per-user x64 MSI and manual update policy (P-07), Use Config as the working name, and deferral of final name/publisher/certificate until before stage 11.
- [x] 1.8. Record answers in `decisions.md` and align `product.md`, `user-stories.md`, and this plan.

- [x] **Checkpoint C1:** implementation is authorized and the technical foundation is defined; remaining release prerequisites have an explicit deadline before stage 11.

Verification (2026-10-25, cloud workspace): stage 1 decisions were checked against the user's answers and approved closure plan. Documentation links/anchors, English text, task IDs/order, and requirement consistency were reviewed. Encrypted drafts replace the rejected discard-on-lock proposal. Tasks 2–12 and their checkpoints remain open. This is documentation completion; code lint/build/runtime checks are not applicable, and Windows compatibility is not yet verified.

## 2. Prepare the technical foundation

Dependency: C1 (historical planning complete) and the approved D-21 revision. Task 2.1 was explicitly authorized and completed. Continue only with a separate instruction or `next-task` invocation.

- [x] 2.1. Create the solution and separate the WPF UI, catalog operations, protected storage, and Windows integration.
- [x] 2.2. Specify the Markdown catalog format: fields, stable identifiers, filenames, single-level topic directories, format version, manual editing rules, and malformed-file handling. Define syntax here before implementation.
- [x] 2.3. Use a small technical prototype to verify Markdown read/write round trips, Unicode and multiline fields, external-change detection, safe file replacement, and ZIP round trips on Windows; review licenses of any required dependencies.
- [x] 2.4. Pin dependency versions and document build/test commands on Windows; arrange automated execution of these checks.
- [x] 2.5. Define interfaces for URL launching, clipboard access, reveal/clipboard timing, and catalog/file operations so they can be tested independently of the UI.
- [x] 2.6. Define storage of the user-selected catalog path, technical log location, and error handling without entry contents or secrets. Do not add telemetry.

Task 2.1 verification (2026-10-25, Windows build 26200 x64, .NET SDK 10.0.402 / runtime 10.0.13): restore, Debug and Release builds (zero warnings/errors), and `dotnet format --verify-no-changes --no-restore` passed. Project references match the intended acyclic layer structure. The Release window opened with the expected English text and closed normally with exit code 0. Git excludes build artifacts. No business logic or automated test suite exists yet; the SQLCipher integration planned at that time was unverified and has since been superseded by D-21. Markdown storage and compatibility across all target Windows versions remain unverified. C2 was not complete at the time of the task 2.1 verification.

- [x] **Checkpoint C2:** a minimal application builds and runs on Windows; the Markdown format and file/ZIP prototype are verified, and dependencies and layers are defined.

Stage 2 completion (2026-10-25, user-authorized): verified on Windows 10.0.26200.0 x64 / NTFS with SDK 10.0.402 and runtime 10.0.13. Debug/Release builds and formatting passed; each configuration passed 96 storage-probe scenarios and 11 contract checks. WPF startup and normal shutdown passed; local file links and diff whitespace were checked. The retry used five command invocations within five minutes. See [technical-foundation.md](technical-foundation.md#authorized-retry-results-2026-10-25). Production adapters, minimum supported Windows versions and remote CI remain unverified; C2 does not certify later-stage behavior.

## 3. Implement Markdown storage

Dependency: C2 and the format specified in 2.2. Coverage: US-01, storage foundations of US-09, and part of US-10.

- [x] 3.1. Define folder and entry models with stable IDs, folder relationships, all product fields including a separate description, and the format version specified in 2.2.
- [x] 3.2. Implement catalog directory selection/creation and per-user path persistence; reopen the remembered location without authentication.
  - [x] Implement and verify creation/opening of empty v1 catalogs and per-user path persistence (2026-10-25).
  - [x] Verify native UI selection, cancellation preserving the active catalog, and automatic reopening after restart (2026-10-25, Windows desktop).
  - [x] Integrate full validation and opening of catalogs containing topics/entries using the explicitly authorized 3.4–3.5 dependencies (2026-10-25).
- [ ] 3.3. Explain that Markdown files and ZIP backups contain plaintext credentials; handle canceled selection and missing or inaccessible catalog directories without replacing data.
- [x] 3.4. Implement Markdown parsing and serialization according to 2.2; preserve supported Unicode and multiline field values.
- [x] 3.5. Validate catalog structure and files; report malformed or unsupported content without rewriting the original files or logging entry contents.
- [ ] 3.6. Implement safe writes and external-change conflict detection; preserve the previous saved state on failures and never silently overwrite external edits.
- [ ] 3.7. Prepare format versioning and supported migrations with a backup before changes; do not modify unsupported formats.
- [ ] 3.8. Test directory creation/reopening, canceled selection, missing paths, Unicode, multiline fields, malformed files, unsupported versions, external edits, denied access, insufficient space, and interrupted writes.

- [ ] **Checkpoint C3:** US-01 and the storage foundations of US-09 are verified; saved state survives restarts and tested write failures, and invalid files are preserved.

Task 3.1 verification (2026-10-25, Windows x64): the Debug solution build passed with zero warnings/errors, and all 20 contract checks passed, including nine model checks. Solution and contract-project formatting checks passed outside the sandbox after sandbox MSBuild pipe access failures. See the [model verification record](technical-foundation.md#task-31-models-and-verification). Production storage, tasks 3.2–3.8 and C3 remain open.

Task 3.2 and dependencies verification (2026-10-25, Windows x64): Debug build, 106 checks, solution/test formatting and UI reopening of a populated catalog passed. Canceling the picker retained the loaded records. Ten verification calls took approximately 93 seconds; initial formatting findings were corrected. Safe catalog mutations, migrations and the broader failure matrix remain 3.6–3.8. Historical evidence above describes the state at task 3.1, not current completion. C3 remains open.

## 4. Build the UI foundation

Dependency: C3 and decisions about layout and repeated launches. Rules: `design.md`.

- [ ] 4.1. Create shared WPF resources for the palette, typography, dimensions, spacing, and control styles in the concise design system.
- [ ] 4.2. Build catalog directory selection/creation and reopening flows with local loading indicators and clear errors, using English text.
- [ ] 4.3. Build the main window: folders, website list, and details, with a multiline Description field, Ctrl+F quick search, and Save / Discard / Cancel prompts for unsaved edits.
- [ ] 4.4. Prepare shared inputs, buttons, lists, confirmations, and hover/focus/disabled/loading/empty/error states.
- [ ] 4.5. Implement agreed repeated-launch behavior without allowing inconsistent writes to the same catalog.
- [ ] 4.6. Verify keyboard navigation, focus order, long labels, and resizing. Show an honest empty state before entries exist.

- [ ] **Checkpoint C4:** users can select/create a catalog directory and enter the catalog shell; core components and states are consistent.

## 5. Handle unsaved edits and external changes

Dependency: C4 and D-21. Coverage: US-07 and US-09.

- [ ] 5.1. Implement Save / Discard / Cancel handling before leaving an edited entry, closing the application, or replacing the catalog.
- [ ] 5.2. Keep editor input on failed saves; allow the pending action only after a successful save or explicit discard.
- [ ] 5.3. Implement catalog reloading for external file changes, including renamed, moved, and deleted files; do not silently recreate stale records.
- [ ] 5.4. Report malformed external files and conflicts between in-app edits and external changes; preserve original files and unsaved editor input instead of silently overwriting.
- [ ] 5.5. Test save/discard/cancel, failed writes, reloads, external edits/renames/moves/deletions, malformed files, and conflicts. Recheck these flows with actual entry operations in 6.10.

- [ ] **Checkpoint C5:** unsaved-edit choices and external-change handling are verified on Windows; failed writes and conflicts preserve existing files and editor input.

## 6. Implement folders, entries, and credentials

Dependency: C5, duplicate-name rules, and password reveal duration. Coverage: US-02, US-03, US-06.

- [ ] 6.1. Add flat-folder creation, renaming, deletion, selection, and sorting by name; reject duplicate folder names after trimming and ignoring case.
- [ ] 6.2. Show the number of affected websites before deleting a nonempty folder; canceling changes nothing, and folder/entry deletion is consistent.
- [ ] 6.3. Implement adding, editing, deleting, and moving entries between topic directories using the Markdown format; allow duplicate website names and URLs and integrate unsaved-edit and external-conflict handling.
- [ ] 6.4. Validate required names and primary URLs; allow only `http://` and `https://`, including for a supplied additional URL.
- [ ] 6.5. Support an optional multiline description, username, password, additional URL, and 2FA note; represent multiple accounts as separate entries.
- [ ] 6.6. Show selected website details with a masked password; end temporary reveal after 30 seconds or earlier on selecting another website. Ordinary focus loss alone must not end the reveal.
- [ ] 6.7. Add separate username/password copy actions, brief confirmation without exposing secrets, and handling for a busy/unavailable clipboard.
- [ ] 6.8. Clear copied passwords after 60 seconds only if clipboard contents have not been replaced; test repeated copying and conditional cleanup on application close.
- [ ] 6.9. Explain Windows clipboard history and third-party application limitations. A 2FA note must not imply email retrieval or code generation.
- [ ] 6.10. Test the full operation cycle across restarts, save/discard/cancel, external edits and conflicts, canceled deletions, validation and save errors, empty optional fields, description persistence after edits/moves/restarts/backup/restore, and clipboard replacement by another application. Repeat backup/restore cases once stage 8 is available.

- [ ] **Checkpoint C6:** US-02, US-03, and US-06 are verified; catalog data persists, password display follows masking/reveal rules, and stale timers do not delete new clipboard content.

## 7. Add search and website launching

Dependency: C6, empty-search decisions, and P-05. Coverage: US-04, US-05.

- [ ] 7.1. Implement quick search across names, primary URLs, additional URLs, descriptions, and usernames in all folders; exclude passwords. Update results while typing, support case-insensitive substring matches, and provide Ctrl+F focus without blocking input.
- [ ] 7.2. Sort websites by name and allow navigation from results to details; an empty query returns to the selected folder, and no matches provides a Clear search action.
- [ ] 7.3. Open primary URLs through buttons and context menus in the Windows default browser; provide a separate button for additional URLs.
- [ ] 7.4. Revalidate URL schemes before launching; do not manage browser profiles, accounts, or sign-in forms.
- [ ] 7.5. Implement “Open all” for primary URLs in the selected folder; require confirmation above 10 websites and disable Open all for an empty folder.
- [ ] 7.6. Continue remaining launches after individual failures; report handoff success/failure counts and retry only failures. Do not equate a browser handoff with successful page loading or sign-in.
- [ ] 7.7. Test searches across folders, description-only and additional-URL-only matches, Unicode and empty descriptions, refreshed results after edits/moves/restoration, password exclusion, permitted/forbidden schemes, additional URLs, 0/1/10/11 websites, and canceled bulk launches.

- [ ] **Checkpoint C7:** functional criteria for US-04 and US-05 are verified on Windows; stage 10 measures search performance.

## 8. Implement ZIP backup and restoration

Dependency: C7, D-21, and compatibility rules. Coverage: US-08.

- [ ] 8.1. Create consistent unencrypted ZIP exports of the Markdown catalog; test recently written data and destination-file errors.
- [ ] 8.2. Validate archive integrity, catalog structure, entries, format version, and safe extraction paths before restoration without changing the current catalog.
- [ ] 8.3. After validation, request replacement confirmation and resolve unsaved edits; save a backup of the current catalog first. If that backup fails, do not replace the catalog.
- [ ] 8.4. Implement replacement without merging, preserving a recoverable state if interrupted and detecting external changes before replacement.
- [ ] 8.5. Test transfer to another computer, damaged/unsupported archives, malformed entries, unsafe paths, cancellation, backup failure, insufficient space, and interrupted restoration.
- [ ] 8.6. Reload the restored Markdown catalog and refresh selection, details, and search without retaining stale entries.
- [ ] 8.7. Verify that exported archives contain all supported fields and format information, need no password, and preserve Unicode, descriptions, and topic structure across export/restore.

- [ ] **Checkpoint C8:** US-08 is verified, including cross-computer ZIP transfer and failures; restoration preserves a recoverable saved state and refreshes the catalog.

## 9. Review data integrity and file handling

Dependency: C8. Coverage: cross-cutting requirements of US-01 and US-06–US-10.

- [ ] 9.1. Repeat write-failure checks for catalog operations, external-edit conflicts, migrations, and restoration: insufficient space, access loss, and abnormal termination.
- [ ] 9.2. Verify supported format compatibility and refusal to modify unsupported formats; test recovery from a backup after a failed migration.
- [ ] 9.3. Verify unsaved-edit prompts and external-change handling during editing and file operations, including password reveal/copy and failed-save reporting.
- [ ] 9.4. Inspect logs and error messages for entry contents; verify that telemetry is absent. Plaintext data in catalog files and ZIP backups is expected.
- [ ] 9.5. Conduct a separate pre-release review of file handling, write integrity, external-change conflicts, clipboard behavior, ZIP extraction, and restoration. Record findings and fix release-blocking issues.

- [ ] **Checkpoint C9:** data-integrity and failure-recovery results are recorded; no unresolved issues cause data loss, silent overwrites, unsafe extraction, or entry contents in logs in tested scenarios. Catalog files and backups intentionally remain plaintext.

## 10. Verify UI, performance, and offline operation

Dependency: C9 and the P-08 measurement method. Coverage: US-04, US-10, and `design.md`.

- [ ] 10.1. Check every screen on Windows at 1280×800, 1440×900, and 1920×1080, with display scaling and long names/URLs. Verify English UI text and unchanged multilingual user content.
- [ ] 10.2. Complete core workflows using only the keyboard; check dialog focus, contrast, states, and the system animation-disable setting.
- [ ] 10.3. Measure search against the p95 ≤ 300 ms target with 10,000 synthetic entries including representative descriptions, queries matching descriptions/additional URLs, and end-to-end results updates while typing on a machine with 4 CPU cores, 8 GB RAM, and an SSD. Record the build, machine specifications, queries, and results using the agreed method.
- [ ] 10.4. Verify responsiveness of lists, entry selection, saving, and lengthy file operations; resolve UI blocking if needed and repeat affected measurements.
- [ ] 10.5. Without internet access, test catalog selection/creation/reopening, external editing/reloading, catalog operations, search, copying, ZIP export, and restoration; assess website connectivity separately.
- [ ] 10.6. Map results to US-01–US-10; fix defects and repeat checks for changed scenarios.

- [ ] **Checkpoint C10:** functional scenarios, visual checks, offline operation, and target performance are supported by recorded Windows results.

## 11. Prepare installation and updates

Dependency: C10 and confirmed P-07. Before starting stage 11, resolve the final application name, actual publisher identity, and signing certificate; Use Config remains the working name until then. Coverage: distribution requirements in US-10.

- [ ] 11.1. Prepare a signed English-language per-user x64 MSI for the agreed Windows targets, bundling the self-contained application, .NET runtime, and any required dependencies; verify operation without a separately installed SDK/runtime.
- [ ] 11.2. Configure release artifact signing with the publisher certificate and signature verification; record a missing certificate as a signed-release blocker.
- [ ] 11.3. Implement manual updates through a new installer, preserving the catalog and using verified migrations; uninstalling retains the selected catalog directory by default.
- [ ] 11.4. On clean installations of the oldest agreed Windows versions and a current release, test installation, launch, updates with existing data, interrupted updates, uninstalling, and reinstalling.
- [ ] 11.5. Document a reproducible build process, dependency versions, release artifacts, and redistributed component licenses.
- [ ] 11.6. Prepare concise English installation, Markdown editing, ZIP backup/restoration, and update instructions, including plaintext storage and clipboard limitations.

- [ ] **Checkpoint C11:** the signed installer is verified on clean Windows; updates and uninstalling meet requirements, and the release can be rebuilt.

## 12. Accept the MVP and prepare release

Dependency: C11.

- [ ] 12.1. Collect C1–C11 and US-01–US-10 results, environment versions, and known limitations for the final build. Check every completion criterion in `product.md`.
- [ ] 12.2. Resolve release-blocking defects and repeat affected checks. Explicitly leave unverified items open.
- [ ] 12.3. Update documentation so implemented behavior, decisions, instructions, and this checklist agree.
- [ ] 12.4. Present the ready build, verification results, and limitations to the user for acceptance.
- [ ] 12.5. Publish/release only when instructed; otherwise record artifact readiness without claiming publication.

- [ ] **Checkpoint C12:** the MVP is accepted based on a verified build; release preparation and actual publication are reported separately.
