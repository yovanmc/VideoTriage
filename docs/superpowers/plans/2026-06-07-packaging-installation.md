# Packaging And Installation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a self-contained `win-x64` MSIX for the VideoTriage WPF app, upload a test-installable package from CI, and document installation, prerequisites, safety defaults, and partial-replacement recovery.

**Architecture:** VideoTriage is a non-WinUI WPF desktop app, so use a separate Windows Application Packaging Project (`.wapproj`) that references `VideoTriage.App`; do not use single-project MSIX or add the Windows App SDK to the app. The app is published self-contained for `win-x64`, while `ffmpeg`, `ffprobe`, and `HandBrakeCLI` remain external prerequisites. Local and CI packages use a generated self-signed development certificate whose public `.cer` accompanies the MSIX; no private key is committed or published as a release asset.

**Tech Stack:** .NET 10, WPF, Windows Application Packaging Project, MSIX, MSBuild, PowerShell PKI cmdlets, GitHub Actions.

---

## Authoritative References

- Architecture contract: `docs/superpowers/plans/2026-06-07-architecture-contract.md`, especially sections 11, 13, and 14.
- Microsoft Learn, [Set up your desktop application for MSIX packaging in Visual Studio](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-packaging-dot-net): non-WinUI desktop apps use a Windows Application Packaging Project, reference the desktop project, and keep platform configurations aligned.
- Microsoft Learn, [Package a desktop or UWP app in Visual Studio](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps): generate and test an MSIX from the packaging project.
- Microsoft Learn, [Create a certificate for package signing](https://learn.microsoft.com/windows/msix/package/create-certificate-package-signing): the certificate subject must exactly match the manifest publisher.

## Scope Check

This plan owns the packaging project, package assets, development signing helper, installation
documentation, and CI artifact. It does not bundle media tools, create a Store submission, push a
tag, publish a GitHub release, or acquire a production code-signing certificate.

The fixed packaging decisions are:

```text
Package project: src/VideoTriage.Package/VideoTriage.Package.wapproj
Package identity: YovanMc.VideoTriage
Publisher: CN=YovanMc
Display name: VideoTriage
Package version: 1.0.0.0
Architecture: x64
Minimum Windows version: 10.0.17763.0 (Windows 10 version 1809)
Target Windows SDK: 10.0.26100.0
App deployment: self-contained win-x64
Bundle: never; one x64 .msix
CI signing: generated self-signed certificate, private PFX deleted after packaging
External tools: prerequisites only; never package ffmpeg, ffprobe, or HandBrakeCLI
```

## Execution Corrections

- Completed-file state, result logs, and deletion manifests are stored under
  `<selected folder>\_videotriage_data` by default, not under LocalAppData.
- Cancellation or a later run failure may occur after earlier files completed replacement. Document
  the per-file safety guarantee accurately: no original is removed for a file until its smaller
  replacement passes enabled verification; completed replacements may remain after cancellation.
- Local package construction requires the fixed Windows SDK `10.0.26100.0`. If it is unavailable,
  complete and verify all repository artifacts and CI policy without changing the target SDK, then
  report local MSIX build/install verification as blocked by that machine prerequisite.

## File Structure

```text
.github/workflows/ci.yml
.gitignore
VideoTriage.sln
build/
  GeneratePackageAssets.ps1
  New-DevelopmentPackageCertificate.ps1
docs/
  installation.md
src/
  VideoTriage.App/
    Properties/PublishProfiles/win-x64.pubxml
  VideoTriage.Package/
    Assets/Square44x44Logo.png
    Assets/Square150x150Logo.png
    Assets/StoreLogo.png
    Package.appxmanifest
    VideoTriage.Package.wapproj
```

### Task 1: Add Deterministic Package Assets

**Files:**
- Create: `build/GeneratePackageAssets.ps1`
- Create: `src/VideoTriage.Package/Assets/Square44x44Logo.png`
- Create: `src/VideoTriage.Package/Assets/Square150x150Logo.png`
- Create: `src/VideoTriage.Package/Assets/StoreLogo.png`

- [ ] **Step 1: Create the asset generator**

Create `build/GeneratePackageAssets.ps1`:

```powershell
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-VideoTriageLogo {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [int] $Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(20, 28, 40))

        $accent = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(92, 200, 255))
        try {
            $points = [System.Drawing.PointF[]] @(
                [System.Drawing.PointF]::new($Size * 0.34, $Size * 0.22),
                [System.Drawing.PointF]::new($Size * 0.34, $Size * 0.78),
                [System.Drawing.PointF]::new($Size * 0.76, $Size * 0.50)
            )
            $graphics.FillPolygon($accent, $points)
        }
        finally {
            $accent.Dispose()
        }

        $directory = Split-Path -Parent $Path
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$assetDirectory = Join-Path $PSScriptRoot '..\src\VideoTriage.Package\Assets'
New-VideoTriageLogo (Join-Path $assetDirectory 'Square44x44Logo.png') 44
New-VideoTriageLogo (Join-Path $assetDirectory 'Square150x150Logo.png') 150
New-VideoTriageLogo (Join-Path $assetDirectory 'StoreLogo.png') 50

Get-ChildItem $assetDirectory -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length
```

- [ ] **Step 2: Generate the assets**

Run:

```powershell
pwsh -NoProfile -File build/GeneratePackageAssets.ps1
```

Expected: the command exits `0` and lists exactly these three non-empty files:

```text
Square150x150Logo.png
Square44x44Logo.png
StoreLogo.png
```

- [ ] **Step 3: Validate PNG dimensions**

Run:

```powershell
Add-Type -AssemblyName System.Drawing
$expected = @{
    'Square44x44Logo.png' = 44
    'Square150x150Logo.png' = 150
    'StoreLogo.png' = 50
}
foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path 'src/VideoTriage.Package/Assets' $entry.Key
    $image = [System.Drawing.Image]::FromFile((Resolve-Path $path))
    try {
        if ($image.Width -ne $entry.Value -or $image.Height -ne $entry.Value) {
            throw "$($entry.Key) is $($image.Width)x$($image.Height)"
        }
    }
    finally {
        $image.Dispose()
    }
}
'Package asset dimensions are valid.'
```

Expected:

```text
Package asset dimensions are valid.
```

- [ ] **Step 4: Commit the generated assets**

```powershell
git add build/GeneratePackageAssets.ps1 src/VideoTriage.Package/Assets
git commit -m "chore(package): add deterministic MSIX assets"
```

Expected: one commit containing the script and three PNG files.

### Task 2: Publish The WPF App Self-Contained

**Files:**
- Create: `src/VideoTriage.App/Properties/PublishProfiles/win-x64.pubxml`

- [ ] **Step 1: Create the publish profile**

Create `src/VideoTriage.App/Properties/PublishProfiles/win-x64.pubxml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishProtocol>FileSystem</PublishProtocol>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <DeleteExistingFiles>true</DeleteExistingFiles>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Publish with the profile**

Run:

```powershell
dotnet publish src/VideoTriage.App/VideoTriage.App.csproj `
  -c Release `
  -p:PublishProfile=Properties/PublishProfiles/win-x64.pubxml
```

Expected: exit code `0`, a `Publish succeeded` message, and
`src/VideoTriage.App/bin/Release/net10.0-windows/win-x64/publish/VideoTriage.App.exe`.

- [ ] **Step 3: Prove the output is self-contained**

Run:

```powershell
$publish = 'src/VideoTriage.App/bin/Release/net10.0-windows/win-x64/publish'
$required = @(
    'VideoTriage.App.exe',
    'VideoTriage.App.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'PresentationFramework.dll'
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $publish $_)) }
if ($missing) { throw "Missing self-contained files: $($missing -join ', ')" }
'Self-contained win-x64 publish is complete.'
```

Expected:

```text
Self-contained win-x64 publish is complete.
```

- [ ] **Step 4: Commit**

```powershell
git add src/VideoTriage.App/Properties/PublishProfiles/win-x64.pubxml
git commit -m "build(app): add self-contained win-x64 profile"
```

### Task 3: Add The Windows Application Packaging Project

**Files:**
- Create: `src/VideoTriage.Package/VideoTriage.Package.wapproj`
- Create: `src/VideoTriage.Package/Package.appxmanifest`
- Modify: `VideoTriage.sln`

- [ ] **Step 1: Create the packaging project**

Create `src/VideoTriage.Package/VideoTriage.Package.wapproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0"
         DefaultTargets="Build"
         xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <WapProjPath Condition="'$(WapProjPath)' == ''">$(MSBuildExtensionsPath)\Microsoft\DesktopBridge\</WapProjPath>
  </PropertyGroup>

  <Import Project="$(WapProjPath)\Microsoft.DesktopBridge.props" />

  <PropertyGroup>
    <ProjectGuid>{71C772CE-C6ED-4A67-87DF-78794B9031ED}</ProjectGuid>
    <TargetPlatformVersion>10.0.26100.0</TargetPlatformVersion>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <DefaultLanguage>en-US</DefaultLanguage>
    <EntryPointProjectUniqueName>..\VideoTriage.App\VideoTriage.App.csproj</EntryPointProjectUniqueName>
    <GenerateAppInstallerFile>false</GenerateAppInstallerFile>
    <AppxBundle>Never</AppxBundle>
    <AppxBundlePlatforms>x64</AppxBundlePlatforms>
    <AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <AppxManifest Include="Package.appxmanifest">
      <SubType>Designer</SubType>
    </AppxManifest>
  </ItemGroup>

  <ItemGroup>
    <Content Include="Assets\Square44x44Logo.png" />
    <Content Include="Assets\Square150x150Logo.png" />
    <Content Include="Assets\StoreLogo.png" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\VideoTriage.App\VideoTriage.App.csproj">
      <Project>{1CBCE2A6-95A2-4533-96FA-A626A546D665}</Project>
      <Name>VideoTriage.App</Name>
      <SkipGetTargetFrameworkProperties>true</SkipGetTargetFrameworkProperties>
      <PublishProfile>Properties\PublishProfiles\win-x64.pubxml</PublishProfile>
    </ProjectReference>
  </ItemGroup>

  <Import Project="$(WapProjPath)\Microsoft.DesktopBridge.targets" />
