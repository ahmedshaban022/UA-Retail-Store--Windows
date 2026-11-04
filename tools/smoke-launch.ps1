param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

$exe = $ExePath
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot 'docs\phases\phase0-screens'

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Win {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<string> ForProcess(uint target) {
        var list = new List<string>();
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == target) {
                var t = new StringBuilder(512);
                GetWindowText(h, t, 512);
                var c = new StringBuilder(256);
                GetClassName(h, c, 256);
                RECT r;
                GetWindowRect(h, out r);
                list.Add(string.Format("visible={0} class={1} title=[{2}] rect={3},{4},{5}x{6}",
                    IsWindowVisible(h), c.ToString(), t.ToString(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@

$p = Start-Process -FilePath $exe -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 10
$p.Refresh()
Write-Output ('alive=' + (-not $p.HasExited))

if (-not $p.HasExited) {
    Write-Output '--- top-level windows ---'
    foreach ($w in [Win]::ForProcess([uint32]$p.Id)) { Write-Output ('  ' + $w) }

    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    Write-Output ('screen ' + $bounds.Width + 'x' + $bounds.Height)
    $shot = Join-Path $outDir 'phase0-firstrun.png'
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $g.Dispose()
    $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ('screenshot: ' + $shot + ' bytes=' + (Get-Item $shot).Length)

    Start-Sleep -Seconds 3
    Write-Output '--- windows after 3 more seconds ---'
    foreach ($w in [Win]::ForProcess([uint32]$p.Id)) { Write-Output ('  ' + $w) }

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Write-Output 'stopped'
}
