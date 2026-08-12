# Changelog

## 0.10.3 – 2026-08-12

- Alle editierbaren Freitext-, Such-, Farb-, Zahlen- und Zeitfelder verwenden
  jetzt einen dunkelgrünen Hintergrund mit fetter weißer Schrift. Normal-,
  Hover-, Aktiv- und Fokuszustände behalten den hohen Kontrast; der Fokus wird
  zusätzlich durch eine grüne Kontur hervorgehoben.
- Hauptfenster, Meldungseditor und abgekoppelte Tafeln lassen sich wieder
  flüssig an ihrer Titelleiste verschieben und behalten die Position nach dem
  Loslassen zuverlässig bei.
- Die Viewportbegrenzung setzt eine Fensterposition nicht mehr bei jedem
  UI-Update zurück, sondern nur nach einer tatsächlichen Auflösungs- oder
  Skalierungsänderung. Die endgültige Drag-Position wird nach dem nächsten
  UI-Layout-Takt übernommen und nur korrigiert, wenn sie außerhalb des
  sichtbaren Bereichs liegt.
- Öffentliche API und Assembly-Bindung bleiben V1, das Persistenzschema bleibt
  20; für bestehende Spielstände ist keine Migration erforderlich.
- Release-Build, 138.275 Core-Assertions sowie sämtliche IL-/Reflection-,
  Lokalisierungs-, Rollback- und Paketprüfungen wurden erfolgreich ausgeführt.

## 0.10.2 – 2026-08-12

- Der Meldungseditor zeigt den **MELDUNGSTITEL** jetzt als erstes Feld und
  erklärt direkt, dass er auf dem Meldeschlitz und im Verlauf erscheint. Der
  Aktivzustand einer Regel ist sichtbar und editierbar; dadurch lassen sich
  insbesondere bewusst deaktivierte Panelkopien ohne Umweg aktivieren.
- Eine feste Aktionsleiste hält Validierungsstatus, Speichern, Verwerfen und
  Löschen auch in langen Entwürfen erreichbar. Fehlende Titel, Zielpanels oder
  Bedingungen sowie ungültige Farb- und Zeitwerte werden konkret benannt.
  Bei minimalem Fenster und hoher Skalierung reduziert sich die Leiste auf
  eine kompakte, vollständig per Tooltip erklärte Zeile.
- Bedingungen verwenden unter schmalen Viewports responsive Karten statt
  überbreiter Tabellen. Zeitsteuerung und Eskalation liegen in einem zunächst
  eingeklappten Bereich, der Standardwerte, vorhandene Konfiguration oder
  verdeckte Eingabefehler bereits im Kopf anzeigt.
- Eigene Meldeschlitze bieten eine sichtbare **BEARBEITEN**-Aktion; Doppelklick
  bleibt als Abkürzung erhalten. Inaktive Regeln werden gekennzeichnet.
  Filter und zyklische Auswahlen zeigen ihre verfügbaren Werte nachvollziehbar.
- Status- und Fehlermeldungen bleiben außerhalb langer Scrollbereiche sichtbar.
  Tastaturfokus, `Strg+Eingabe`, `Esc`, beschriftete Metadaten und größere
  Ziele verhindern parallele Spielaktionen und verbessern die Bedienbarkeit.
- Alarmtext wählt abhängig von der Hintergrundfarbe automatisch Schwarz oder
  Weiß. Der Verlauf verwendet ruhige, kontrastreiche Zustandsfarben. Die neue
  Option **REDUZIERTE BEWEGUNG** ersetzt blinkende Alarmhintergründe durch eine
  stabile Hervorhebung und ist Teil von Darstellungstransferprofilen.
- Hauptfenster, Editor und abgekoppelte Tafeln speichern Position, bevorzugte
  Größe und Öffnungszustand. Automatische Viewport- oder Skalierungsbegrenzung,
  Pinning und Historian-Vollbild überschreiben keine Nutzerpräferenzen;
  Layoutimporte werden sofort sichtbar angewandt.
- MultiLangLib bleibt Pflichtabhängigkeit und externe Assembly. Alle 21
  Sprachkataloge besitzen 1.069 identische Schlüssel; neue sichtbare Texte
  laufen über `UnmaText`. API und Assembly-Bindung bleiben V1,
  Persistenzschema 20 bleibt kompatibel.
- Release-Build, 138.229 Core-Assertions, sämtliche IL-/Reflection-,
  Lokalisierungs-, Rollback- und Paketprüfungen wurden erfolgreich ausgeführt.

## 0.10.1 – 2026-08-11

- Unter **OPTIONEN** kann die aktuelle Konfiguration als spielstandsübergreifendes
  Standardprofil gespeichert und in einer anderen Welt mit Vorschau importiert
  werden. Meldungsregeln, Systemmeldungen, Alarmfarben/UI-Skalierung und
  Fensterpositionen sind einzeln auswählbar.
- Nur wenn `%LOCALAPPDATA%\UNMA\profiles\default.json` tatsächlich fehlt,
  erzeugt und persistiert UNMA **UNMA Recommended Quiet**. Diese
  Initialisierung überschreibt keine vorhandene Datei. Exakt erkannte,
  unveränderte frühere Built-ins mit sechs Silent-Regeln oder mit sechs
  Silent- und zwei Hidden-Regeln werden nur im Speicher aktualisiert; ihre
  Seed-Dateien sowie abweichende und benutzerdefinierte Profile bleiben
  unverändert. Importiert wird weiterhin erst nach Vorschau und ausdrücklicher
  Bestätigung.
- Das empfohlene Profil stellt `UpgradeInProgress`, `DowngradeInProgress`,
  `VehicleGoalStruggling`, `VehicleNoReachableDesignations`,
  `NoTreesToHarvest` und `ExcavatorHasNoValidTruck` auf Silent. Nur der
  UNMA-Ton entfällt; CoI-Meldung, HOME und Verlauf bleiben erhalten.
