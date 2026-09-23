# Launches the built app, brings its window to the front, and saves a PNG of exactly the visible
# window frame, then closes the app. Uses DWM's extended frame bounds: GetWindowRect includes the
# invisible resize margin and would bleed in whatever sits behind the window.
#
# Dependencies: Windows, PowerShell 7+ (pwsh) or Windows PowerShell 5.1; a built app:
#                 dotnet build TranscriptBuilder.slnx -c Release
# Run:          pwsh tools/screenshot/capture.ps1 -OutputPath .github/screenshot-light.png
#                 [-ComposeText "<sample prompt to show>"] [-DelaySeconds 3]
#                 [-ExePath "<path to Claude Code Transcript Builder.exe>"]
#               -ComposeText fills the compose box through UI Automation (its accessible name).
# Note:         the app opens whichever project and theme %AppData%\TranscriptBuilder\settings.json
#               remembers. Point it at a sanitized demo project first (and back up/restore that file)
#               so no real paths or prompts end up in the image. The app is force-closed, which skips
#               its Closing handler, so the remembered window size is left untouched.

param(
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $ComposeText,
    [string] $ExePath = "TranscriptBuilder/bin/Release/net10.0-windows/Claude Code Transcript Builder.exe",
    [int] $DelaySeconds = 3
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CaptureNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
"@

$DwmwaExtendedFrameBounds = 9
$fullOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)

$process = Start-Process -FilePath $ExePath -PassThru
try {
    Start-Sleep -Seconds $DelaySeconds
    $process.Refresh()
    $hwnd = $process.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { throw "The app window didn't appear within $DelaySeconds seconds." }

    if ($ComposeText) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "Prompt compose box")
        $composeBox = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
        if ($null -eq $composeBox) { throw "Compose box not found (is a project loaded?)." }
        $composeBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($ComposeText)
    }

    [CaptureNative]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 500

    $rect = New-Object CaptureNative+RECT
    $size = [Runtime.InteropServices.Marshal]::SizeOf([type][CaptureNative+RECT])
    $hr = [CaptureNative]::DwmGetWindowAttribute($hwnd, $DwmwaExtendedFrameBounds, [ref]$rect, $size)
    if ($hr -ne 0) { throw "DwmGetWindowAttribute failed (HRESULT $hr)." }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
        $bitmap.Save($fullOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Output "Saved $fullOutputPath ($width x $height)"
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.Dispose()
}
