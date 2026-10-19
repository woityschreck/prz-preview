# Store-Screenshots: PrzViewer im Format 1366x768 (Mindestgröße für Desktop-Screenshots im Store)
param([string[]]$Files)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace StoreShot -Name U -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct R { public int l, t, r, b; }
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
'@
[StoreShot.U]::SetProcessDPIAware() | Out-Null   # echte Pixel statt skalierter Koordinaten
$out = Join-Path $PSScriptRoot 'screenshots'
New-Item -ItemType Directory -Force $out | Out-Null
$viewer = Join-Path (Split-Path $PSScriptRoot) 'bin\PrzViewer.exe'
$n = 1
foreach ($f in $Files) {
    $p = Start-Process $viewer -ArgumentList ('"' + $f + '"') -PassThru
    Start-Sleep -Seconds 4
    $p.Refresh(); $h = $p.MainWindowHandle
    # Ganz nach vorn, feste Größe (etwas größer, damit das Bild ohne Rahmen >= 1366x768 bleibt)
    [StoreShot.U]::SetWindowPos($h, [IntPtr](-1), 40, 40, 1382, 784, 0x40) | Out-Null
    [StoreShot.U]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Seconds 3   # Fenster-Animation abwarten
    $r = New-Object StoreShot.U+R
    [StoreShot.U]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap 1366, 768
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.l, $r.t, 0, 0, $bmp.Size)
    $name = "Screenshot-$n.png"
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Stop-Process -Id $p.Id -Force
    Write-Host "  $name  ($([IO.Path]::GetFileName($f)))  Fenster $($r.r - $r.l)x$($r.b - $r.t)"
    $n++
}
