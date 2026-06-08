# Install VideoTriage

VideoTriage is distributed as a self-contained Windows x64 MSIX. The .NET desktop runtime is
included in the package. VideoTriage does not bundle ffmpeg, ffprobe, or HandBrakeCLI.

## Requirements

- Windows 10 version 1809 (build 17763) or newer
- x64 Windows
- ffmpeg and ffprobe available on `PATH`
- HandBrakeCLI available on `PATH`
- A supported NVIDIA GPU and driver for the `VideoTriage AV1` NVEnc preset

Install the external tools from a PowerShell prompt:

```powershell
winget install --exact --id Gyan.FFmpeg
winget install --exact --id HandBrake.HandBrake.CLI
```

Close and reopen PowerShell, then verify:

```powershell
ffmpeg -version
ffprobe -version
HandBrakeCLI --version
```

Each command must print version information. VideoTriage checks the same executables at startup and
keeps **Start** disabled when a prerequisite is missing.

## Install A CI Test Package

CI artifacts are signed with a disposable self-signed development certificate. The artifact
contains one `.msix` and `VideoTriage.Development.cer`; it never contains the private `.pfx`.

From PowerShell:

```powershell
Import-Certificate `
  -FilePath .\VideoTriage.Development.cer `
  -CertStoreLocation Cert:\CurrentUser\TrustedPeople

Add-AppxPackage .\VideoTriage.Package_*.msix
```

Expected: `Add-AppxPackage` returns without an error and **VideoTriage** appears in the Start menu.
Trust only a certificate obtained from the same CI artifact as the MSIX. Remove an obsolete test
certificate from `certmgr.msc` under **Trusted People > Certificates**.

## First Run

1. Open VideoTriage from the Start menu.
2. Confirm ffmpeg, ffprobe, and HandBrakeCLI show as available.
3. Select a folder containing disposable test media.
4. Enable **Dry run**.
5. Run the scan and review the candidate list before allowing encoding or replacement.

Dry-run stops after discovery, probe, and classification. It does not encode, verify, replace,
delete, or write completed-file state.

## Safety Defaults

- For each file, the original is removed only after a smaller replacement passes every enabled
  verification check and the replacement is confirmed on disk.
- Recycle Bin is the default deletion mode.
- Permanent deletion requires an explicit setting and a fresh session confirmation.
- A failure before a file reaches replacement leaves that file's original untouched.
- Cancellation or a later run failure may occur after earlier files completed replacement; review
  the queue and result log before retrying.
- ffmpeg, ffprobe, and HandBrakeCLI remain separately installed tools and are not updated by
  VideoTriage.

## State And Logs

Each non-dry run stores its completed-file state, result log, and deletion manifest under
`<selected folder>\_videotriage_data` by default. The folder name can be changed in application
options.

Application diagnostic logs are under `%LocalAppData%\VideoTriage\Logs`. Persisted application
settings are under `%LocalAppData%\VideoTriage`.

## Recover A Partial Replacement

A file named like `clip.videotriage.partial.1234.mp4` means the verified replacement was preserved
after the original had already been removed but the final rename failed.

1. Stop VideoTriage.
2. Do not delete the `.videotriage.partial.*` file.
3. Confirm the file is non-empty:

   ```powershell
   Get-Item .\clip.videotriage.partial.1234.mp4 | Select-Object FullName, Length
   ```

4. Verify it with ffprobe and a full decode:

   ```powershell
   ffprobe -v error -show_format -show_streams .\clip.videotriage.partial.1234.mp4
   ffmpeg -nostdin -v error -i .\clip.videotriage.partial.1234.mp4 -f null -
   ```

5. If ffprobe reports a video stream and ffmpeg prints no real decode errors, rename it to the
   intended final `.mp4` name:

   ```powershell
   Rename-Item `
     .\clip.videotriage.partial.1234.mp4 `
     .\clip.mp4
   ```

If the intended final path already exists, keep both files and inspect them before renaming either.
Review the selected folder's `_videotriage_data` result log and deletion manifest to understand what
completed before the partial replacement.

## Uninstall

Open **Settings > Apps > Installed apps**, select **VideoTriage**, and choose **Uninstall**.
Uninstall does not remove ffmpeg, ffprobe, HandBrakeCLI, source videos, per-folder
`_videotriage_data`, persisted settings, or diagnostic logs.