- `TruckCannotDeliver` und `TruckCannotDeliverMixedCargo` stehen auf Ignored.
  UNMA verwirft neue Ereignisse vor `SetAlarm`, Verlauf und Persistenz. Import
  und Normalisierung bereinigen passende aktive Zustände und Memories sowie
  ältere globale Verlaufseinträge, sofern keine spezifischere nicht ignorierende
  Regel gilt. Das senkt Incident-Lens- und Spielstandlast bei flackernden
  `NotificationId`-Werten; die originale CoI-Meldung bleibt unverändert.
  `CannotDeliverFromMineTower`, `VehicleGoalUnreachable` und `VehicleNoFuel`
  bleiben hörbar.
- Der Import führt ausgewählte Werte atomar mit der Zielwelt zusammen: Die
  Vorschau unterscheidet neue, geänderte, unveränderte und übersprungene Werte;
  vorhandene Werte außerhalb der Auswahl bleiben erhalten.
- Vanilla-Regeln nach stabiler Meldungsart (`NotificationType`) und
  Entity-Prototyp sind portabel. Instanzgebundene Entity-Regeln werden wegen
  ihrer weltabhängigen Entity-ID sicher übersprungen und im Ergebnis gemeldet.
- Manuell bearbeitete oder beschädigte Profile werden vor dem Merge semantisch
  geprüft. Ungültige Werte bleiben unverändert und erscheinen als übersprungen;
  nicht installierte Töne werden bereits in der Vorschau gemeldet.
- Verlauf, aktive Alarme, Quittierungen, Timer und sonstige Laufzeit-Memories
  werden weder exportiert noch importiert. Importierte Ignore-Regeln wirken nur
  in UNMA; die ursprüngliche Captain-of-Industry-Meldung bleibt unverändert.
- Das atomar geschriebene Standardprofil liegt unter
  `%LOCALAPPDATA%\UNMA\profiles\default.json`. Das öffentliche Erweiterungs-API
  und die Assembly-Bindung bleiben auf V1, das Persistenzschema bleibt 20.

## 0.10.0 – 2026-08-11

- 0.10.0 bündelt und stabilisiert die vollständige Ausbaureihe 0.9.19 bis
  0.9.26: Bediener-Keybinds, Verlaufssuche und -export, atomare Panelkopien,
  spielzeitpersistente Verzögerungen und Hysterese, schlitzbezogene Tonpause,
  Eskalation und sichere Operatoraktionen, Historian mit Prognose,
  Betriebsbereiche sowie die nur lesende Incident-Linse.
- Alle Belegungen des optionalen Keybind Frameworks werden nun unterdrückt,
  solange ein natives UNMA-Textfeld Fokus besitzt. Neu belegte Buchstabentasten
  können beim Schreiben damit keine Quittierung oder andere Aktion auslösen.
- Hauptfenster, Editor und abgekoppelte Panels verwenden viewportbegrenzte,
  skalenabhängige Mindestgrößen. Die nutzbare logische Arbeitsfläche bleibt
  dadurch auch bei 200 Prozent erhalten.
- Alle Hauptansichten folgen nun einem einheitlichen dunklen Kartenraster mit
  konsistenten Abständen, Eingabefeldern und responsiven Aktionszeilen. Verlauf,
  Systemmeldungen, Benachrichtigungsoptionen, Instrumente und Optionen nutzen
  den verfügbaren Platz ohne leere Aktionsspalten oder horizontales Überlaufen.
- Instrumenttitel werden kontrolliert gekürzt und Messwerte kollisionsfrei
  dargestellt. Der Historian verwendet echtes CoI-Vollbild und kehrt danach
  exakt zu Größe und Position des Bedienfensters zurück.
- Interne CoI-Kernmodule erscheinen nicht mehr fälschlich als ungültige externe
  API-Anbieter; textbasierte **BEREICHE**- und **BEARBEITEN**-Aktionen ersetzen
  auf Systemen ohne passende Symbolglyphe die bisherigen Platzhalterkästchen.
- Konfigurationen aus Schema 21 oder höher werden vor vollständiger
  Deserialisierung erkannt. UNMA lässt Hauptdatei, Backup-, Temp- und
  Broken-Artefakte unangetastet und sperrt weitere Schreibvorgänge für die
  Sitzung, statt unbekannte Felder mit dem Schema-20-Modell zu verlieren.
- Das öffentliche Erweiterungs-API und die Assembly-Bindung bleiben auf V1;
  das Persistenzschema bleibt 20. Für 0.10.0 ist keine neue Migration nötig.
- Release-Build, 125.696 Core-Assertions sowie alle IL-/Reflection-,
  Lokalisierungs-, Rollback- und Paketprüfungen wurden erfolgreich ausgeführt.
- Die Versionsfolge springt bewusst von 0.9.26 auf 0.10.0; 0.9.27 wurde nicht
  veröffentlicht.

## 0.9.26 – 2026-08-11

- Die nur auf HOME sichtbare **INCIDENT-LINSE** gruppiert aktive Alarme anhand
  ihrer Kommen-Zeitpunkte. Ein Abstand von höchstens zwei Spieltagen bildet
  einen zeitlichen Cluster; **ERSTES SIGNAL** bezeichnet nur dessen früheste
  Meldung und ausdrücklich keine bestätigte Ursache.
- Cluster respektieren **ALLE**, **NICHT ZUGEORDNET** und konkrete
  Betriebsbereiche. Der getrennte globale Alarmdruck gewichtet Vorkommen der
  letzten zehn Spieltage nach Stufe und zeigt Anzahl sowie verschiedene Alarme
  als **NORMAL**, **ERHÖHT**, **STURM** oder **SCHWER**.
- Die erweiterte Ansicht zeigt höchstens sechs Incident-Karten mit je acht
  Mitgliedern und weist auf weitere Einträge hin. **FOKUS** springt nur zu
  einer noch sichtbaren Meldung beziehungsweise ihrem Objekt.
- Analyse und Fokus quittieren, verbergen oder löschen keine Meldung, ändern
  weder Ton noch Verlauf und führen keinerlei eigene Alarmzustände ein.
- Ein revisionsgebundener, unveränderlich ersetzter History-Cache begrenzt den
  globalen Druck auf die neuesten 8.192 Vorkommen. Sortierung und Analyse
  laufen außerhalb des Alarm-Locks; unveränderte Frames scannen den Verlauf
  nicht erneut. Bei dauerhaft wechselnder Revision liefert UNMA nach höchstens
  zwei Versuchen einen konsistenten lokalen Snapshot, statt die UI zu blockieren.
