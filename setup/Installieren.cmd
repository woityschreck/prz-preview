@echo off
rem Doppelklick-Starter: umgeht die PowerShell-Ausführungsrichtlinie nur für dieses Skript.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installieren.ps1"
