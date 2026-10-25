# MVP User Stories

Status: approved acceptance scenarios after stage 1. These are acceptance criteria for future implementation, not a record of completed tests. The primary requirements are in [product.md](product.md). Decision statuses and open questions are in [decisions.md](decisions.md).

All application-owned interface text is English. User-entered names, notes, and other content retain their original language.

## US-01. Create and unlock a vault

As a user, I want to protect my catalog with one master password so that my data is not stored on disk in plaintext.

Acceptance criteria:

- First launch offers master password creation and confirmation. Accept 4–128 Unicode code points without mandatory character classes; preserve spaces and the exact entered value.
- Test the length boundaries: 3 and 129 characters are rejected; 4 and 128 are accepted, including Unicode input. Apply the same rule when changing the master password.
- Before creation, a warning explains that a forgotten password cannot be recovered.
- Mismatched confirmation does not create a vault.
- Creation opens an empty catalog; subsequent launches require the master password.
- The correct password unlocks the catalog; an incorrect password neither unlocks nor modifies it.
- The master password is not stored; vault contents are encrypted.

## US-02. Organize topics

As a user, I want folders for interests such as VPS hosting or electronics.

Acceptance criteria:

- Folders can be created, renamed, and deleted.
- The application uses a single folder level and sorting by name.
- Folder names that match after trimming surrounding whitespace and ignoring case are rejected. Duplicate website names and URLs remain allowed.
- Selecting a folder displays its websites.
- Deleting a nonempty folder requires confirmation showing how many websites will be deleted.
- Canceling preserves the folder and its websites.
- Changes persist across restarts.

## US-03. Save a website and sign-in details

As a user, I want URLs and credentials stored together so that I do not have to remember them.

Acceptance criteria:

- Entries can be added, edited, deleted, and moved between folders.
- Name and primary URL are required; description, username, password, additional URL, and a 2FA note are optional.
- Every entry provides a multiline Description field, separate from the 2FA note. It can be created, edited, viewed, and cleared, with Unicode content preserved.
- Descriptions persist across restarts, moves, and backup/restore and are encrypted with the other entry data.
- The primary URL and any supplied additional URL must use `http://` or `https://`.
- Invalid required fields prevent saving and produce an explanatory message.
- The application allows one username/password pair per entry; multiple accounts use separate entries.
- Selecting an entry shows its details with the password initially masked.
- Saved changes persist across restarts.

## US-04. Find a website

As a user, I want to search the catalog regardless of folder location.

Acceptance criteria:

- Quick search covers names, primary URLs, additional URLs, descriptions, and usernames across all folders.
- Results update as the user types without requiring a Search button or Enter. Ctrl+F focuses search; matching is case-insensitive and supports substrings.
- Verify matches that occur only in a description or additional URL, empty descriptions, Unicode content, and updated results after editing, moving, or restoring entries.
- Password contents are excluded.
- Results allow selecting an entry and viewing its details.
- Websites are sorted by name. An empty search returns to the selected folder; no matches provides a clear empty state and Clear search action.
- The target is p95 ≤ 300 ms for 10,000 entries with representative descriptions, measured using the configuration and end-to-end method in `product.md`. Record median, maximum, first-search, and unlock timings separately as specified.

## US-05. Open one website or an entire topic

As a user, I want to open a website or every website in a folder to start working quickly.

Acceptance criteria:

- A button or context menu opens the primary URL in the Windows default browser.
- “Open all” launches primary URLs from the selected folder.
- The application requires confirmation for more than 10 websites; canceling launches none.
- A separate button opens the additional URL.
- Unsupported URL schemes are not launched.
- Open all is disabled for an empty folder. Failed launches do not prevent remaining attempts; report handoff successes and failures separately and retry only failures. A browser handoff does not imply a successful page load or sign-in.
- The application does not select browser profiles, switch accounts, or fill sign-in forms.

## US-06. Use credentials

As a user, I want to copy my username and password and view a 2FA note to sign in manually.

Acceptance criteria:

- Separate buttons copy the username and password.
- A password can be revealed temporarily and is masked again afterward. The agreed reveal duration is 30 seconds, ending earlier on selecting another website or locking. Ordinary application focus loss alone does not end the reveal.
- A copied password is cleared unless the clipboard contents have been replaced. The agreed clipboard expiry is 60 seconds.
- Replacement clipboard text is not deleted. Repeated copying replaces the prior expiry timer. On locking or closing, clear the application-owned copied password only if it remains unchanged.
- The interface explains that Windows clipboard history and third-party applications may retain a copy.
- A note may identify a 2FA application; the additional URL may point to webmail. The application does not retrieve email or generate codes.

## US-07. Lock the catalog

As a user, I want to close access to my data when stepping away.

Acceptance criteria:

- A manual lock command is available.
- Locking occurs on in-app inactivity, Windows lock, and sleep. The agreed inactivity interval is 15 minutes.
- Locked entries and secrets are inaccessible; continuing requires the master password.
- Saved data remains available after unlocking.
- Before automatic locking, preserve unsaved entry input as an encrypted draft separate from the saved entry. Offer to resume it only after successful unlocking; the saved entry must not change automatically.
- Saving the entry or explicitly discarding the draft removes it. Drafts remain encrypted and inaccessible while locked.
- A failed draft write does not delay locking; report the failure after unlocking. Verify both successful recovery and the failed-write path.
- Manual locking with edits offers Save and lock / Discard and lock / Cancel. A failed save leaves the editor open with an error unless an automatic lock takes priority.

## US-08. Back up and restore

As a user, I want an encrypted backup that can transfer my catalog to another computer.

Acceptance criteria:

- Manual export creates a consistent encrypted file.
- A copy opens on another computer with the master password in effect at export time.
- Password, integrity, and format version are checked before restoration.
- An incorrect password, damaged file, or unsupported version leaves the current catalog unchanged.
- After successful validation, the user confirms replacement; canceling preserves the current catalog.
- An encrypted copy of the current vault is saved before replacement. Failure to create it prevents replacement.
- Supported older formats use tested migrations; newer or unsupported formats do not modify the active vault. Interrupted restore/migration retains a valid recoverable old or new state.
- Restoration replaces the catalog rather than merging entries.

## US-09. Change the master password

As a user, I want to change my master password while retaining my catalog.

Acceptance criteria:

- Changing the password requires the current master password.
- An incorrect current password leaves the vault unchanged.
- After a successful change, the current catalog opens with the new password and rejects the old one.
- Previously exported copies continue to use the password in effect when they were created.
- A save failure or interruption must not destroy the last saved state. Temporary encrypted recovery copies are removed after a successful password change.

## US-10. Work without the cloud

As a user, I want local operation and updates that preserve my catalog.

Acceptance criteria:

- The catalog requires no internet, registration, or server. Website availability depends on connectivity.
- The MVP has no telemetry; logs contain no entries, passwords, or other secrets.
- Distribution uses a signed per-user x64 MSI containing the self-contained application, .NET runtime, and native dependencies.
- Manual updates preserve data; uninstalling retains the vault by default.
- Interrupted installer updates and first-launch migrations are tested separately; recovery retains a usable application and a valid vault without silently downgrading its format.
- Repeated launches activate the existing per-user application instance without unlocking it.
- Use Config is the working name. Final application name, publisher identity, and signing certificate are release prerequisites to resolve before stage 11, not open stage 1 decisions.
- Installation, updates, and core workflows are tested on clean systems running the oldest agreed Windows versions and a current Windows release. The agreed minimums are Windows 10 22H2 and Windows 11 21H2; compatibility must be verified rather than inferred from the target.
- Interrupted saves and insufficient disk space are tested; the last saved state is not corrupted.
- Application-owned UI and installer text is English, including validation, errors, and confirmations; user-entered content is not translated.

## Completion boundary

Each scenario requires recorded verification results during implementation. These criteria alone do not establish security or readiness for public release. Resolve relevant uncertainties in the decision log before implementing the affected behavior.
