# PRZ Vorschau 1.0.4

Explorer-Vorschau für CAM-Part-Dateien (`.prz`):

- 3D-Modell (Fertigteil) und bearbeitetes Rohteil, drehbar
- Operationen mit Bearbeitungszeiten, sortierbar nach Dauer
- Gesamtzeit, Maschine, Werkstoff, Anzahl der eingesetzten Werkzeuge und der Nullpunkte

## Voraussetzungen

- Windows 10 oder 11, 64 Bit (.NET Framework 4.8 ist dort bereits enthalten)
- Das CAM-System muss **nicht** installiert sein

## Installation

1. Ordner entpacken (nicht direkt aus dem ZIP starten).
2. `Installieren.cmd` doppelklicken und die Admin-Abfrage mit „Ja“ bestätigen.
3. Explorer-Fenster neu öffnen, eine `.prz` anklicken, Vorschaufenster mit `Alt+P` einschalten.
   Falls die Vorschau leer bleibt: Task-Manager → „Windows-Explorer“ → Neu starten.

Zusätzlich: `PrzViewer.exe` (im Installationsordner) öffnet `.prz`-Dateien in einem eigenen Fenster, auch per Drag & Drop.

## Deinstallation

- Einstellungen → Apps → „PRZ Vorschau“ → Deinstallieren
- oder `Deinstallieren.ps1` im Installationsordner ausführen

## Hinweise

- **Bearbeitungszeiten entstehen bei der Simulation** im CAM-System – nicht beim Berechnen der Jobs und nicht beim Erzeugen des NC-Programms. Fehlt eine Zeit, wurde der Job nach der letzten Simulation geändert. Für vollständige Zeiten: Teil simulieren, dann speichern.
- **Keine Vorschau bei heruntergeladenen Dateien?** Windows zeigt für Dateien aus dem Internet (Browser-Download, E-Mail-Anhang) keine Vorschau. Abhilfe: Rechtsklick → *Eigenschaften* → **„Zulassen“** anhaken → OK.
- Fehlerprotokoll: `%LOCALAPPDATA%Low\PrzPreview.log`
- Installiert nach: `C:\Program Files\PRZ Vorschau`
