# Pulse · PC Optimizer

Native portable Windows-App mit dunklem Dashboard, Live-Leistung, vorsichtiger App-Verwaltung, Memory Reducer, Speedtest und signierten In-App-Updates. Windows 10/11 x64. Kein Konto und keine Werbung. Pulse selbst benötigt keine Administratorrechte; Hersteller-Deinstallationen können eine Windows-Administratorbestätigung verlangen.

## Start

[Aktuelle Version auf GitHub](https://github.com/lukescharf28-prog/pulse-pc-optimizer/releases/latest). `Pulse.exe` in einen eigenen beschreibbaren Ordner legen und starten. Die EXE enthält die .NET-Laufzeit. Kein Installer erforderlich. Die EXE besitzt kein kommerzielles Authenticode-Zertifikat; Update-Metadaten werden separat kryptografisch signiert.

## Funktionen

- **Übersicht:** belegter/verfügbarer RAM, CPU-Auslastung, dedizierter GPU-Speicher und RAM-Verlauf der letzten 60 Messungen.
- **Memory Reducer:** Prozess auswählen und „RAM reduzieren“ anklicken. Nutzt Windows `EmptyWorkingSet`, ohne das Programm zu beenden. Das reduziert den physischen Arbeitssatz kurzfristig; benötigte Seiten werden erneut geladen. Es kann Ruckler verursachen und garantiert keine schnellere Ausführung. Kein vollständiger Nachbau von Mem Reduct; Standby-Listen und Kernel-Caches werden nicht geleert.
- **Hintergrund-RAM reduzieren:** nach einer Namensvorschau die Arbeitssätze von bis zu 30 zugänglichen Hintergrundprozessen ab 32 MB reduzieren. Fensterprogramme und geschützte Prozesse werden übersprungen. Die summierten Arbeitssatz-Differenzen können gemeinsame Seiten enthalten und entsprechen nicht der global freigegebenen RAM-Menge.
- **GPU & VRAM:** nach dediziertem GPU-Speicher sortierte Prozessliste. Nicht benötigte Programme bewusst schließen, um deren Ressourcen freizugeben. Keine Übertaktung oder Treibermanipulation. Gemeinsamer RAM der GPU wird nicht als VRAM angezeigt. Bei mehreren GPUs werden Werte summiert. Prozesswerte können wegen gemeinsam genutzter Ressourcen von der Gesamtanzeige abweichen.
- **Prozesse:** Suche nach Name/PID, RAM-/CPU-/GPU-/I/O-Werte, sortierbare Spalten, RAM-Reduktion und normale Schließen-Anfrage. I/O umfasst Dateien, Netzwerk und Geräte, nicht nur die Festplatte. Keine Schaltfläche zum erzwungenen Beenden.
- **Live-Leistung:** CPU, RAM, GPU-Engines, Datenträgeraktivität, Lesen/Schreiben, zugesicherter Speicher, Laufwerksspeicher und aktueller Netzwerkdurchsatz mit Verlauf.
- **Temperaturen:** NVIDIA-GPU über den vorhandenen Treiber; CPU, GPU, RAM und Laufwerke optional über die WMI-Schnittstelle eines bereits laufenden LibreHardwareMonitor. Kein Sensortreiber wird installiert. Fehlende Werte werden nicht geschätzt. Sensorabfrage höchstens alle 5 Sekunden.
- **Apps & Spiele:** konservative Auswahl klassischer Drittanbieter-Apps bekannter Hersteller und Steam-Spiele. Windows-/Microsoft-Produkte, Treiber, Dienste, Laufzeitbibliotheken, versteckte Systemkomponenten und unbekannte Hersteller bleiben ausgeblendet. Die Liste ist absichtlich nicht vollständig; Store-Apps werden nicht deinstalliert. Pulse öffnet nach Bestätigung den geprüften normalen Hersteller-/MSI-/Steam-Deinstallationsdialog. Es löscht keine App-Ordner oder Registry-Einträge selbst.
- **Speedtest:** HTTP-Ping, Download und Upload gegen Cloudflare, Datenlimit 10–300 MB, Dauer je Phase 3–20 Sekunden, 1–4 Verbindungen und Abbruch. Nur synthetische Testdaten. Windows bestimmt den Weg über WLAN, LAN oder VPN. HTTP-Ping ist keine ICMP-Spielserver-Latenz. Kurze Tests können schnelle Anschlüsse unterschätzen.
- **Updates:** Prüfung beim Start und alle 30 Minuten, abschaltbar. Bei neuer signierter Version erscheint ein Update-Button. Download und Installation nur nach Klick. Signatur, Dateigröße, SHA-256, Produkt und Version werden geprüft. Die alte EXE bleibt als `Pulse.exe.previous` erhalten; bei fehlender Startbestätigung versucht der Updater die Wiederherstellung.
- **Einstellungen:** Monitoring 1/3/5/10 Sekunden, Pause bei Minimierung, 30–180 Verlaufsmessungen, Sensorabfrage, Celsius/Fahrenheit, geschützte Prozesse ausblenden, zusätzliche Schutzliste, RAM-Mindestgröße, Sammellimit, Abkühlzeit, Speedtest-Datenlimit, Dauer, Verbindungen, WLAN-Voraussetzung und Update-Prüfung. Die manuelle RAM-Sammelaktion ist standardmäßig deaktiviert. Windows-Autostart-, Grafik- und Energieeinstellungen sind verlinkt.

Windows-/kritische Prozesse, Microsoft-/Treiber-/Sicherheitsprodukte, andere Benutzer/Sitzungen und Pulse selbst sind von Aktionen ausgeschlossen. Vor jeder Aktion werden Prozess-ID, Startzeit, Pfad und Berechtigung erneut geprüft. Eigene Schutzausnahmen können feste Sperren nur erweitern. Es gibt keine automatische Prozessbeendigung oder Sammeldeinstallation. Normales Schließen kann Speicherdialoge öffnen oder ein Programm nur in den Infobereich verschieben.

**Keine Software kann jede Folge einer Deinstallation oder eines Programmendes ausschließen.** Auch entbehrliche Apps können wichtige eigene Daten, Einstellungen oder Spielstände enthalten. Vor einer Deinstallation sichern. Ein Hintergrundprozess ist nicht automatisch unbenutzt.

## Messwerte und Datenschutz

GPU-Zähler sind vom Grafiktreiber abhängig; fehlende Werte werden mit „Nicht verfügbar“/„—“ angezeigt. Integrierte GPUs können 0 dedizierten Speicher melden. Monitoring und App-Inventar werden nicht übertragen. Der Speedtest verbindet sich mit `speed.cloudflare.com`; Cloudflare sieht IP-Adresse und synthetische Messdaten. Update-Prüfungen verbinden sich mit GitHub und dessen Asset-Servern. Keine zusätzliche Analyse-/Werbetelemetrie. Einstellungen liegen in `%LOCALAPPDATA%\Pulse\settings.json`, Update-Staging in `%LOCALAPPDATA%\Pulse\Updates`. Aktivitäten bleiben im RAM.

## Selbst bauen

.NET 10 SDK mit Windows-Desktop-Unterstützung benötigt:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
```

## Funktionstest

```powershell
Start-Process .\Pulse.exe -ArgumentList '--self-test', 'test-result.json' -Wait
```

Der Test prüft Live-Metriken, Prozessschutz, veraltete Identitäten, RAM-Reduktion, normales Schließen und erzwungenes Beenden. Aktionen werden ausschließlich an eigenen kurzlebigen Testprozessen ausgeführt. Die GPU-Verfügbarkeit wird dokumentiert, da sie hardwareabhängig ist.

`--ui-test <Ordner>` navigiert durch die Oberfläche und rendert Ansichten. `--network-test <Bericht.json>` führt einen echten Cloudflare-Test mit höchstens 10 MB Nutzdaten aus. Tests deinstallieren keine Apps.

## Neue Version veröffentlichen

Version in `Pulse.csproj` erhöhen, testen und veröffentlichen. `scripts/Sign-Release.ps1` erhält den EXE-Pfad, den separat aufbewahrten privaten Schlüssel, die vierteilige Version (z. B. 1.2.0.0), den Ausgabe-Pfad und Release-Notizen. Ein stabiles GitHub-Release `v1.2.0` anlegen und `Pulse.exe` sowie `pulse-update.json` hochladen. Der öffentliche Prüfschlüssel ist in der App eingebettet. **Den privaten Schlüssel niemals committen oder hochladen.** Ohne passenden Schlüssel werden Updates vorhandener Installationen abgelehnt. Die Updatefunktion erzeugt keine neuen Programmversionen selbst; diese müssen veröffentlicht werden.

## Technische Quellen

- https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset
- https://learn.microsoft.com/en-us/windows/win32/psapi/working-set-information
- https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw
- https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw

Dies ist eine eigenständige App, kein offizielles Mem-Reduct-Produkt.
