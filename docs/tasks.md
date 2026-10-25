# MVP Implementation Sequence

Status: implementation planning is in progress; application code has not been created. Task 1.1 is complete: the user explicitly invoked `next-task` and approved recording that authorization. Other completion states are shown below.

Sources: [product.md](product.md), [US-01–US-10](user-stories.md), [decision statuses D/P](decisions.md), and [design.md](design.md). Including baseline parameters or the stack in this checklist does not confirm them.

## Maintaining this plan

- Invoke `next-task` in chat to execute one numbered item using the [command workflow](next-task.md). Completion marks, commit, and push are proposed after execution and verification; creating the workflow does not start development.

- Follow the sequence: decisions → foundation → protected storage → catalog → browser and search → backups → release. Verify UI and protection as features become available.
- Mark `[x]` only after completion and verification. Close checkpoints based on the stated outcome, not merely the existence of files.
- Record the check, date, and environment beside completed checkpoints; record the reason and next step for blockers. Leave unverified scenarios open.
- Resolve questions before implementing dependent behavior; independent work may continue. Update the decision log and related requirements when decisions change.
- This plan covers the MVP. Servers, synchronization, autofill, browser extensions, mobile clients, and shared access are excluded.
- Application-owned UI and installer text must be English (D-12); preserve user-entered content without translation.

## 1. Clarify the foundation and unresolved behavior

Dependency: none. Source: P-01–P-08 and open questions in the decision log.

- [x] 1.1. Obtain a separate explicit instruction to implement before starting stage 2.

  Evidence (2026-10-25): the user invoked `next-task` and approved recording completion. Authorization follows the selected-task scope in D-13; this does not confirm P-01–P-08 or authorize publication. Documentation links and task IDs were checked in the cloud workspace; no code checks apply.
- [ ] 1.2. Confirm P-01: Windows 11 x64, minimum OS version, C# / .NET 10 LTS, WPF, and SQLite with SQLCipher.
- [ ] 1.3. Confirm P-02–P-03: three panes, flat folders, sorting, and one credential pair per entry.
- [ ] 1.4. Before stages 3–5, define master password rules, reveal duration, locking/clipboard intervals, and unsaved input handling on lock (P-04).
- [ ] 1.5. Before stages 4, 6–7, define repeated launches, duplicate names, empty queries, no results, and partial website launch failures; confirm P-05's threshold.
- [ ] 1.6. Before stage 8, clarify replacement during restore, backup version compatibility, and failure recovery during password changes (P-06).
- [ ] 1.7. Before stages 10–11, clarify search measurement (P-08), installer format and signing, manual and interrupted updates (P-07); select the application and publisher names.
- [ ] 1.8. Record answers in `decisions.md` and align `product.md`, `user-stories.md`, and this plan.

- [ ] **Checkpoint C1:** implementation is authorized and the technical foundation is defined; remaining questions have explicit deadlines before dependent stages.

## 2. Prepare the technical foundation

Dependency: C1. The stack below applies after P-01 is confirmed.

- [ ] 2.1. Create the solution and separate the WPF UI, catalog operations, protected storage, and Windows integration.
- [ ] 2.2. Select a compatible .NET provider and native SQLCipher x64 build; review licenses and redistribution rights.
- [ ] 2.3. Use a small technical prototype to verify actual database encryption, incorrect-key rejection, backup operations, and rekeying support.
- [ ] 2.4. Pin dependency versions and document build/test commands on Windows; arrange automated execution of these checks.
- [ ] 2.5. Define interfaces for URL launching, clipboard access, time/inactivity, and file operations so they can be tested independently of the UI.
- [ ] 2.6. Define the Windows user data directory, technical log location, and error handling without entry contents or secrets. Do not add telemetry.

- [ ] **Checkpoint C2:** a minimal application builds and runs on Windows; the chosen SQLCipher integration works, and dependencies and layers are defined.

## 3. Implement protected storage

Dependency: C2 and master password rules. Coverage: US-01 and part of US-10.

- [ ] 3.1. Define folders and entries with stable IDs, folder relationships, all product fields, and a schema version.
- [ ] 3.2. Implement one local vault per Windows user and master password creation/confirmation.
- [ ] 3.3. Explain before creation that a forgotten master password cannot be recovered.
- [ ] 3.4. Implement unlocking with an established cryptographic library; never store the master password or implement custom cryptographic algorithms.
- [ ] 3.5. Verify encryption of every field and the absence of plaintext in database auxiliary files, temporary files, and logs.
- [ ] 3.6. Apply changes transactionally; preserve the previous committed state when writing fails.
- [ ] 3.7. Prepare versioning and migrations with an encrypted backup before schema changes; do not modify unsupported formats.
- [ ] 3.8. Test creation, reopening, mismatched confirmation, incorrect passwords, damaged files, denied write access, insufficient disk space, and interrupted saves.

