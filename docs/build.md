# Windows build and verification

## Requirements

Use Windows x64, PowerShell, and the exact .NET SDK **10.0.402** selected by [global.json](../global.json). SDK roll-forward and prerelease selection are disabled. The script resolves `dotnet.exe` from PATH, falling back to `%ProgramFiles%/dotnet/dotnet.exe`. It fails if the required SDK is unavailable; it does not install tools or change machine configuration.

Package restoration uses the public NuGet feed at `https://api.nuget.org/v3/index.json`. Initial restoration requires network access. Run on a local NTFS volume for the Windows file-sharing and hard-link scenarios. Restricted sandboxes may block MSBuild named pipes or native file operations; report these failures and run in an authorized Windows host environment where necessary.

## Mandatory verification budget

The user prohibits prolonged verification. The entire task has a shared maximum of **5 minutes and 5 verification command invocations**, including builds, formatting, tests, smoke checks and retries. Stop at whichever limit comes first; do not reset the budget between items or evade it by splitting/wrapping commands. Use a time-bounded runner and terminate its child processes when the remaining time expires. Report omitted checks rather than claiming success. This overrides exhaustive stage-review requirements.

## One verification command

From the repository root:

```powershell
./scripts/verify.ps1
```

From another directory, invoke the script by its full path. It resolves the repository relative to itself and restores the caller's location. Use a child PowerShell process when checking its process exit code, for example `pwsh -NoProfile -File ./scripts/verify.ps1`.

The script stops at the first failed command and returns a nonzero exit code. In order, it:

1. Checks Windows and the exact SDK version.
2. Restores the application solution and the separate storage probe; the probe uses `--locked-mode`.
3. Restores the dependency-free contract checks and builds all three targets in Debug and Release with `--no-restore`.
4. Runs `dotnet format --verify-no-changes --no-restore` on all three targets without rewriting sources.
5. Runs the storage probe and contract-check executables in Debug and Release. Any nonzero exit code fails verification. Contract checks use in-memory doubles, not real adapters; see [technical-foundation.md](technical-foundation.md).

The probe remains outside [UseConfig.slnx](../UseConfig.slnx). Its executable assertions are the existing automated scenario suite; there is no separate `dotnet test` project. See [the prototype report](storage-prototype.md) for scenarios, synthetic fixtures retained under ignored `bin/`, and known limitations. Do not supply real catalog data to verification.

## Dependency updates

The application has no explicit third-party package references. The isolated probe uses **YamlDotNet 18.1.0** with the license review in [the prototype report](storage-prototype.md#dependencies-and-license-review). Its tracked `packages.lock.json` records resolved dependencies and content hashes. No production YAML dependency is selected by this setup.

To deliberately update dependencies, edit the probe's package reference, run `dotnet restore prototypes/UseConfig.StorageProbe/UseConfig.StorageProbe.csproj --source https://api.nuget.org/v3/index.json --force-evaluate`, inspect the generated lock-file diff and license implications, then run the full verification script. Ordinary verification must not refresh the lock file to hide a mismatch.

To update the SDK, install the intended version separately, change `global.json`, review any affected restore graph and lock file, update this document, and rerun verification. CI reads the same SDK pin. The pin fixes the SDK, not the host OS image or the installed runtime used by framework-dependent executables; the probe reports its actual runtime and OS.

## Continuous integration and limits

[Windows verification](../.github/workflows/verify.yml) runs the same script on GitHub's `windows-2025` runner for pushes, pull requests, and manual dispatch. It has read-only repository permissions, a 20-minute timeout, and official checkout/setup-dotnet actions pinned to verified commit SHAs. Review upstream release metadata before changing those pins.

Preparing this workflow does not publish code or demonstrate a successful remote run. Hosted Windows Server checks do not verify WPF interaction, installation, or compatibility with Windows 10 22H2 and Windows 11 21H2. Those checks remain separate project tasks. Build automation alone did not complete task 2.5 or C2; both are now completed by the separate stage 2 review and user-approved finalization.

## Local verification record

Stage 2 retry (2026-10-25): the user-authorized five-command attempt completed within five minutes. The full script passed on Windows 10.0.26200.0 x64 / NTFS, SDK 10.0.402 and runtime 10.0.13: all three targets built in Debug/Release without warnings/errors, formatting passed, and each configuration passed 96 storage-probe scenarios plus 11 contract checks. WPF startup and normal WM_CLOSE shutdown passed separately. See [the foundation verification record](technical-foundation.md#authorized-retry-results-2026-10-25) for sandbox/encoding failures and limits. No package changes were needed; remote CI and minimum Windows versions remain unverified.

On 2026-10-25, the complete script passed on Windows 10.0.26200.0 x64 / NTFS with SDK 10.0.402 and runtime 10.0.13, both from the repository root and from another working directory. Application and probe Debug/Release builds had zero warnings and errors; both formatting checks passed. The probe reported **96 passed, 0 failed** in each configuration. Locked restoration left the lock-file hash unchanged.

A child PowerShell process with an empty PATH and a nonexistent Program Files location returned exit code 1 with the expected missing-SDK diagnostic. PowerShell syntax, workflow YAML parsing and triggers, local documentation links, and diff whitespace were checked. Action SHAs were verified against official release tags. An initial sandbox restore could not fetch NuGet vulnerability data; the final host restoration completed without that warning. Remote CI and minimum supported Windows versions were not tested. The user subsequently authorized marking task 2.4 complete; task 2.5 and checkpoint C2 remained open at that time and were subsequently completed in the stage 2 review above.
