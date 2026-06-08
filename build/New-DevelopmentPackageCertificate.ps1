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
