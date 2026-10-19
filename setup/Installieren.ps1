# PRZ Vorschau – Installation (systemweit, benötigt einmal Adminrechte)
param([switch]$Elevated)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if ($Elevated) { Write-Host "Keine Adminrechte erhalten – Abbruch."; Read-Host "Enter zum Schließen"; exit 1 }
    # Genau EIN Neustart mit Adminrechten; -Elevated verhindert eine Endlosschleife
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    exit
}
$ErrorActionPreference = 'Stop'

$name    = 'PRZ Vorschau'
$version = '1.0.3'
$clsid   = '{224aabb0-d365-4cea-95af-e95c1979a035}'
$iid     = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'   # IPreviewHandler
$appId   = '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}'   # prevhost.exe (64 Bit)
$target  = Join-Path $env:ProgramFiles 'PRZ Vorschau'

try {
    # Laufende Vorschau-Prozesse beenden, sonst ist die DLL gesperrt
    Get-Process prevhost, PrzViewer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    # Dateien kopieren
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($f in 'PrzPreview.dll', 'PrzViewer.exe', 'Deinstallieren.ps1') {
        Copy-Item (Join-Path $PSScriptRoot $f) $target -Force
    }
    $dll = Join-Path $target 'PrzPreview.dll'
    $asm = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
    $codeBase = 'file:///' + ($dll -replace '\\', '/')

    # COM-Klasse (läuft in prevhost.exe)
    $cls = "HKLM:\Software\Classes\CLSID\$clsid"
    New-Item -Path $cls -Force | Out-Null
    Set-ItemProperty $cls '(default)' $name
    Set-ItemProperty $cls 'AppID' $appId
    Set-ItemProperty $cls 'DisplayName' $name
    $inproc = "$cls\InprocServer32"
    New-Item -Path $inproc -Force | Out-Null
    Set-ItemProperty $inproc '(default)' 'mscoree.dll'
    Set-ItemProperty $inproc 'ThreadingModel' 'Apartment'
    Set-ItemProperty $inproc 'Class' 'PrzPreview.PrzPreviewHandler'
    Set-ItemProperty $inproc 'Assembly' $asm
    Set-ItemProperty $inproc 'RuntimeVersion' 'v4.0.30319'
    Set-ItemProperty $inproc 'CodeBase' $codeBase

    # Vorschau-Handler der Endung .prz zuordnen (die bestehende Standard-Verknüpfung bleibt unangetastet)
    $ext = "HKLM:\Software\Classes\SystemFileAssociations\.prz\shellex\$iid"
    New-Item -Path $ext -Force | Out-Null
    Set-ItemProperty $ext '(default)' $clsid
    Set-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers' $clsid $name

    # Eintrag unter "Apps & Features" zum Deinstallieren
    $un = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PRZVorschau'
    New-Item -Path $un -Force | Out-Null
    Set-ItemProperty $un 'DisplayName' $name
    Set-ItemProperty $un 'DisplayVersion' $version
    Set-ItemProperty $un 'Publisher' 'woityschreck'
    Set-ItemProperty $un 'InstallLocation' $target
    Set-ItemProperty $un 'UninstallString' ("powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"" + (Join-Path $target 'Deinstallieren.ps1') + "`"")
    Set-ItemProperty $un 'NoModify' 1 -Type DWord
    Set-ItemProperty $un 'NoRepair' 1 -Type DWord

    # Alte Test-Registrierung aus der Entwicklung (nur aktueller Benutzer) entfernen
    Remove-Item "HKCU:\Software\Classes\CLSID\$clsid" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Classes\SystemFileAssociations\.prz\shellex\$iid" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Classes\.prz\shellex\$iid" -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "PRZ Vorschau $version wurde installiert nach: $target"
    Write-Host "Explorer-Fenster neu öffnen, eine .prz anklicken und das Vorschaufenster mit Alt+P einschalten."
}
catch {
    Write-Host "FEHLER: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host "Enter zum Schließen"