- Die Incident-Linse wird ausschließlich aus Laufzeit-Snapshots abgeleitet und
  nicht gespeichert. 0.9.26 benötigt deshalb keine neue Schema-Migration.

## 0.9.25 – 2026-08-11

- Benutzerdefinierte Betriebsbereiche gruppieren globale Panels, ohne
  Alarmzustand, Verlauf, Historian oder Audioverhalten zu verändern. **ALLE**,
  **NICHT ZUGEORDNET** und jeder eigene Bereich filtern Panelleiste und HOME.
- Das gefilterte HOME führt aktive Meldungen der zugehörigen Panels zu genau
  einem Schlitz je Alarm zusammen. **BEREICH QUITT.** und **BEREICH WEITER**
  arbeiten nur in der ausgewählten Ansicht; **ALLES QUITTIEREN** bleibt global.
- Quittierung gehört weiterhin zum eigentlichen Alarmzustand: Ist dieselbe
  Meldung über mehrere Bereiche sichtbar, wird ihre Quittierung überall
  wirksam und erzeugt weder Kopien noch getrennte Verlaufsereignisse.
- Bereiche lassen sich in einem atomaren Entwurf anlegen, umbenennen,
  umsortieren und löschen. Beim Löschen werden ihre Panels ausschließlich
  **NICHT ZUGEORDNET**; Panels, Schlitze, Regeln und Zustände bleiben erhalten.
- Die Panel-Einstellungen weisen Bereiche direkt zu. Duplikate übernehmen den
  Bereich ihrer Quelle; neue Panels übernehmen nur einen konkret ausgewählten
  Bereich und bleiben unter **ALLE** oder **NICHT ZUGEORDNET** unzugeordnet.
- Entwurfswechsel und Schließen schützen ungespeicherte Panel- und
  Bereichseinstellungen. Schmale Fenster und 200-%-Skalierung verwenden
  gestapelte, scrollbar bleibende Layouts.
- Schema 20 migriert vorhandene Welten ohne Bereichszuordnung. Dadurch bleibt
  ihre bisherige **ALLE**-Ansicht unverändert.

## 0.9.24 – 2026-08-10

- Jedes Instrument besitzt jetzt unabhängig vom Anzeigetyp einen gemeinsamen
  **HIST**-Zugang. Die große Historienansicht verwendet Spielzeitfenster von
  einem Tag bis zur gesamten sitzungsbasierten Laufzeit.
- Der Historian zeigt aktuellen Wert, Minimum, Mittelwert und Maximum sowie
  eine numerisch stabile lineare Rate pro Spielmonat und deren R²-Güte.
- Bei einem belastbaren steigenden oder fallenden Trend berechnet UNMA eine
  gerichtete ETA zur oberen beziehungsweise unteren Instrumentenskala. Wenige,
  stabile, unzuverlässige und über 100 Jahre entfernte Verläufe werden klar
  getrennt ausgewiesen.
- Diagramm und Analyse verwenden denselben atomaren Laufzeit-Snapshot und
  dasselbe inklusive Zeitfenster. Spielzeit-Rücksprünge beginnen eine neue
  Historienepoche, statt Messpunkte aus einer zukünftigen Zeitleiste zu mischen.
- Forecast-Ausfall, fehlende Quelle und unzureichende Daten bleiben getrennte
  Zustände; Diagramm und aktueller Wert werden soweit möglich weiter angezeigt.

## 0.9.23 – 2026-08-10

- Eigene Regeln können nach einer frei wählbaren aktiven Spielzeit einmalig auf
  eine strikt höhere Alarmstufe und einen eigenen Ton eskalieren. Ein leerer
  Eskalationston übernimmt bewusst den Grundton.
- Die Eskalation erzeugt eine neue Meldungsfolge und verlangt damit erneut eine
  Quittierung. Eine Tonpause der vorherigen Folge wird nicht übernommen.
- Jede Systemalarmstufe kann eine sichere Operatoraktion auslösen, wenn ein
  bereits aktiver Alarm in diese Stufe wechselt.
- Operatoraktionen öffnen ausschließlich das passende UNMA-Panel und scrollen
  zur Meldung. Optional beenden sie nur die vorübergehende
  Fünf-Minuten-Stummschaltung; Kamera, Maschinen, globale Audioeinstellung und
  schlitzbezogene Tonpausen bleiben unangetastet.
- Eine auf 64 Einträge begrenzte Laufzeit-Queue verwirft veraltete,
  quittierte oder ersetzte Anforderungen und priorisiert Alarmstufe, Aktion und
  jüngste Meldungsfolge deterministisch.
- Schema 19 migriert bestehende Welten mit deaktivierter Eskalation und ohne
  Operatoraktionen; die in 0.9.22 gespeicherten Timer und Latches bleiben
  vollständig erhalten.

## 0.9.22 – 2026-08-10

- Eigene Regeln und jede einzelne Systemalarmstufe besitzen jetzt eine
  spielzeitbasierte Auslöseverzögerung, Rücksetzverzögerung und optionale
  Mindestaktivzeit. `0` erhält das bisherige Sofortverhalten.
- Jede numerische Schwellenbedingung kann mit einer Hysterese versehen werden.
  Die sechs Vergleichsoperatoren verwenden dafür stabile Schmitt-Bänder, damit
  Messwerte an einer Grenze keine Alarmflut erzeugen.
- Laufende Timer und Bedingungslatches werden je Welt gespeichert. Laden,
  Spielzeitrücksprünge und reine Text-, Farb- oder Tonänderungen erzeugen keine
  künstlichen Alarmwechsel.
- `Z` pausiert den Ton eines sichtbaren Meldeschlitzes für einen Spielmonat;
  `R` hebt die Pause wieder auf. Quittierung, Sichtbarkeit, Zähler und Verlauf
  bleiben unverändert, auch bei zusammengefassten Objektmeldungen.
- Ungültige Zeit- oder Hystereseentwürfe blockieren das Speichern und
  strukturelle Systemeditor-Aktionen, statt Eingaben still zu normalisieren
  oder zu verwerfen.