</Project>
```

- [ ] **Step 2: Create the package manifest**

Create `src/VideoTriage.Package/Package.appxmanifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <Identity
    Name="YovanMc.VideoTriage"
    Publisher="CN=YovanMc"
    Version="1.0.0.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>VideoTriage</DisplayName>
    <PublisherDisplayName>YovanMc</PublisherDisplayName>
    <Description>Safely triage and recompress videos with verify-before-replace checks.</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily
      Name="Windows.Desktop"
      MinVersion="10.0.17763.0"
      MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application
      Id="App"
      Executable="$targetnametoken$.exe"
      EntryPoint="$targetentrypoint$">
      <uap:VisualElements
        DisplayName="VideoTriage"
        Description="Safely triage and recompress videos with verify-before-replace checks."
        BackgroundColor="#141C28"
        Square44x44Logo="Assets\Square44x44Logo.png"
        Square150x150Logo="Assets\Square150x150Logo.png" />
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

- [ ] **Step 3: Add the project to the solution**

Run:

```powershell
dotnet sln VideoTriage.sln add `
  src/VideoTriage.Package/VideoTriage.Package.wapproj `
  --solution-folder src
```

Expected:

```text
Project `src\VideoTriage.Package\VideoTriage.Package.wapproj` added to the solution.
```

