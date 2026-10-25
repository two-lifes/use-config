# MVP User Stories

Status: approved acceptance scenarios revised for plaintext Markdown storage (D-21). These are acceptance criteria for future implementation, not a record of completed tests. The primary requirements are in [product.md](product.md). Decision statuses and open questions are in [decisions.md](decisions.md).

All application-owned interface text is English. User-entered names, notes, and other content retain their original language.

## US-01. Choose and open a catalog

As a user, I want to select a local directory and access my catalog without a master password.

Acceptance criteria:

- First launch lets the user select an existing catalog directory or create an empty one.
- The application remembers the selected path and reopens it on later launches without authentication.
- Canceling directory selection does not create or modify a catalog.
- Missing or inaccessible directories produce a recoverable error without replacing existing data.
- Topics are directories at one level; each website is a separate `.md` file.
- The interface explains that files and ZIP backups contain plaintext credentials, even when passwords are masked in the UI.
- Valid catalogs load according to the documented format; malformed or unsupported files are reported without changing their source contents.

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
- Descriptions persist across restarts, moves, and backup/restore as plaintext Markdown with the other entry data.
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
- The target is p95 ≤ 300 ms for 10,000 entries with representative descriptions, measured using the configuration and end-to-end method in `product.md`. Record median, maximum, first-search, and catalog-loading timings separately as specified.

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
- A password can be revealed temporarily and is masked again afterward. The agreed reveal duration is 30 seconds, ending earlier on selecting another website. Ordinary application focus loss alone does not end the reveal.
- A copied password is cleared unless the clipboard contents have been replaced. The agreed clipboard expiry is 60 seconds.
- Replacement clipboard text is not deleted. Repeated copying replaces the prior expiry timer. On closing, clear the application-owned copied password only if it remains unchanged.
- The interface explains that Windows clipboard history and third-party applications may retain a copy.
- A note may identify a 2FA application; the additional URL may point to webmail. The application does not retrieve email or generate codes.

## US-07. Handle unsaved edits

As a user, I want to decide what happens to unfinished changes before leaving the editor.

Acceptance criteria:

- Leaving an edited entry, closing the application, or replacing the catalog offers Save / Discard / Cancel.
- Save completes the pending action only after a successful write.
- A failed save preserves editor input and previously saved data and explains the error.
- Discard leaves the saved entry unchanged; Cancel keeps the editor and aborts the pending action.
- External changes are not silently overwritten when saving; the user is informed of the conflict.
- Persistent draft recovery is outside the MVP.

## US-08. Back up and restore

As a user, I want a ZIP backup that can transfer my catalog to another computer.

Acceptance criteria:

- Manual export creates a consistent unencrypted ZIP archive.
- A copy restores on another computer without a password.
- Archive integrity, catalog structure, entry validity, and format version are checked before restoration; paths escaping the restoration directory are rejected.
- A damaged archive, invalid catalog, unsafe archive path, or unsupported version leaves the current catalog unchanged.
- After successful validation, the user confirms replacement; canceling preserves the current catalog.
- A backup of the current catalog is saved before replacement. Failure to create it prevents replacement.
- Supported older formats use tested migrations; newer or unsupported formats do not modify the active catalog. Interrupted restore/migration retains a valid recoverable old or new state.
- Restoration replaces the catalog rather than merging entries.

## US-09. Edit Markdown files externally

As a user, I want to edit my catalog with a text editor using the documented Markdown format.

Acceptance criteria:

- The format documents fields, stable identifiers, filenames, versioning, and the single-level topic hierarchy.
- Valid manual edits are reflected in entry details and search after the catalog is reloaded.
- Unicode and multiline descriptions are preserved when reading and saving supported files.
- Malformed files and unsupported versions produce actionable errors without rewriting the originals.
- If a file changes externally while it is being edited in the application, saving must not silently overwrite that change.
- Deletion, renaming, and movement outside the application are handled without silently recreating stale data.
- The application does not promise preservation of arbitrary Markdown constructs outside the documented format.

## US-10. Work without the cloud

As a user, I want local operation and updates that preserve my catalog.

Acceptance criteria:

- The catalog requires no internet, registration, or server. Website availability depends on connectivity.
- The MVP has no telemetry; logs contain no entries, passwords, or other secrets.
- Distribution uses a signed per-user x64 MSI containing the self-contained application, .NET runtime, and native dependencies.
- Manual updates preserve data; uninstalling retains the catalog by default.
- Interrupted installer updates and first-launch migrations are tested separately; recovery retains a usable application and a valid catalog without silently downgrading its format.
- Repeated launches activate the existing per-user application instance without creating another writer.
- Use Config is the working name. Final application name, publisher identity, and signing certificate are release prerequisites to resolve before stage 11, not open stage 1 decisions.
- Installation, updates, and core workflows are tested on clean systems running the oldest agreed Windows versions and a current Windows release. The agreed minimums are Windows 10 22H2 and Windows 11 21H2; compatibility must be verified rather than inferred from the target.
- Interrupted saves and insufficient disk space are tested; the last saved state is not corrupted.
- Application-owned UI and installer text is English, including validation, errors, and confirmations; user-entered content is not translated.

## Completion boundary

Each scenario requires recorded verification results during implementation. These criteria alone do not establish security or readiness for public release. Resolve relevant uncertainties in the decision log before implementing the affected behavior.