## 0.9.21 – 2026-08-10

- Globale Panels lassen sich in ihren Einstellungen als unabhängige Vorlage
  duplizieren. Spalten, Filter, automatische Quellen, Ausschlüsse und feste
  Schlitzreihenfolge bleiben erhalten.
- Zugeordnete eigene Meldungen werden tief kopiert, erhalten kollisionsfreie
  Kennungen und starten bewusst deaktiviert. Verknüpfungen, aktive Zustände und
  Verlaufseinträge werden nicht übernommen.
- Dashboard und Objektpanels bleiben von der Funktion ausgeschlossen. Verwaiste
  Regelplätze aus beschädigten Alt-Konfigurationen werden sicher übersprungen
  und dem Nutzer gemeldet.
- Der gesamte Vorgang wird einmal atomar gespeichert; bei einem Schreibfehler
  werden Panel und Regeln vollständig zurückgerollt.

## 0.9.20 – 2026-08-10

- Der Verlauf besitzt jetzt eine Freitextsuche über Meldung, Detail, Quelle,
  Panel und Alarmkennung sowie kombinierbare Zustands- und Stufenfilter.
- Jede neue Verlaufszeile speichert Spielzeitmarken für Kommen, Gehen und
  Quittieren; die Oberfläche zeigt das jeweils jüngste Ereignis als
  Spieljahr, -monat und -tag.
- Die aktuell gefilterte Ansicht lässt sich als RFC-4180-CSV oder als JSON nach
  `%LOCALAPPDATA%\UNMA\exports` exportieren. Tickwerte und Unicode-Texte bleiben
  vollständig erhalten.
- Alte Spielstände ohne Zeitmarken bleiben lesbar; beschädigte oder ungültige
  Zeitwerte werden beim Laden sicher normalisiert.
- Eine vollständig getestete, spielzeitbasierte Timing-Policy bereitet
  Aktivierungs-/Rücksetzverzögerung, Mindestaktivzeit und Hysterese vor, ohne
  das bisherige Sofortverhalten bestehender Regeln zu verändern.

## 0.9.19 – 2026-08-10

- Meldungen lassen sich jetzt einzeln am Schlitz, panelweise oder weiterhin
  global quittieren. Aggregierte Objektmeldungen quittieren dabei zuverlässig
  alle zugrunde liegenden Ereignisse samt Verlauf.
- **NÄCHSTER ALARM** springt zyklisch zur nächsten unquittierten Meldung,
  scrollt sie ins Sichtfeld und öffnet nach Möglichkeit das betroffene Objekt.
- Das optionale Keybind Framework 2.0.2 bindet Fenster, Master-Quittierung,
  Alarmnavigation und fünfminütige Tonstummschaltung mit primärer und
  sekundärer Belegung ein; ohne Framework gelten sichere Standardtasten.
- Eine fünfminütige Stummschaltung pausiert nur die Audioausgabe, ohne
  Meldungen zu quittieren oder ihren Zustand zu verändern.
- Entity-Metadaten bleiben beim Zusammenfassen von Meldeschlitzen erhalten,
  sodass Navigation auch aus projizierten Panels das richtige Ziel findet.
- Reproduzierbare Paketskripte, vollständige Release-Prüfungen und ein
  GitHub-Coretest-Workflow verhindern versehentliches Deployment in die aktive
  Mod-Installation und prüfen Sprachkataloge automatisch.

## 0.9.18 – 2026-08-10

- Launcher, Hauptfenster, Meldungseditor, abgetrennte Panels, Formulare und
  Instrumente werden vollständig in der nativen COI-UI-Hierarchie gerendert.
- Die frühere IMGUI-Ebene und der transparente uGUI-Eingabeschutz wurden
  entfernt. UNMA- und Vanilla-Fenster verwenden damit dieselbe vom Spiel
  verwaltete Vordergrundreihenfolge.
- Textfokus, UI-Skalierung und dynamische Listen bleiben auch mit mehreren
  gleichzeitig geöffneten UNMA-Fenstern stabil.
- Die Optionsseite ist scrollbar, sodass alle Einstellungen auch in kleinen
  Fenstern und bei großer UI-Skalierung erreichbar bleiben.

## 0.9.17 – 2026-08-10

- Instrumentmeldungen wählen ihre Zielpanels jetzt ausdrücklich im
  Meldungseditor, statt unbemerkt das erste Globalpanel zu verwenden.
- Pro Instrumentmeldung können mehrere Zielpanels unabhängig gewählt werden;
  mindestens ein Ziel ist zwingend erforderlich.
- **INSTRUMENT** steht als dritter Quellenbutton neben Gebäudeauswahl und
  globalen Variablen bereit.
- Alte oder beschädigte Regeln ohne gültiges Zielpanel werden beim Laden auf
  eine vorhandene Meldetafel repariert.

## 0.9.16 – 2026-08-10

- **MELD.** öffnet berechnete Instrumentwerte nun als eigene Quelle
  **VERKNÜPFTE WERTE: Schildname**.
- Weitere Instrumentwerte desselben Messpanels lassen sich direkt auswählen
  und als zusätzliche Bedingungen in die Meldung aufnehmen.
- Beim erneuten Bearbeiten einer instrumentgebundenen Meldung wird der
  verknüpfte Quellenkontext automatisch wiederhergestellt.

## 0.9.15 – 2026-08-10

- Schreiberarchive verwenden echte COI-Spielzeit mit Bereichen von einem
  Spieltag bis zu einem Jahrhundert statt realer Minuten und Stunden.
- Zeitabhängige Meldungen unterstützen Rückgang und Zunahme jeweils als
  Betrag oder Prozent sowie das kontinuierliche Halten eines Vergleichs.
- Zeitfenster werden als Tage, Monate, Jahre, Jahrzehnte oder Jahrhunderte
  definiert; Pause und Spielgeschwindigkeit werden korrekt berücksichtigt.
- Alte Sekundenbedingungen werden beim Laden automatisch auf gleichwertige
  Spielzeiteinheiten migriert.

## 0.9.14 – 2026-08-10

- Im freien Meldungseditor stehen jetzt die globalen Lagerbestände aller
  freigeschalteten Produkte aus der COI-Ressourcenleiste zur Auswahl.