- [ ] **Step 4: Verify the fixed identity and project reference**

Run:

```powershell
[xml]$manifest = Get-Content -Raw src/VideoTriage.Package/Package.appxmanifest
if ($manifest.Package.Identity.Name -ne 'YovanMc.VideoTriage') { throw 'Wrong package identity.' }
if ($manifest.Package.Identity.Publisher -ne 'CN=YovanMc') { throw 'Wrong publisher.' }
if ($manifest.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'Wrong architecture.' }
if (-not (Select-String -Path src/VideoTriage.Package/VideoTriage.Package.wapproj `
    -SimpleMatch '..\VideoTriage.App\VideoTriage.App.csproj')) {
    throw 'WPF project reference is missing.'
}
'Packaging project contract is valid.'
```

Expected:

```text
Packaging project contract is valid.
```

- [ ] **Step 5: Commit**

```powershell
git add VideoTriage.sln src/VideoTriage.Package
git commit -m "chore(package): add WPF application packaging project"
```

### Task 4: Add Development Certificate Generation

**Files:**
- Create: `build/New-DevelopmentPackageCertificate.ps1`
- Modify: `.gitignore`

- [ ] **Step 1: Create the certificate helper**

Create `build/New-DevelopmentPackageCertificate.ps1`:

```powershell
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\package-signing'),

    [Parameter(Mandatory)]
    [string] $Password
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw 'Password must not be empty.'
}

$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

