# Registriert die PRZ-Vorschau im Explorer (systemweit – Windows startet prevhost.exe nur für HKLM-Registrierungen).
# Startet sich bei Bedarf selbst mit Adminrechten neu.
param([switch]$Elevated)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if ($Elevated) { Write-Host "Keine Adminrechte erhalten – Abbruch."; Read-Host "Enter zum Schließen"; exit 1 }
    # Genau EIN Neustart mit Adminrechten; -Elevated verhindert eine Endlosschleife
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    exit
}

$ErrorActionPreference = 'Stop'
$clsid   = '{224aabb0-d365-4cea-95af-e95c1979a035}'
$iid     = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'   # IPreviewHandler
$appId   = '{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}'   # prevhost.exe (64 Bit)
$name    = 'PRZ Vorschau'
$dll     = Join-Path $PSScriptRoot 'bin\PrzPreview.dll'
if (-not (Test-Path $dll)) { throw "Nicht gefunden: $dll – zuerst build.cmd ausführen." }

$asm = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
$codeBase = 'file:///' + ($dll -replace '\\', '/')

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
New-Item -Path "$cls\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}" -Force | Out-Null

# Vorschau-Handler der Endung .prz zuordnen (SystemFileAssociations lässt die bestehende Verknüpfung unangetastet)
$ext = "HKLM:\Software\Classes\SystemFileAssociations\.prz\shellex\$iid"
New-Item -Path $ext -Force | Out-Null
Set-ItemProperty $ext '(default)' $clsid

$list = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers'
Set-ItemProperty $list $clsid $name

# Laufende Vorschau-Prozesse beenden, damit die neue Version geladen wird
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "Installiert. Explorer-Fenster ggf. neu öffnen und Vorschaufenster mit Alt+P einschalten."
