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
    public enum ProcessDpiAwareness
    {
        Unaware = 0,
        SystemAware = 1,
        PerMonitorAware = 2
    }

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
        private const int DwmExtendedFrameBounds = 9;
        private const int ErrorAccessDenied = 5;
        private const int ErrorInvalidParameter = 87;
        private const int EAccessDenied = unchecked((int)0x80070005);
        private static readonly IntPtr DpiAwarenessContextPerMonitorAware =
            new IntPtr(-3);
        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 =
            new IntPtr(-4);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(
            IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(
            IntPtr dpiContextA,
            IntPtr dpiContextB);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(
            ProcessDpiAwareness value);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(
            IntPtr processHandle,
            out ProcessDpiAwareness value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            out Rect value,
            int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        public static int ExtendedFrameBoundsAttribute
        {
            get { return DwmExtendedFrameBounds; }
        }

        public static string EnsurePerMonitorDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(
                    DpiAwarenessContextPerMonitorAwareV2))
                {
                    return "per-monitor-v2";
                }

                int error = Marshal.GetLastWin32Error();
                if (IsCurrentThreadPerMonitorAware())
                {
                    return "existing per-monitor";
                }

                if (error != ErrorAccessDenied &&
                    error != ErrorInvalidParameter)
                {
                    throw new InvalidOperationException(
                        "SetProcessDpiAwarenessContext failed with Win32 error " +
                        error + ".");
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Windows versions before 10 Creators Update use SHCore below.
            }

            try
            {
                int result = SetProcessDpiAwareness(
                    ProcessDpiAwareness.PerMonitorAware);
                if (result == 0)
                {
                    return "per-monitor";
                }

                ProcessDpiAwareness current;
                int queryResult = GetProcessDpiAwareness(
                    IntPtr.Zero,
                    out current);
                if (queryResult == 0 &&
                    current == ProcessDpiAwareness.PerMonitorAware)
                {
                    return "existing per-monitor";
                }

                if (result == EAccessDenied)
                {
                    throw new InvalidOperationException(
                        "The host process already has weaker DPI awareness. " +
                        "Start this script in a per-monitor-DPI-aware host.");
                }

                throw new InvalidOperationException(
                    "SetProcessDpiAwareness failed with HRESULT 0x" +
                    result.ToString("X8") + ".");
            }
            catch (DllNotFoundException)
            {
                throw new PlatformNotSupportedException(
                    "Per-monitor DPI awareness requires Windows 8.1 or later.");
            }
            catch (EntryPointNotFoundException)
            {
                throw new PlatformNotSupportedException(
                    "Per-monitor DPI awareness requires Windows 8.1 or later.");
            }
        }

        private static bool IsCurrentThreadPerMonitorAware()
        {
            try
            {
                IntPtr current = GetThreadDpiAwarenessContext();
                return AreDpiAwarenessContextsEqual(
                           current,
                           DpiAwarenessContextPerMonitorAwareV2) ||
                       AreDpiAwarenessContextsEqual(
                           current,
                           DpiAwarenessContextPerMonitorAware);
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
'@
}

$dpiMode = [VideoTriage.WindowCapture.NativeMethods]::EnsurePerMonitorDpiAwareness()
Write-Verbose "DPI awareness: $dpiMode"

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and
        [VideoTriage.WindowCapture.NativeMethods]::IsWindowVisible(
            $_.MainWindowHandle)
    } |
    Select-Object -First 1

if ($null -eq $process) {
    throw "No visible process named '$ProcessName' with a main window was found."
}

$windowHandle = $process.MainWindowHandle
if (-not [VideoTriage.WindowCapture.NativeMethods]::IsWindow($windowHandle)) {
    throw "The main window for process '$ProcessName' is no longer valid."
}

if ([VideoTriage.WindowCapture.NativeMethods]::IsIconic($windowHandle)) {
    throw "The main window for process '$ProcessName' is minimized."
}

if (-not [VideoTriage.WindowCapture.NativeMethods]::SetForegroundWindow(
        $windowHandle)) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "Could not foreground the window for process '$ProcessName' (Win32 error $errorCode)."
}

Start-Sleep -Milliseconds 500

if ([VideoTriage.WindowCapture.NativeMethods]::GetForegroundWindow() -ne
        $windowHandle) {
    throw "The window for process '$ProcessName' did not remain in the foreground."
}

$rect = [VideoTriage.WindowCapture.Rect]::new()
$dwmResult = [VideoTriage.WindowCapture.NativeMethods]::DwmGetWindowAttribute(
    $windowHandle,
    [VideoTriage.WindowCapture.NativeMethods]::ExtendedFrameBoundsAttribute,
    [ref] $rect,
    [Runtime.InteropServices.Marshal]::SizeOf($rect))
if ($dwmResult -ne 0) {
    Write-Warning (
        "DwmGetWindowAttribute failed with HRESULT 0x{0:X8}; " +
        "falling back to GetWindowRect." -f $dwmResult)
    if (-not [VideoTriage.WindowCapture.NativeMethods]::GetWindowRect(
            $windowHandle,
            [ref] $rect)) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "Could not get the window rectangle for process '$ProcessName' (Win32 error $errorCode)."
    }
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Window has invalid capture bounds: ${width}x${height}."
}

if ($width -lt 800 -or $height -lt 500) {
    throw "Window is too small to capture: ${width}x${height}; minimum size is 800x500."
}

$virtualLeft = [VideoTriage.WindowCapture.NativeMethods]::GetSystemMetrics(76)
$virtualTop = [VideoTriage.WindowCapture.NativeMethods]::GetSystemMetrics(77)
$virtualWidth = [VideoTriage.WindowCapture.NativeMethods]::GetSystemMetrics(78)
$virtualHeight = [VideoTriage.WindowCapture.NativeMethods]::GetSystemMetrics(79)
$virtualRight = $virtualLeft + $virtualWidth
$virtualBottom = $virtualTop + $virtualHeight
if ($virtualWidth -le 0 -or $virtualHeight -le 0) {
    throw 'Could not determine the virtual desktop bounds.'
}

if ($rect.Left -lt $virtualLeft -or $rect.Top -lt $virtualTop -or
    $rect.Right -gt $virtualRight -or $rect.Bottom -gt $virtualBottom) {
    throw "Window capture bounds are partially or completely off-screen."
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