$pfxPath = Join-Path $output 'VideoTriage.Development.pfx'
$cerPath = Join-Path $output 'VideoTriage.Development.cer'
$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
        '2.5.29.19={text}'
    ) `
    -Subject 'CN=YovanMc' `
    -FriendlyName 'VideoTriage Development Package Signing'

try {
    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $pfxPath `
        -Password $securePassword `
        -Force | Out-Null
    Export-Certificate `
        -Cert $certificate `
        -FilePath $cerPath `
        -Type CERT `
        -Force | Out-Null
}
finally {
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force
}

Write-Output "PFX=$pfxPath"
Write-Output "CER=$cerPath"
```

- [ ] **Step 2: Ignore all private signing material**

Append to `.gitignore`:

```gitignore

# Package signing private keys
*.pfx
```

- [ ] **Step 3: Generate and inspect a certificate**

Run:

```powershell
pwsh -NoProfile -File build/New-DevelopmentPackageCertificate.ps1 `
  -Password 'VideoTriage-Development-Only'

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    (Resolve-Path 'artifacts/package-signing/VideoTriage.Development.pfx'),
    'VideoTriage-Development-Only')

if ($certificate.Subject -ne 'CN=YovanMc') {
    throw "Certificate subject was $($certificate.Subject)"
}
'Development certificate publisher matches the manifest.'
```

Expected:

```text
PFX=<absolute path>\artifacts\package-signing\VideoTriage.Development.pfx
CER=<absolute path>\artifacts\package-signing\VideoTriage.Development.cer
Development certificate publisher matches the manifest.
```

- [ ] **Step 4: Confirm private material is ignored**

Run:

```powershell
git check-ignore artifacts/package-signing/VideoTriage.Development.pfx
git status --short
```

Expected: `git check-ignore` prints the PFX path, and `git status --short` does not list a `.pfx`.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore build/New-DevelopmentPackageCertificate.ps1
git commit -m "build(package): generate disposable signing certificates"
```

### Task 5: Build And Inspect A Signed MSIX Locally

**Files:**
- Verify only; no source changes.

- [ ] **Step 1: Confirm required build tooling**

Run from a Developer PowerShell for Visual Studio:

```powershell
dotnet --version
msbuild -version
$desktopBridge = Join-Path ${env:ProgramFiles(x86)} `
  'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $desktopBridge)) { throw 'Visual Studio Installer was not found.' }
& $desktopBridge -latest -products * `
  -requires Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging `
  -property installationPath
```

Expected: .NET reports a `10.0.*` SDK, MSBuild reports a version, and `vswhere` prints one Visual
Studio installation path. If `vswhere` prints nothing, install the Visual Studio **MSIX Packaging
Tools** optional component before continuing; this is a machine prerequisite, not a repository
change.

- [ ] **Step 2: Generate the disposable signing certificate**

```powershell
$password = 'VideoTriage-Development-Only'
pwsh -NoProfile -File build/New-DevelopmentPackageCertificate.ps1 -Password $password
```

Expected: one `.pfx` and one `.cer` under `artifacts/package-signing`.

- [ ] **Step 3: Restore the packaging graph**

```powershell
msbuild src/VideoTriage.Package/VideoTriage.Package.wapproj `
  /t:Restore `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:RuntimeIdentifier=win-x64
```

Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 4: Build the signed package**

```powershell
$pfx = (Resolve-Path 'artifacts/package-signing/VideoTriage.Development.pfx').Path
$packageDir = Join-Path (Resolve-Path 'artifacts').Path 'msix\'

msbuild src/VideoTriage.Package/VideoTriage.Package.wapproj `
  /m `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:RuntimeIdentifier=win-x64 `
  /p:UapAppxPackageBuildMode=SideLoadOnly `
  /p:GenerateAppInstallerFile=false `
  /p:AppxBundle=Never `
  /p:AppxPackageDir="$packageDir" `
  /p:PackageCertificateKeyFile="$pfx" `
  /p:PackageCertificatePassword="$password"
```

Expected: `Build succeeded.`, `0 Error(s)`, and exactly one `.msix` under `artifacts/msix`.

- [ ] **Step 5: Inspect the package**

Run:

```powershell
$packages = @(Get-ChildItem artifacts/msix -Recurse -Filter '*.msix')
if ($packages.Count -ne 1) { throw "Expected one MSIX, found $($packages.Count)." }

$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
  -Recurse -Filter makeappx.exe |
  Sort-Object FullName -Descending |
  Select-Object -First 1
if (-not $makeAppx) { throw 'makeappx.exe was not found.' }

$unpack = 'artifacts/msix-inspect'
if (Test-Path $unpack) { Remove-Item $unpack -Recurse -Force }
& $makeAppx.FullName unpack /p $packages[0].FullName /d $unpack /o

[xml]$builtManifest = Get-Content -Raw "$unpack/AppxManifest.xml"
if ($builtManifest.Package.Identity.Name -ne 'YovanMc.VideoTriage') {
    throw 'Built package identity is wrong.'
}
if ($builtManifest.Package.Identity.ProcessorArchitecture -ne 'x64') {
    throw 'Built package is not x64.'
}
if (-not (Test-Path "$unpack/VideoTriage.App.exe")) {
    throw 'Packaged executable is missing.'
}
if (-not (Test-Path "$unpack/coreclr.dll")) {
    throw 'Packaged app is not self-contained.'
}
if (Get-ChildItem $unpack -Recurse -File |
    Where-Object Name -Match '^(ffmpeg|ffprobe|HandBrakeCLI)\.exe$') {
    throw 'An external media tool was bundled.'
}
'MSIX identity, architecture, self-contained runtime, and prerequisite boundary are valid.'
```

Expected:

```text
MSIX identity, architecture, self-contained runtime, and prerequisite boundary are valid.
```

### Task 6: Write Installation And Recovery Documentation

**Files:**
- Create: `docs/installation.md`

- [ ] **Step 1: Create the installation guide**

Create `docs/installation.md` with this content:

```markdown
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

CI artifacts are signed with a disposable self-signed development certificate. The artifact contains
one `.msix` and `VideoTriage.Development.cer`; it never contains the private `.pfx`.

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

- An original is removed only after a smaller replacement passes every enabled verification check
  and the replacement is confirmed on disk.
- Recycle Bin is the default deletion mode.
- Permanent deletion requires an explicit setting and visible warning.
- Pause, cancellation, missing tools, low disk space, encode failure, verification failure, poster
  failure, or an exception leaves the original untouched.
- ffmpeg, ffprobe, and HandBrakeCLI remain separately installed tools and are not updated by
  VideoTriage.

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
Application state and result records are under `%LocalAppData%\VideoTriage\Data`; diagnostic logs
are under `%LocalAppData%\VideoTriage\Logs`.

## Uninstall

Open **Settings > Apps > Installed apps**, select **VideoTriage**, and choose **Uninstall**. Uninstall
does not remove ffmpeg, ffprobe, HandBrakeCLI, source videos, or files under the VideoTriage data and
log directories.
```

- [ ] **Step 2: Check documentation commands and safety terms**

Run:

```powershell
$required = @(
  'winget install --exact --id Gyan.FFmpeg',
  'winget install --exact --id HandBrake.HandBrake.CLI',
  'Dry-run stops after discovery, probe, and classification',
  'Recycle Bin is the default',
  '.videotriage.partial.',
  '%LocalAppData%\VideoTriage\Logs'
)
$text = Get-Content -Raw docs/installation.md
$missing = $required | Where-Object { -not $text.Contains($_) }
if ($missing) { throw "Installation guide is missing: $($missing -join ', ')" }
'Installation documentation covers prerequisites, dry-run, safety, and recovery.'
```

Expected:

```text
Installation documentation covers prerequisites, dry-run, safety, and recovery.
```

- [ ] **Step 3: Commit**

```powershell
git add docs/installation.md
git commit -m "docs: add MSIX installation and recovery guide"
```

### Task 7: Build And Upload The Test-Signed MSIX In CI

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Replace the CI workflow**