- Zusätzlich lassen sich globale Lagerkapazität und Füllstand jedes Produkts
  als Bedingung verwenden.
- Für jede sichtbare Wartungsart sind Füllstand, Reserve, Kapazität,
  Monatsänderung sowie aktueller und maximaler Monatsbedarf verfügbar.
- Produktnamen stammen lokalisiert aus COI; alle neuen UNMA-Bezeichnungen sind
  vollständig an MultiLangLib angebunden.

## 0.9.13 – 2026-08-09

- Sämtliche spielersichtbaren UNMA-Texte in Oberfläche, Laufzeitmeldungen,
  Messinstrumenten und Schreiberarchiv werden nun über MultiLangLib aufgelöst.
- Deutsche und englische Kataloge enthalten vollständige Übersetzungen; alle
  weiteren Sprachkataloge sind schlüsselgleich und verwenden für neue, noch
  nicht übersetzte Einträge einen englischen Fallback.
- Namen von Systemmesswerten, Einheiten und eingebauten Tönen werden dynamisch
  in der aktiven Sprache aufgelöst. Formatierte Texte verwenden die
  MultiLangLib-Formatierung.
- Automatische Regressionstests prüfen Sprachdateien, verwendete Schlüssel und
  Platzhalter aller 21 Kataloge auf Vollständigkeit.

## 0.9.12 – 2026-08-09

- Die UNMA-Fenster verwenden den nativen COI-Rahmen, native Schaltflächenoptik,
  zuverlässige Vordergrundreihenfolge und frei ziehbare Größen; Eingaben
  außerhalb des tatsächlich sichtbaren Rahmens bleiben beim Spiel.
- Papierschreiber besitzen eine maximierte Archivansicht mit mehreren
  Zeitfenstern und durchgehend gezeichneter Historie.
- Ein Instrument kann mehrere Gebäude derselben Messgröße als Summe,
  Mittelwert, Minimum oder Maximum zusammenfassen.
- Berechnete Instrumentwerte können eigene Meldungen auslösen. Neben normalen
  Grenzwerten stehen Abfälle um Betrag oder Prozent innerhalb eines
  Zeitfensters zur Verfügung.
- Instrumenttitel und Aktionsschaltflächen besitzen getrennte Kopfzeilen und
  überdecken sich auch bei langen Bezeichnungen nicht mehr.

## 0.9.11 – 2026-08-09

- Der Auswahlbereich für globale Variablen bleibt nun geöffnet, während seine
  angezeigten Ist-Werte im Hintergrund aktualisiert werden.

## 0.9.10 – 2026-08-09

- Eigene Meldungsregeln können jetzt globale Spielvariablen wie Bevölkerung,
  Arbeitsreserve, Nahrungsvorrat und Gesundheit als Bedingungen verwenden.
- Ein anderer ungespeicherter Entwurf wird im Meldungseditor dauerhaft durch
  ein großes Warnbanner hervorgehoben.
- Beim Schließen des Meldungseditors stehen Speichern, Minimieren und Verwerfen
  zur Auswahl; bestehende eigene Meldungen lassen sich direkt dort löschen.
- Ein fehlertoleranter JSON-Datei-Export stellt aktive Meldungen und den
  Panelzustand für optionale externe UNMA-Anzeigen bereit.

## 0.9.9 – 2026-08-09

- Verbliebene fest eingebaute deutsche Texte bei Lager-, Transport- und
  Fahrzeugmesswerten sowie deren Einheit werden nun korrekt lokalisiert.
- Vergleichseditor, Anzeigeoptionen, Schweregrade, fehlende Quellen und die
  Anzahl verknüpfter Bedingungen verwenden jetzt ebenfalls den aktiven
  Sprachkatalog.

## 0.9.8 – 2026-08-09

- Terrain-Collapse-Warnungen von unsichtbaren konstruktiven Unterobjekten wie
  Transportpfeilern werden jetzt dem zugehörigen sichtbaren Transport- oder
  Layoutgebäude zugeordnet und in dessen Objektpanel angezeigt.
- `EntityMayCollapseUnevenTerrain` wird für kollabierbare statische Objekte
  vorsorglich als mögliche Vanilla-Meldung angeboten. Objekt- und
  Objekttyp-Unterdrückung wirken auch auf angehängte Pfeiler.

## 0.9.7 – 2026-08-09

- Objektpanels lesen die vom Spiel am konkreten Objekt registrierten
  Vanilla-Dauermeldungen jetzt direkt aus. Dadurch sind alle für dieses Objekt
  definierten Meldungen bereits vor ihrem ersten Auftreten sichtbar und lassen
  sich vorsorglich pro Objekt oder Objekttyp unterdrücken.
- Die maximale Breite der Hauptregister wurde erhöht, damit
  `MELDUNGSOPTIONEN` auch bei breiten Fenstern vollständig dargestellt wird.

## 0.9.6 – 2026-08-09

- Der bisherige Tab `TÖNE` heißt jetzt `MELDUNGSOPTIONEN` und bündelt damit
  treffender die Behandlung, Sichtbarkeit und Audioeinstellungen vorhandener
  Spielmeldungen.
- Objektbezogene Tafeln zeigen bekannte Vanilla-Meldungen des jeweiligen
  Objekttyps dauerhaft als weiße, inaktive Vorschau an. Direkt im Meldeschlitz
  lassen sich die vier Behandlungsmodi für das einzelne Objekt oder für alle
  Objekte desselben Typs durchschalten.

## 0.9.5 – 2026-08-09

- Vanilla-Meldungen können pro Objekt oder Objektprototyp jetzt vollständig
  ignoriert werden. Dieser vierte Modus erzeugt weder Alarmzustände noch neue
  Verlaufseinträge und bereinigt beim Aktivieren alle noch sicher zuordenbaren
  aktiven und jüngeren Ereignisse aus dem UNMA-Verlauf.

## 0.9.4 – 2026-08-09

- Eine Mod-Hub-kompatible `changelog.txt` liegt jetzt direkt neben dem
  Manifest und wird in jedem Uploadpaket mitgeliefert.

## 0.9.3 – 2026-08-09