- [ ] **Checkpoint C3:** US-01 is verified; saved state survives restarts and tested write failures, and no plaintext user data has been found on disk.

## 4. Build the UI foundation

Dependency: C3 and decisions about layout and repeated launches. Rules: `design.md`.

- [ ] 4.1. Create shared WPF resources for the palette, typography, dimensions, spacing, and control styles in the concise design system.
- [ ] 4.2. Build vault creation/unlock screens with local loading indicators and clear errors, using English text.
- [ ] 4.3. Build the main window: folders, website list, and details, with space for search and catalog commands.
- [ ] 4.4. Prepare shared inputs, buttons, lists, confirmations, and hover/focus/disabled/loading/empty/error states.
- [ ] 4.5. Implement agreed repeated-launch behavior without allowing inconsistent writes to the same vault.
- [ ] 4.6. Verify keyboard navigation, focus order, long labels, and resizing. Show an honest empty state before entries exist.

- [ ] **Checkpoint C4:** users can create/unlock the vault and enter the catalog shell; core components and states are consistent.

## 5. Implement locking and secret lifecycles

Dependency: C4 and P-04. Coverage: US-07 and the foundation of US-06.

- [ ] 5.1. Implement manual locking and in-application inactivity tracking; the baseline interval is 5 minutes.
- [ ] 5.2. Handle Windows locking and sleep; require the master password on return.
- [ ] 5.3. On lock, close vault access, remove data from the UI, and release accessible resources containing secrets without promising guaranteed erasure of all managed memory.
- [ ] 5.4. Apply the agreed unsaved-edit policy; locking must not silently save or discard input contrary to that policy.
- [ ] 5.5. Test unlocking again, incorrect passwords, sleep/resume, timer resets through in-app actions, and preservation of previously saved data.

- [ ] **Checkpoint C5:** every lock trigger is verified on Windows; protected data is unavailable until successful unlocking.

## 6. Implement folders, entries, and credentials

Dependency: C5, duplicate-name rules, and password reveal duration. Coverage: US-02, US-03, US-06.

- [ ] 6.1. Add flat-folder creation, renaming, deletion, selection, and sorting by name.
- [ ] 6.2. Show the number of affected websites before deleting a nonempty folder; canceling changes nothing, and folder/entry deletion is consistent.
- [ ] 6.3. Implement adding, editing, deleting, and moving entries between folders.
- [ ] 6.4. Validate required names and primary URLs; allow only `http://` and `https://`, including for a supplied additional URL.
- [ ] 6.5. Support optional username, password, additional URL, and 2FA note; represent multiple accounts as separate entries.
- [ ] 6.6. Show selected website details with a masked password; end temporary reveal according to the agreed rule and on lock.
- [ ] 6.7. Add separate username/password copy actions, brief confirmation without exposing secrets, and handling for a busy/unavailable clipboard.
- [ ] 6.8. Clear copied passwords after the baseline 30 seconds only if clipboard contents have not been replaced; test repeated copying and timer interaction with locking.
- [ ] 6.9. Explain Windows clipboard history and third-party application limitations. A 2FA note must not imply email retrieval or code generation.
- [ ] 6.10. Test the full operation cycle across restarts, canceled edits/deletions, validation and save errors, empty optional fields, and clipboard replacement by another application.

- [ ] **Checkpoint C6:** US-02, US-03, and US-06 are verified; catalog data persists, secrets are not inadvertently exposed, and stale timers do not delete new clipboard content.

## 7. Add search and website launching

Dependency: C6, empty-search decisions, and P-05. Coverage: US-04, US-05.

- [ ] 7.1. Search names, primary URLs, and usernames across all folders; exclude passwords.
- [ ] 7.2. Sort websites by name and allow navigation from results to details; apply agreed empty-query and no-results behavior.
- [ ] 7.3. Open primary URLs through buttons and context menus in the Windows default browser; provide a separate button for additional URLs.
- [ ] 7.4. Revalidate URL schemes before launching; do not manage browser profiles, accounts, or sign-in forms.
- [ ] 7.5. Implement “Open all” for primary URLs in the selected folder; the baseline requires confirmation above 10 websites.
- [ ] 7.6. Handle browser errors and partial launches according to the agreed policy; do not report complete success when some launches fail.
- [ ] 7.7. Test searches across folders, password exclusion, permitted/forbidden schemes, additional URLs, 0/1/10/11 websites, and canceled bulk launches.

- [ ] **Checkpoint C7:** functional criteria for US-04 and US-05 are verified on Windows; stage 10 measures search performance.

## 8. Implement backups and master password changes

Dependency: C7, P-06, and compatibility rules. Coverage: US-08, US-09.

