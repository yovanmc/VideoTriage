# Release Checklist

This checklist prepares a release; it does not authorize tagging, pushing, production signing,
GitHub release creation, or Store submission.

## 1. Repository State

- [ ] Work from updated `main` after packaging is integrated.
- [ ] `git status --short` is empty before release verification.
- [ ] `git diff --check` prints no output.
- [ ] README, installation, architecture, safety, screenshots, and prototype provenance describe the
      integrated behavior.

## 2. Restore, Build, And Test

Run:

```powershell
dotnet restore src/VideoTriage.App/VideoTriage.App.csproj
dotnet restore src/VideoTriage.Cli/VideoTriage.Cli.csproj
dotnet restore tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
dotnet restore tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj

dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Release --no-restore
dotnet build src/VideoTriage.Cli/VideoTriage.Cli.csproj -c Release --no-restore
dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj -c Release
dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj -c Release
```

- [ ] All commands exit `0`.
- [ ] Both test projects report `Failed: 0`.

## 3. Package Artifact

- [ ] The latest `main` CI run is green.
- [ ] Download `VideoTriage-win-x64-msix`.
- [ ] The artifact contains exactly one `.msix`, `VideoTriage.Development.cer`, and
      `installation.md`.
- [ ] The artifact contains no `.pfx`.
- [ ] The package identity is `YovanMc.VideoTriage`, version `1.0.0.0`, architecture `x64`.
- [ ] The package contains `VideoTriage.App.exe` and `coreclr.dll`.
- [ ] The package contains no `ffmpeg.exe`, `ffprobe.exe`, or `HandBrakeCLI.exe`.

## 4. Disposable Installation Check

Run on a Windows x64 test account:

```powershell
Import-Certificate `
  -FilePath .\VideoTriage.Development.cer `
  -CertStoreLocation Cert:\CurrentUser\TrustedPeople
Add-AppxPackage .\VideoTriage.Package_*.msix
Get-AppxPackage YovanMc.VideoTriage |
  Select-Object Name, Version, Architecture, Status
```

- [ ] Package status is `Ok`.
- [ ] VideoTriage launches from the Start menu.
- [ ] Missing prerequisites disable Start and show install guidance.
- [ ] Uninstall succeeds through Settings or `Remove-AppxPackage`.

## 5. Manual Safety Check On Disposable Media

- [ ] Install ffmpeg, ffprobe, and HandBrakeCLI using `docs/installation.md`.
- [ ] Generate a synthetic video under `artifacts/release-smoke`; do not use personal media.
- [ ] Run dry-run and confirm no encode, replacement, deletion, or completed-state write occurs.
- [ ] Run one Recycle Bin replacement and confirm the replacement is smaller and playable.
- [ ] Confirm the original appears in the Recycle Bin.
- [ ] Cancel one active encode and confirm the original remains untouched.
- [ ] Confirm diagnostics are written under `%LocalAppData%\VideoTriage\Logs`.
- [ ] Confirm state records are written under `%LocalAppData%\VideoTriage\Data`.
- [ ] Confirm `.videotriage.partial.*` recovery instructions match the current implementation.

## 6. Documentation And Visual Review

- [ ] `docs/assets/main-window.png` shows the current Release app and dry-run queue.
- [ ] `docs/assets/summary.png` shows the current post-run summary.
- [ ] Screenshots contain no personal paths, unrelated windows, or transient UI.
- [ ] Mermaid diagrams render on GitHub.
- [ ] Every README local link resolves.
- [ ] README does not claim `40 GB`, `33%`, invented benchmark results, or unavailable prototype
      source.

## 7. Approval Gate

Stop and obtain explicit maintainer approval before any of these commands or actions:

```text
git tag v0.1.0
git push origin main
git push origin v0.1.0
gh release create ...
production certificate signing
Microsoft Store submission
```

Record the approved version, commit SHA, signer identity, artifact hash, and publication destination
in the release notes before publishing.