- Bestehende Vanilla-Meldungen lassen sich im TÖNE-Tab jetzt wahlweise nur für
  das betroffene Objekt oder für alle Objekte desselben Prototyps behandeln.
  Drei Zustände stehen bereit: normal, nur im Verlauf protokollieren und den
  UNMA-Ton abschalten, oder zusätzlich aus HOME und den aktiven Zählern
  ausblenden. Eine spezifische Objektregel übersteuert die Prototypregel.
- Das Konfigurationsschema 13 speichert diese Regeln sowie Objekt-ID,
  Objektprototyp und Titel an aktiven Vanilla-Zuständen. Frühere globale
  Abschaltungen werden verlustfrei als ausgeblendete Meldungstyp-Regeln
  migriert.

## 0.9.2 – 2026-08-09

- Der Abriss-Listener verwendet jetzt ausdrücklich die nicht speicherbare
  COI-Eventregistrierung. Autosaves versuchen dadurch nicht mehr,
  `UnmaRuntime` als Callback-Besitzer zu serialisieren und brechen nicht mehr
  wegen einer fehlenden `Serialize`-Methode ab.
- Die MultiLangLib-Kataloge von UNMA und des vollständigen Providerbeispiels decken
  jetzt alle 21 mit der aktuellen COI-Version ausgelieferten Locale-Dateien ab.
  Eine neue deutschsprachige Integrationsanleitung erklärt JSON- und C#-API,
  mehrsprachige Providermeldungen, Fallbacks und die Release-Prüfung.

## 0.9.0 – 2026-08-08

- Der Meldungseditor ist jetzt ein eigenes verschiebbares und skalierbares
  Fenster. Das Hauptfenster bleibt auf Meldetafel, Verlauf, System, Töne und
  Optionen konzentriert; `+ PANEL`, Panel-Einstellungen und
  `+ NEUE MELDUNG` öffnen jeweils den passenden getrennten Arbeitsbereich.
- Jeder über die UNMA-Glocke geöffnete Gebäude-/Entity-Inspector erhält eine
  eigene dauerhafte Tafel. Dieselbe Entity-ID öffnet stets dieselbe Tafel.
- Gebäuderegeln lassen sich mit beliebig vielen globalen Fachpanels
  verknüpfen. Alle Schlitze verwenden dieselbe Regel-ID und damit exakt
  denselben K/G/Q-, Quittier-, Ton- und Verlaufszustand.
- Ein kleiner Pfeil unten rechts in objektbezogenen Meldeschlitzen zentriert
  die Kamera auf das zugehörige Objekt und öffnet dessen Inspector.
- `AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN` ist gegen Durchklicken abgesichert.
  Damit können nacheinander beispielsweise `Stückgutlager · Menge = 0` und
  ein Farm-Messwert als gemeinsame UND-/ODER-Regel hinzugefügt werden.
- Klick, Drag, Rechtsklick und Mausrad über Hauptfenster, Meldungseditor,
  Launcher und abgekoppelten Tafeln werden gegenüber Welt-Auswahl und Kamera
  blockiert.
- Neue persistente UI-Skalierung von 75 bis 200 Prozent; Haupt- und
  Editorfenster bleiben in logischen Koordinaten gespeichert und damit auf
  1080p- sowie 4K-Monitoren verwendbar.
- Beim endgültigen Abriss werden jetzt auch die eigene Entity-Tafel, ihre
  Primärregeln und sämtliche global verknüpften Schlitze atomar entfernt.
  Der Load-Fallback speichert zusätzlich den Entity-Typ, um verwaiste
  Gebäudetafeln sicher von temporär despawnten Fahrzeugen zu unterscheiden.
- Konfigurationsschema 12 migriert bestehende Tafeln und Regeln ohne
  versehentliche Entity-Zuordnung; UI-Skalierung startet bei Altständen mit
  100 Prozent.

## 0.8.0 – 2026-08-07

- Bekannte Vanilla-Meldungstypen lassen sich im TÖNE-Tab global für UNMA
  ein- und ausblenden. Ein Schalter für `NoRecipeSelected` gilt damit sofort
  für alle Gebäudeinstanzen, beendet offene UNMA-Ereignisse sauber als KGQ
  und unterdrückt neue Schlitze sowie Alarmtöne, ohne die Spielmeldung selbst
  zu verändern.
- Globale Abschaltungen bleiben über Neustarts erhalten. Feste Panelplätze
  werden dabei nur ausgeblendet und kehren beim Wiedereinschalten in ihrer
  bisherigen Reihenfolge zurück; bereits anstehende Spielmeldungen werden
  gesammelt neu eingelesen.
- Wird eine überwachte Entity tatsächlich zerstört oder ein Gebäude
  abgerissen, entfernt UNMA automatisch alle davon abhängigen eigenen Regeln,
  festen Meldeschlitze und aktiven Zustände. Sammelmeldungen werden dabei als
  unteilbare UND-/ODER-Regel behandelt; ihr Verlauf bleibt als KG/KGQ erhalten.
- Temporäre Entity-Entfernungen wie Fahrzeug-/Zug-Despawn oder ein lebender
  Ersatz unter derselben ID lösen ausdrücklich keine automatische Löschung aus.
- Der Größenänderungsgriff der Haupttafel besitzt jetzt eine klar sichtbare,
  deckungsgleiche Trefferfläche und exklusiven Mauszeiger-Fokus. Ziehen im
  Fensterinhalt kann dadurch keine hängen gebliebene Skalierung mehr fortsetzen.
- Eigene Meldeschlitze öffnen ihre Regel nun per Doppelklick direkt im Editor;
  bestehende ungespeicherte Entwürfe bleiben geschützt.
- Mehrprodukt-Eingabepuffer werden pro Produkt aggregiert. Damit lassen sich in
  Lebensmittelmärkten und ähnlichen Gebäuden Bedingungen wie
  `Kartoffeln < 400` oder `Kartoffeln < 50 % Kapazität` auswählen.
- Das Home-/Dashboard-Panel ist jetzt eine reine aktive Übersicht: Es zeigt nur
  anstehende quittierte und unquittierte Meldungen (`K`/`KQ`) und blendet
  Normalzustände, gegangene Meldungen sowie leere Rasterplätze aus.