- [ ] 8.1. Create consistent encrypted exports; test recently written data and destination-file errors.
- [ ] 8.2. Validate the password, integrity, and format version before restoration without changing the current catalog.
- [ ] 8.3. After validation, request replacement confirmation; save an encrypted copy of the current vault first. If that backup fails, do not replace the vault.
- [ ] 8.4. Implement replacement without merging, preserving a recoverable state if interrupted.
- [ ] 8.5. Test transfer to another computer, incorrect passwords, damaged/unsupported backups, cancellation, insufficient space, and interrupted restoration.
- [ ] 8.6. Add master password changes with current-password verification and new-password confirmation; preserve data on failure or interruption.
- [ ] 8.7. Explain and verify that the current catalog accepts only the new password after a change, while older exports retain their original password.

- [ ] **Checkpoint C8:** US-08 and US-09 are verified, including cross-computer transfer and failures; restore and password changes preserve the last saved state.

## 9. Review data integrity and protection

Dependency: C8. Coverage: cross-cutting requirements of US-01 and US-06–US-10.

- [ ] 9.1. Repeat write-failure checks for catalog operations, migrations, password changes, and restoration: insufficient space, access loss, and abnormal termination.
- [ ] 9.2. Verify supported format compatibility and refusal to modify unsupported formats; test recovery from an encrypted backup after a failed migration.
- [ ] 9.3. Verify locking during editing, password reveal/copy, and file operations according to the agreed rules.
- [ ] 9.4. Inspect generated files, database auxiliary files, logs, and error messages for plaintext test data; verify that telemetry is absent.
- [ ] 9.5. Conduct a separate pre-release review of data protection: library usage, key management, files, clipboard, locking, and restoration. Record findings and fix release-blocking issues.

- [ ] **Checkpoint C9:** protection and failure-recovery results are recorded; no unresolved issues permit data loss or exposure in tested scenarios. This is not a guarantee of absolute security.

## 10. Verify UI, performance, and offline operation

Dependency: C9 and the P-08 measurement method. Coverage: US-04, US-10, and `design.md`.

- [ ] 10.1. Check every screen on Windows at 1280×800, 1440×900, and 1920×1080, with display scaling and long names/URLs. Verify English UI text and unchanged multilingual user content.
- [ ] 10.2. Complete core workflows using only the keyboard; check dialog focus, contrast, states, and the system animation-disable setting.
- [ ] 10.3. Measure search against the 300 ms target with 10,000 synthetic entries on a machine with 4 CPU cores, 8 GB RAM, and an SSD. Record the build, machine specifications, queries, and results using the agreed method.
- [ ] 10.4. Verify responsiveness of lists, entry selection, saving, and lengthy file operations; resolve UI blocking if needed and repeat affected measurements.
- [ ] 10.5. Without internet access, test creation/unlocking, catalog operations, search, copying, export, and restoration; assess website connectivity separately.
- [ ] 10.6. Map results to US-01–US-10; fix defects and repeat checks for changed scenarios.

- [ ] **Checkpoint C10:** functional scenarios, visual checks, offline operation, and target performance are supported by recorded Windows results.

## 11. Prepare installation and updates

Dependency: C10, P-07, application/publisher names, and interrupted-update rules. Coverage: distribution requirements in US-10.

- [ ] 11.1. Prepare an English-language installer for the agreed Windows x64 target, bundling the .NET runtime and native SQLCipher dependencies; verify operation without a separately installed SDK/runtime.
- [ ] 11.2. Configure release artifact signing with the publisher certificate and signature verification; record a missing certificate as a signed-release blocker.
- [ ] 11.3. Implement manual updates through a new installer, preserving the catalog and using verified migrations; uninstalling retains the vault by default.
- [ ] 11.4. On clean Windows 11, test installation, launch, updates with existing data, interrupted updates, uninstalling, and reinstalling.
- [ ] 11.5. Document a reproducible build process, dependency versions, release artifacts, and redistributed component licenses.
- [ ] 11.6. Prepare concise English installation, backup, restoration, and update instructions, including master password and clipboard limitations.

- [ ] **Checkpoint C11:** the signed installer is verified on clean Windows; updates and uninstalling meet requirements, and the release can be rebuilt.

## 12. Accept the MVP and prepare release

Dependency: C11.

- [ ] 12.1. Collect C1–C11 and US-01–US-10 results, environment versions, and known limitations for the final build. Check every completion criterion in `product.md`.
- [ ] 12.2. Resolve release-blocking defects and repeat affected checks. Explicitly leave unverified items open.
- [ ] 12.3. Update documentation so implemented behavior, decisions, instructions, and this checklist agree.
- [ ] 12.4. Present the ready build, verification results, and limitations to the user for acceptance.
- [ ] 12.5. Publish/release only when instructed; otherwise record artifact readiness without claiming publication.

- [ ] **Checkpoint C12:** the MVP is accepted based on a verified build; release preparation and actual publication are reported separately.
