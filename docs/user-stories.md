# MVP User Stories

Status: draft scenarios for discussion. These are acceptance criteria for future implementation, not a record of completed tests. The primary requirements are in [product.md](product.md). Decision statuses and open questions are in [decisions.md](decisions.md).

All application-owned interface text is English. User-entered names, notes, and other content retain their original language.

## US-01. Create and unlock a vault

As a user, I want to protect my catalog with one master password so that my data is not stored on disk in plaintext.

Acceptance criteria:

- First launch offers master password creation and confirmation.
- Before creation, a warning explains that a forgotten password cannot be recovered.
- Mismatched confirmation does not create a vault.
- Creation opens an empty catalog; subsequent launches require the master password.
- The correct password unlocks the catalog; an incorrect password neither unlocks nor modifies it.
- The master password is not stored; vault contents are encrypted.

## US-02. Organize topics

As a user, I want folders for interests such as VPS hosting or electronics.

Acceptance criteria:

- Folders can be created, renamed, and deleted.
- The baseline plan uses a single folder level and sorting by name.
- Selecting a folder displays its websites.
- Deleting a nonempty folder requires confirmation showing how many websites will be deleted.
- Canceling preserves the folder and its websites.
- Changes persist across restarts.

## US-03. Save a website and sign-in details

As a user, I want URLs and credentials stored together so that I do not have to remember them.

Acceptance criteria:

- Entries can be added, edited, deleted, and moved between folders.
- Name and primary URL are required; username, password, additional URL, and a 2FA note are optional.
- The primary URL and any supplied additional URL must use `http://` or `https://`.
- Invalid required fields prevent saving and produce an explanatory message.
- The baseline plan allows one username/password pair per entry; multiple accounts use separate entries.
- Selecting an entry shows its details with the password initially masked.
- Saved changes persist across restarts.

## US-04. Find a website

As a user, I want to search the catalog regardless of folder location.

Acceptance criteria:

- Search covers names, primary URLs, and usernames across all folders.
- Password contents are excluded.
- Results allow selecting an entry and viewing its details.
- Websites are sorted by name.
- The target is 300 ms or less for 10,000 entries on the configuration in `product.md`; this must be measured.

## US-05. Open one website or an entire topic

As a user, I want to open a website or every website in a folder to start working quickly.

Acceptance criteria:

- A button or context menu opens the primary URL in the Windows default browser.
- “Open all” launches primary URLs from the selected folder.
- The baseline plan requires confirmation for more than 10 websites; canceling launches none.
- A separate button opens the additional URL.
- Unsupported URL schemes are not launched.
- The application does not select browser profiles, switch accounts, or fill sign-in forms.

## US-06. Use credentials

As a user, I want to copy my username and password and view a 2FA note to sign in manually.

Acceptance criteria:

- Separate buttons copy the username and password.
- A password can be revealed temporarily and is masked again afterward. The exact duration is undecided.
- Under the baseline plan, a copied password is cleared after 30 seconds unless the clipboard contents have been replaced.
- Replacement clipboard text is not deleted.
- The interface explains that Windows clipboard history and third-party applications may retain a copy.
- A note may identify a 2FA application; the additional URL may point to webmail. The application does not retrieve email or generate codes.

## US-07. Lock the catalog

As a user, I want to close access to my data when stepping away.

Acceptance criteria:

- A manual lock command is available.
- Under the baseline plan, locking occurs after 5 minutes of inactivity within the application, when Windows locks, and when the computer sleeps.
- Locked entries and secrets are inaccessible; continuing requires the master password.
- Saved data remains available after unlocking.
- Handling unsaved edits requires a separate decision before implementing this scenario.

## US-08. Back up and restore

As a user, I want an encrypted backup that can transfer my catalog to another computer.

Acceptance criteria:

- Manual export creates a consistent encrypted file.
- A copy opens on another computer with the master password in effect at export time.
- Password, integrity, and format version are checked before restoration.
- An incorrect password, damaged file, or unsupported version leaves the current catalog unchanged.
- After successful validation, the user confirms replacement; canceling preserves the current catalog.
- An encrypted copy of the current vault is saved before replacement.
- The baseline plan replaces the catalog rather than merging entries.

## US-09. Change the master password

As a user, I want to change my master password while retaining my catalog.

Acceptance criteria:

- Changing the password requires the current master password.
- An incorrect current password leaves the vault unchanged.
- After a successful change, the current catalog opens with the new password and rejects the old one.
- Previously exported copies continue to use the password in effect when they were created.
- A save failure must not destroy the last saved state.

## US-10. Work without the cloud

As a user, I want local operation and updates that preserve my catalog.

Acceptance criteria:

- The catalog requires no internet, registration, or server. Website availability depends on connectivity.
- The MVP has no telemetry; logs contain no entries, passwords, or other secrets.
- The baseline plan includes a signed installer with the .NET runtime.
- Updates preserve data; uninstalling retains the vault by default.
- Installation and updates are tested on a clean Windows 11 system.
- Interrupted saves and insufficient disk space are tested; the last saved state is not corrupted.
- Application-owned UI and installer text is English, including validation, errors, and confirmations; user-entered content is not translated.

## Completion boundary

Each scenario requires recorded verification results during implementation. These criteria alone do not establish security or readiness for public release. Resolve relevant uncertainties in the decision log before implementing the affected behavior.