- Alle Fachpanels bleiben reale, dauerhaft definierte Schlitzmeldetafeln. Das
  Dashboard kann weder gelöscht noch mit festen Schlitzen oder Auto-Filtern
  belegt werden; Konfigurationsschema 10 speichert diese Panelrolle eindeutig.
- Versionierte Fremdmod-API V1 für prototypgebundene Alarmvorlagen,
  fehlertolerante Messwert-Reader und direkt veröffentlichte Alarmzustände.
- Deklarative Definitionen aktiver Mods werden deterministisch aus deren
  `UNMA/*.json`-Ordnern geladen; Größen-, Mengen-, Schema-, Eigentümer- und
  Übersetzungsschlüsselprüfungen isolieren fehlerhafte Providerdateien.
- Automatische Provideralarme unterstützen feste Sammelschlitze sowie bewusst
  aktivierte instanzbezogene Schlitze. Stabile IDs erhalten K/G/Q und eine
  Quittierung auch dann, wenn sich der angezeigte Messwert ändert.
- MultiLangLib 0.1.0 ist jetzt eine deklarierte Abhängigkeit. Fensterhülle und
  Providertexte werden über kanonische Schlüssel mit sicheren Fallbacks
  aufgelöst.
- Der Optionen-Tab zeigt Provider-, Definitions- und Fehlerzahlen und kann
  JSON- sowie Sprachdateien ohne Neustart erneut einlesen.
- Entwicklerdokumentation, JSON-Schema und ein vollständiges Providerbeispiel
  ergänzt.

## 0.7.0 – 2026-08-07

- Die UNMA-Glocke im Gebäude-/Entity-Inspector fügt das angeklickte Objekt
  jetzt zuerst zur Meldetafel hinzu, statt sofort einen losgelösten Entwurf zu
  öffnen.
- Im sichtbaren Zuweisungsmodus kann anschließend eine vorhandene eigene
  Meldung angeklickt werden. Ihre bisherigen Bedingungen bleiben erhalten und
  das neue Objekt kann als weitere UND-/ODER-Bedingung verknüpft werden.
- Ein hervorgehobenes `+ NEUE MELDUNG`-Karree startet eine neue Meldung genau
  im gewählten Panel und belegt dort nach dem Speichern einen festen Schlitz.
  Das Ziel erscheint auch auf leeren Panels und hinter einer vollständig
  belegten Reihe.
- Vanilla- und Systemschlitze werden im Zuweisungsmodus klar als reine Anzeigen
  gekennzeichnet, da ihre Werkbedingungen nicht aus Entity-Regeln bestehen.
- Ungespeicherte Entwürfe werden nicht mehr still durch eine neue
  Schlitzzuweisung überschrieben. Abbruch und Inspektionsfehler verändern den
  vorhandenen Entwurf nicht; ein geschlossenes Objektfenster kann später mit
  derselben gewählten Schlitzposition fortgesetzt werden.
- Stabile `rule:<id>`-Slot-IDs lösen bestehende Verknüpfungsziele unabhängig
  vom wechselnden Laufzeitereignis auf; doppelte Regel-IDs werden vor jeder
  Konfigurationsänderung abgewiesen.

## 0.6.1 – 2026-08-07

- `HomelessLeft` bleibt jetzt über Monatswechsel hinweg genau eine stehende
  Meldung. Eine geänderte Personenzahl aktualisiert nur den Meldetext und
  erzeugt weder einen neuen Verlaufseintrag noch einen neuen Alarmton.
- Eine manuell quittierte Meldung bleibt `KQ`, solange der Bevölkerungswert
  `+/-` negativ ist. Das Entfernen des kurzlebigen Vanilla-Hinweises beendet
  die Meldung nicht mehr.
- Erst `LastPopulationDiff >= 0` setzt den Zustand auf gegangen. Ein späterer
  echter Rückfall kann danach wieder ein neues, unquittiertes `K` auslösen.
- Schema 9 führt alte aktive Monatszustände zusammen. Beim nächsten passenden
  `HomelessLeft`-Ereignis übernimmt es einmalig die letzte Quittierung, ohne
  einen zusätzlichen Verlaufseintrag zu erzeugen; ein zuvor beobachteter
  Normalwert verwirft diese Übernahme sicher.

## 0.6.0 – 2026-08-07

- Panelplätze sind jetzt echte persistente Meldeschlitze mit dauerhaft fester
  Reihenfolge. Normal-, Kommend-, Stehend- und Gegangen-Zustände sortieren die
  Tafel nicht mehr um.
- Wiederholte Vanilla-Ereignisse derselben Meldungsart und Entität werden über
  eine stabile Schlitz-ID zusammengeführt. Dadurch erzeugt beispielsweise ein
  erneut kommendes `NotEnoughWorkers` keinen zweiten alten NORMAL-Schlitz.
- Ereignisschlüssel, manuelle Quittierung, Audioauswahl und der vollständige
  K/G/Q-Verlauf bleiben bewusst getrennt erhalten.
- Der Panel-Editor kann feste Schlitze hoch/runter verschieben, entfernen und
  aus bekannten Vanilla- und Systemmeldungen ergänzen. Automatisch entdeckte,
  zum Filter passende Arten werden genau einmal am Ende angefügt.
- System- und eigene Regeln erhalten schon im Normalzustand beschriftete,
  hellgraue Plätze. Eigene Regeln nehmen ihren festen Schlitz beim Wechsel der
  Zieltafel mit und entfernen ihn beim Löschen der Regel.
- Konfigurationsschema 8 migriert bestehende Panels, Regeln und gespeicherte
  Alarmzustände ohne Verlust ihrer Quittier- oder Verlaufshistorie.

## 0.5.0 – 2026-08-07

- Goldene UNMA-Alarmglocke direkt in den Inspectoren von Gebäuden, Lagern,
  Fahrzeugen, Transporten, Straßen und Schienen ergänzt. Sie bindet immer das
  tatsächlich angeklickte beziehungsweise angeheftete Objekt.
- Neues separates Objekt-Alarmfenster mit verständlicher AWL-Tabelle:
  `Ist-Wert | Kennung | Steuerzeichen | Soll-Wert | Bedingung`.