Replace `.github/workflows/ci.yml` with:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        shell: pwsh
        run: |
          dotnet restore src/VideoTriage.App/VideoTriage.App.csproj
          dotnet restore src/VideoTriage.Cli/VideoTriage.Cli.csproj
          dotnet restore tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
          dotnet restore tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj

      - name: Build
        shell: pwsh
        run: |
          dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Release --no-restore
          dotnet build src/VideoTriage.Cli/VideoTriage.Cli.csproj -c Release --no-restore
          dotnet build tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj -c Release --no-restore
          dotnet build tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj -c Release --no-restore

      - name: Test
        shell: pwsh
        run: |
          dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj -c Release --no-build --verbosity normal
          dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj -c Release --no-build --verbosity normal

  package:
    runs-on: windows-latest
    needs: build-and-test
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Setup MSBuild
        uses: microsoft/setup-msbuild@v2

      - name: Create disposable development certificate
        shell: pwsh
        env:
          PACKAGE_PASSWORD: VideoTriage-CI-Development-Only
        run: |
          pwsh -NoProfile -File build/New-DevelopmentPackageCertificate.ps1 `
            -Password $env:PACKAGE_PASSWORD

      - name: Restore package project
        shell: pwsh
        run: |
          msbuild src/VideoTriage.Package/VideoTriage.Package.wapproj `
            /t:Restore `
            /p:Configuration=Release `
            /p:Platform=x64 `
            /p:RuntimeIdentifier=win-x64

      - name: Build signed x64 MSIX
        shell: pwsh
        env:
          PACKAGE_PASSWORD: VideoTriage-CI-Development-Only
        run: |
          $pfx = (Resolve-Path 'artifacts/package-signing/VideoTriage.Development.pfx').Path
          $packageDir = Join-Path (Resolve-Path 'artifacts').Path 'msix\'
          msbuild src/VideoTriage.Package/VideoTriage.Package.wapproj `
            /m `
            /p:Configuration=Release `
            /p:Platform=x64 `
            /p:RuntimeIdentifier=win-x64 `
            /p:UapAppxPackageBuildMode=SideLoadOnly `
            /p:GenerateAppInstallerFile=false `
            /p:AppxBundle=Never `
            /p:AppxPackageDir="$packageDir" `
            /p:PackageCertificateKeyFile="$pfx" `
            /p:PackageCertificatePassword="$env:PACKAGE_PASSWORD"

      - name: Stage public package artifact
        shell: pwsh
        run: |
          $msix = @(Get-ChildItem artifacts/msix -Recurse -Filter '*.msix')
          if ($msix.Count -ne 1) {
            throw "Expected exactly one MSIX, found $($msix.Count)."
          }
          New-Item -ItemType Directory -Force artifacts/upload | Out-Null
          Copy-Item $msix[0].FullName artifacts/upload/
          Copy-Item artifacts/package-signing/VideoTriage.Development.cer artifacts/upload/
          Copy-Item docs/installation.md artifacts/upload/
          Remove-Item artifacts/package-signing/VideoTriage.Development.pfx -Force
          if (Get-ChildItem artifacts/upload -Filter '*.pfx') {
            throw 'Private signing material entered the upload directory.'
          }

      - name: Upload MSIX
        uses: actions/upload-artifact@v4
        with:
          name: VideoTriage-win-x64-msix
          path: artifacts/upload/
          if-no-files-found: error
```

- [ ] **Step 2: Validate the workflow policy locally**

Run:

```powershell
$workflow = Get-Content -Raw .github/workflows/ci.yml
$required = @(
    'needs: build-and-test',
    'microsoft/setup-msbuild@v2',
    'VideoTriage.Package/VideoTriage.Package.wapproj',
    'AppxBundle=Never',
    'VideoTriage.Development.cer',
    'actions/upload-artifact@v4'
)
$forbidden = @(
    'gh release create',
    'actions/create-release',
    'git tag',
    'git push'
)
foreach ($value in $required) {
    if (-not $workflow.Contains($value)) { throw "CI is missing $value" }
}
foreach ($value in $forbidden) {
    if ($workflow.Contains($value)) { throw "CI must not contain $value" }
}
'CI builds after tests, uploads MSIX plus CER, and does not publish.'
```

Expected:

```text
CI builds after tests, uploads MSIX plus CER, and does not publish.
```

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/ci.yml
git commit -m "ci: build test-signed Windows package artifact"
```

### Task 8: Final Verification, Self-Review, And Handoff

**Files:**
- Verify all files from Tasks 1-7.

- [ ] **Step 1: Run the complete Release build and test suite**

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

Expected: all four commands exit `0`; both test projects report `Failed: 0`.

- [ ] **Step 2: Rebuild and inspect the signed package**

Repeat Task 5 Steps 2-5.

Expected: exactly one x64 MSIX, packaged `VideoTriage.App.exe`, packaged `coreclr.dll`, no bundled
media-tool executables, and no build errors.

- [ ] **Step 3: Perform a disposable install smoke test**

Run:

```powershell
$cer = (Resolve-Path 'artifacts/package-signing/VideoTriage.Development.cer').Path
$msix = @(Get-ChildItem artifacts/msix -Recurse -Filter '*.msix').Single().FullName
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
Add-AppxPackage $msix
Get-AppxPackage YovanMc.VideoTriage |
    Select-Object Name, Version, Architecture, Status
