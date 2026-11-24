# Visual check: launch d3d-target, inject ReShade, open overlay, capture a screenshot.
# All comments are ASCII on purpose (this file has no BOM, PS 5.1 reads it as ANSI).
param(
    [string]$Deploy = "",
    [string]$Out = ""
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

[void][Win32]::SetProcessDPIAware()

if (-not $Deploy) {
    $root = Split-Path -Parent $PSScriptRoot
    $Deploy = Join-Path (Split-Path -Parent $root) 'ReShadeInjector'
}
if (-not $Out) { $Out = Join-Path $Deploy 'overlay-check.png' }

# "\u6d4b\u8bd5" is the Chinese folder name; build it from code points because this
# file is read as ANSI by PS 5.1 (no BOM) and a literal would get mangled.
$testDirName = [string]([char]0x6D4B) + [string]([char]0x8BD5)
$targetExe = Join-Path (Join-Path $Deploy $testDirName) 'd3d-target.exe'
$reShade   = Join-Path $Deploy 'DLL\ReShade64.dll'
$injector  = Join-Path $Deploy '_inject.exe'

Write-Host ("target : " + $targetExe)
Write-Host ("dll    : " + $reShade)
Write-Host ("inject : " + $injector)

Get-Process d3d-target -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$outLog = Join-Path $Deploy 'visual-target-out.txt'
$errLog = Join-Path $Deploy 'visual-target-err.txt'
$proc = Start-Process -FilePath $targetExe -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
Write-Host ("target pid = " + $proc.Id)

# inject before the 8s sleep ends, otherwise the D3D device exists before ReShade hooks it
Start-Sleep -Milliseconds 900
& $injector $proc.Id $reShade 2>&1 | Select-Object -Last 3

Start-Sleep -Seconds 9

$proc.Refresh()
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { throw "target window not found" }

[void][Win32]::ShowWindow($hwnd, 5)
[void][Win32]::SetWindowPos($hwnd, [IntPtr]::new(-1), 40, 40, 900, 620, 0x0040)
[void][Win32]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 700

# VK_HOME = 0x24 ; ReShade toggles its overlay with the Home key
[Win32]::keybd_event(0x24, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 120
[Win32]::keybd_event(0x24, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1800

$r = New-Object Win32+RECT
[void][Win32]::GetWindowRect($hwnd, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host ("window rect = " + $w + " x " + $h)

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win32]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("PrintWindow ok = " + $ok)
Write-Host ("saved: " + $Out)

Get-Process d3d-target -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