- Alle sechs Vergleiche (`<`, `<=`, `=`, `!=`, `>=`, `>`) sind gleichzeitig
  sichtbar auswählbar; Messwerte werden über eine durchsuchbare Liste statt
  über Pfeile gewählt.
- Neuer Modus `% VON`: Ein Ist-Wert kann sicher als Prozent eines frei
  gewählten Bezugs-Messwerts derselben Entität ausgewertet werden. Für Lager,
  Förderer/Rohre und Fahrzeugfracht wird die jeweilige Kapazität vorgeschlagen.
- Klare Ziel-Meldetafel pro Meldung sowie direkte Anlage einer neuen Tafel aus
  dem Alarmfenster. Der Panel-Editor trennt Auswahl, Bearbeitung und Neuanlage.
- Panel-Löschen verlangt eine zweite Bestätigung und zeigt vorher die Zahl der
  ebenfalls betroffenen eigenen Meldungen.
- Konfigurationsschema 7 migriert bestehende Regeln unverändert als absolute
  Bedingungen und speichert Prozentmodus sowie Bezugs-Messwert dauerhaft.

## 0.4.0 – 2026-08-07

- Neuer persistenter **VERLAUF** mit einer eigenen Zeile pro Alarmereignis und
  den Zuständen `K` (gekommen), `KQ` (gekommen und quittiert), `KG` (gekommen
  und gegangen) sowie `KGQ` (gekommen, gegangen und quittiert).
- Unquittierte `K`-Einträge blinken rot; `KG` blinkt mit schwarzer Schrift auf
  weißem Hintergrund. Quittierte `KQ`- und `KGQ`-Einträge stehen mit schwarzer
  Schrift auf weißem Hintergrund.
- Nur vollständig abgeschlossene `KGQ`-Einträge können ausdrücklich gelöscht
  werden; bis dahin bleiben sie auch nach Speichern und Neuladen erhalten.

## 0.3.0 – 2026-08-07

- Kraftwerksnahe Meldungsfolge ergänzt: Eine ungequittierte Meldung bleibt auch
  nach Wegfall der Ursache als `GEGANGEN · UNQUITTIERT` blinkend und tönend
  gespeichert, bis `MASTER QUIT` gedrückt wird.
- Automatische Quittierung beim Gehen ist pro eigener Regel, vordefinierter
  Systemmeldung und Vanilla-Meldung frei wählbar; der Standard bleibt manuell.
- Quittierte, noch anstehende Meldungen wechseln beim späteren Gehen normal in
  den Ruhezustand, da das Alarmereignis bereits manuell quittiert wurde.
- Die bisherige dünne 8-Sekunden-Sirene wurde durch eine deterministisch
  erzeugte E57-nahe Motorsirene mit 4-Sekunden-Zyklus, 2 Sekunden Hochlauf,
  2 Sekunden Auslauf, 420-Hz-Spitze und kräftigeren Rotor-/Tieftonanteilen
  ersetzt.
- Das kritische Industriehorn wurde zu einem tiefen 3,2-Sekunden-Hornstoß mit
  einer klaren Pause von 1,2 Sekunden umgebaut.
- Quittier- und GEGANGEN-Zustände werden spielstandsbezogen gespeichert und
  nach einem Neustart korrekt wiederhergestellt.
- Höhere Systemstufen gleicher Alarmkategorie lösen erneut KOMMT und Ton aus;
  lautlose Meldungen blockieren keine anderen hörbaren Alarme mehr.
- Konfigurationsschema 5 speichert das Quittierverhalten migrationssicher.

## 0.2.0 – 2026-08-06

- Gesundheitswert korrigiert: `10` ist neutral, ausgewertet wird der stabile
  Wert des letzten abgeschlossenen Monats.
- Krankheit und Pollution/Müll werden über die echten Gesundheitskategorien
  des Spiels getrennt erfasst.
- Prozentuale Arbeitsreserve und erwarteter Netto-Bevölkerungsverlust fließen
  in die Priorität ein. Die Prognose übernimmt dabei die spielgenaue Rundung
  sowie positive Geburten-/Ediktbeiträge; Hunger wird separat bewertet.
- NOTFALL bei Systemvorgaben nur noch für aktive Hunger- oder erkannte
  Gesundheits-/Arbeiter- beziehungsweise strukturelle Todesspiralen.
- Neuer SYSTEM-Editor für Aktivierung, Text, Bedingungen, Schwellen, Stufe,
  Farbe und Ton aller vordefinierten Meldungen.
- Eigene Meldungen und Sammelmeldungen sind nachträglich vollständig
  bearbeitbar.
- Das UNMA-Fenster behält beim Anwählen und Verschieben seinen dunklen,
  nahezu opaken Hintergrund statt auf den transparenten Fokusstil zu wechseln.
- Schema 4 ergänzt fehlende Vorgaben migrationssicher, ohne Nutzerwerte zu
  überschreiben.
- Die alten 65/45/25-Gesundheitsschwellen werden bewusst durch korrekte
  Gesundheitspunkte-Vorgaben ersetzt, da ihre frühere Prozentinterpretation
  fachlich falsch war.

## 0.1.0 – 2026-08-06

- Erste funktionsfähige Schlitzmelder-Tafel.
- Vanilla-Bridge und automatische Gesundheit-/Nahrung-/Arbeiter-Alarme.
- MASTER-QUIT-Zustandsmaschine mit Blink- und Dauertonlogik.
- Frei definierbare Panels, Farben, Töne sowie UND-/ODER-Sammelmeldungen.
- Entitätsauswahl über den aktiven Inspector und generischer Messwertkatalog.
- Synthetische Klingel, Industriehorn, E57-artige Sirene und Oszillatortöne.
- Import eigener PCM-WAV- und Ogg-Vorbis-Dateien.
- Mehrere abkoppelbare In-Game-Panels.
- Individuelle Ton-Overrides für Vanilla- und Systemmeldungen.
- Spielstandsbezogene Zustandsdateien mit Typ-/Prototypvalidierung.
- Typisierte Produktadapter für Förderbänder, Rohre und Fahrzeugfracht.
- Simulationssichere Auswertung über `UpdateEndForUi` und virtualisierte
  Darstellung großer Meldetafeln.