```

Expected: one installed package with:

```text
Name         YovanMc.VideoTriage
Version      1.0.0.0
Architecture X64
Status       Ok
```

Launch VideoTriage from the Start menu. Expected: the real WPF app opens; missing external tools, if
any, are reported by prerequisite status and keep Start disabled.

Remove the disposable package:

```powershell
Get-AppxPackage YovanMc.VideoTriage | Remove-AppxPackage
```

Expected: `Get-AppxPackage YovanMc.VideoTriage` returns no package afterward.

- [ ] **Step 4: Run the plan self-review checks**

```powershell
git diff --check

rg -n "TBD|TODO|implement later|choose one|or alternatively|NullTriagePipeline" `
  src/VideoTriage.Package `
  build/GeneratePackageAssets.ps1 `
  build/New-DevelopmentPackageCertificate.ps1 `
  docs/installation.md `
  .github/workflows/ci.yml

git status --short
```

Expected:

- `git diff --check` prints nothing.
- `rg` prints nothing.
- `git status --short` contains no `.pfx`, no unpacked package directory, and only intentional
  source changes if commits have not yet been made.

- [ ] **Step 5: Review every architecture-contract requirement**

Confirm in the diff:

```text
[ ] Packaging uses a separate Windows Application Packaging Project.
[ ] The WAP project references VideoTriage.App and uses the win-x64 self-contained profile.
[ ] Package identity and certificate publisher are exact matches.
[ ] Package output is one x64 MSIX, not a bundle.
[ ] ffmpeg, ffprobe, and HandBrakeCLI are absent from package contents.
[ ] Installation docs say the .NET runtime is included.
[ ] Installation docs cover prerequisites, startup checks, dry-run, Recycle Bin default,
    cancellation safety, data/log locations, and .videotriage.partial.* recovery.
[ ] CI runs packaging only after tests pass.
[ ] CI uploads the MSIX, public CER, and installation guide.
[ ] CI deletes the PFX before artifact upload.
[ ] No tag, push, GitHub release, Store upload, or automatic publication was added.
```

Expected: every box can be checked from committed files or verification output.

- [ ] **Step 6: Commit any final corrections**

```powershell
git add `
  .github/workflows/ci.yml `
  .gitignore `
  VideoTriage.sln `
  build/GeneratePackageAssets.ps1 `
  build/New-DevelopmentPackageCertificate.ps1 `
  docs/installation.md `
  src/VideoTriage.App/Properties/PublishProfiles/win-x64.pubxml `
  src/VideoTriage.Package
git commit -m "chore(release): finalize Windows installation artifact"
```

Expected: commit succeeds, or Git reports there is nothing to commit because all work was committed
task-by-task.

## Self-Review

- **Spec coverage:** Tasks 1-5 create and prove a self-contained x64 MSIX through the Microsoft-
  recommended WAP route; Task 6 documents installation and safety; Task 7 builds the artifact in CI;
  Task 8 performs final build, package inspection, installation, and policy review.
- **Placeholder scan:** The plan contains fixed identity, publisher, version, platform, SDK,
  commands, expected outputs, artifact contents, and documentation text. There are no unresolved
  packaging alternatives.
- **Safety:** No media tool is bundled, no private certificate is committed or uploaded, and
  publication remains outside CI.
- **Type and path consistency:** `VideoTriage.App`, `VideoTriage.Package`, `YovanMc.VideoTriage`,
  `CN=YovanMc`, `win-x64`, and all data/log paths match the architecture contract and this plan.

## Execution Handoff

Execute on `feature/packaging-installation` only after logging and diagnostics are integrated into
updated `main`. Use the task commits above, then return:

```text
Status: COMPLETE or NEEDS_CONTEXT
Branch: feature/packaging-installation
Verification: Release build/test results; MSIX inspection result; install smoke-test result
Artifact: exact .msix path and public .cer path
Changed files: exact git diff --name-only output
Safety review: external tools absent; PFX absent; no publish action added
```

Use `NEEDS_CONTEXT` only when the Visual Studio MSIX Packaging Tools component or Windows SDK
`10.0.26100.0` is unavailable and cannot be installed in the execution environment. Do not change
the package model, SDK target, identity, publisher, or signing policy to work around that machine
prerequisite.
