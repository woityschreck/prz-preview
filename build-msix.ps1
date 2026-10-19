# Baut das MSIX-Paket für PRZ Vorschau.
#
#   .\build-msix.ps1              Paket bauen -> dist\PRZ-Vorschau-<Version>.msix (unsigniert, für den Store-Upload)
#   .\build-msix.ps1 -Register    zusätzlich lokal registrieren (nur mit eingeschaltetem Entwicklermodus)
#   .\build-msix.ps1 -Unregister  lokale Test-Registrierung wieder entfernen
#
# Voraussetzungen: VS 2017 Build Tools (C++), Windows SDK 10.0.19041 (makeappx).
param([switch]$Register, [switch]$Unregister)
$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$version  = '1.0.3.0'
$sdkVer   = '10.0.19041.0'
$vcvars   = 'C:\Program Files (x86)\Microsoft Visual Studio\2017\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$makeappx = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\$sdkVer\x64\makeappx.exe"
$layout   = Join-Path $root 'obj\msix-layout'
$identity = Get-Content (Join-Path $root 'msix\identity.json') -Raw -Encoding UTF8 | ConvertFrom-Json

function Remove-DevPackage {
    $pkg = Get-AppxPackage -Name $identity.IdentityName -ErrorAction SilentlyContinue
    if ($pkg) {
        Get-Process prevhost, PrzViewer -ErrorAction SilentlyContinue | Stop-Process -Force
        Remove-AppxPackage -Package $pkg.PackageFullName
        Write-Host "Test-Registrierung entfernt: $($pkg.PackageFullName)"
    }
}

if ($Unregister) { Remove-DevPackage; return }

# --- 1. .NET-Teile (PrzPreview.dll, PrzViewer.exe) --------------------------------------
& cmd /c "`"$root\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'build.cmd fehlgeschlagen' }

# --- 2. Native Brücke PrzShim.dll (x64, statische C-Laufzeit -> keine VC-Redistributable nötig)
$obj = Join-Path $root 'obj\shim'
New-Item -ItemType Directory -Force $obj | Out-Null
$shim = Join-Path $root 'src\shim'
$netfx = "${env:ProgramFiles(x86)}\Windows Kits\NETFXSDK\4.8\Include\um"   # mscoree.h / metahost.h
$cl = "cl /nologo /utf-8 /LD /O2 /MT /EHsc /W4 /DUNICODE /D_UNICODE /I`"$netfx`" /Fo`"$obj\\`" `"$shim\PrzShim.cpp`" " +
      "/link /DEF:`"$shim\PrzShim.def`" /OUT:`"$root\bin\PrzShim.dll`" /IMPLIB:`"$obj\PrzShim.lib`" ole32.lib uuid.lib"
& cmd /c "call `"$vcvars`" $sdkVer >nul && $cl"
if ($LASTEXITCODE -ne 0) { throw 'Kompilieren der Brücke (PrzShim.dll) fehlgeschlagen' }

# --- 3. Paket-Ordner zusammenstellen -----------------------------------------------------
Remove-DevPackage    # eine registrierte Testversion sperrt den Ordner
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
foreach ($f in 'PrzPreview.dll', 'PrzViewer.exe', 'PrzShim.dll') { Copy-Item (Join-Path $root "bin\$f") $layout }
foreach ($a in 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png', 'StoreLogo.png') {
    Copy-Item (Join-Path $root "store\assets\$a") "$layout\Assets"
}
$manifest = Get-Content (Join-Path $root 'msix\AppxManifest.template.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('{{IdentityName}}', $identity.IdentityName).Replace('{{Publisher}}', $identity.Publisher).
                      Replace('{{PublisherDisplayName}}', $identity.PublisherDisplayName).Replace('{{Version}}', $version)
[IO.File]::WriteAllText("$layout\AppxManifest.xml", $manifest, (New-Object Text.UTF8Encoding $false))

# --- 4. MSIX packen --------------------------------------------------------------------
$short = $version -replace '\.0$', ''
$out = Join-Path $root "dist\PRZ-Vorschau-$short.msix"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& $makeappx pack /d $layout /p $out /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx fehlgeschlagen' }
Write-Host "MSIX erstellt: $out"

# --- 5. Optional lokal registrieren (Entwicklermodus) -----------------------------------
if ($Register) {
    Add-AppxPackage -Register "$layout\AppxManifest.xml"
    $pkg = Get-AppxPackage -Name $identity.IdentityName
    Write-Host "Lokal registriert: $($pkg.PackageFullName)"
    Write-Host "Installationsordner: $($pkg.InstallLocation)"
}
