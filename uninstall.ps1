# Entfernt die PRZ-Vorschau wieder (systemweit und evtl. alte Benutzer-Einträge).
param([switch]$Elevated)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if ($Elevated) { Write-Host "Keine Adminrechte erhalten – Abbruch."; Read-Host "Enter zum Schließen"; exit 1 }
    # Genau EIN Neustart mit Adminrechten; -Elevated verhindert eine Endlosschleife
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    exit
}
$clsid = '{224aabb0-d365-4cea-95af-e95c1979a035}'
$iid   = '{8895b1c6-b41f-4c1c-a562-0d564250836f}'
foreach ($root in 'HKLM:', 'HKCU:') {
    Remove-Item "$root\Software\Classes\SystemFileAssociations\.prz\shellex\$iid" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$root\Software\Classes\.prz\shellex\$iid" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$root\Software\Classes\CLSID\$clsid" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty "$root\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers" $clsid -ErrorAction SilentlyContinue
}
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "Deinstalliert."
