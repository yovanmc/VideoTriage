[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ProcessName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ($null -eq ('VideoTriage.WindowCapture.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace VideoTriage.WindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);
    }
}
'@
}

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1

if ($null -eq $process) {
    throw "No visible process named '$ProcessName' with a main window was found."
}

$windowHandle = $process.MainWindowHandle
[void] [VideoTriage.WindowCapture.NativeMethods]::SetForegroundWindow($windowHandle)
Start-Sleep -Milliseconds 500

$rect = [VideoTriage.WindowCapture.Rect]::new()
if (-not [VideoTriage.WindowCapture.NativeMethods]::GetWindowRect(
        $windowHandle,
        [ref] $rect)) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "Could not get the window rectangle for process '$ProcessName' (Win32 error $errorCode)."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 800 -or $height -lt 500) {
    throw "Window is too small to capture: ${width}x${height}; minimum size is 800x500."
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($output)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$bitmap = $null
$graphics = $null
try {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen(
        $rect.Left,
        $rect.Top,
        0,
        0,
        [System.Drawing.Size]::new($width, $height))
    $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)

    Write-Output "Captured ${width}x${height} to $output"
}
finally {
    if ($null -ne $graphics) {
        $graphics.Dispose()
    }

    if ($null -ne $bitmap) {
        $bitmap.Dispose()
    }
}
