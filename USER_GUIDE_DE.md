# UNMA Benutzeranleitung

Diese Anleitung gilt für **UNMA 0.9.22** und
**Captain of Industry 0.8.6c**.

UNMA – die Universelle Nachrichten-Meldeanlage – ergänzt Captain of Industry
um eine frei konfigurierbare industrielle Schlitzmelder-Tafel. Sie spiegelt
Spielmeldungen, führt einen dauerhaften Verlauf, überwacht wichtige globale
Spielwerte und ermöglicht eigene Meldungsregeln für Spielobjekte oder globale
Variablen.

Benötigte Abhängigkeit: **MultiLangLib 0.1.0 oder neuer**.
Optionale Abhängigkeit: **Keybind Framework 2.0.2 oder neuer** für frei
konfigurierbare primäre und sekundäre Tastenkürzel. Ohne das Framework bleibt
UNMA vollständig bedienbar.

## Installation und Aktualisierung

1. Die aktuelle UNMA-Version herunterladen.
2. Das Archiv in den `Mods`-Ordner von Captain of Industry entpacken. Danach
   müssen `Mods/UNMA/manifest.json` und `Mods/UNMA/UNMA.dll` vorhanden sein.
3. MultiLangLib 0.1.0 oder neuer installieren und aktivieren.
4. UNMA im Mod-Menü des Spiels aktivieren.
5. Einen Spielstand laden oder ein neues Spiel beginnen.

Vor einer Aktualisierung das Spiel schließen und den vorhandenen Ordner
`Mods/UNMA` sichern. Weltbezogene Daten liegen dort als
`unma-world-<GameId>.json` samt Sicherungsdateien; eigene Töne können unter
`Sounds/` gespeichert sein. Den neuen `UNMA`-Ordner über den vorhandenen
entpacken und die Release-Dateien ersetzen lassen. Das Release-Archiv enthält
weder Weltdateien noch selbst hinzugefügte Töne. Den alten Ordner daher nur
dann vollständig löschen, wenn diese Dateien gesichert wurden und anschließend
zurückkopiert werden.

UNMA kann einem bestehenden Spielstand hinzugefügt und wieder daraus entfernt
werden.

## Schnellstart

1. Mit **F8** das UNMA-Hauptfenster öffnen oder schließen.
2. Alternativ den frei schwebenden Launcher verwenden, der zunächst nahe am
   linken Bildschirmrand liegt.
3. Unter **MELDETAFEL** befinden sich HOME und die dauerhaft angelegten Panels.
4. Mit **Q** nur einen Schlitz, mit **PANEL QUITTIEREN** die angezeigte Tafel
   oder mit **ALLES QUITTIEREN** sämtliche neuen und gegangenen Meldungen
   quittieren.
5. **NÄCHSTER ALARM** oder **Umschalt links + F8** durchläuft die
   unquittierten Meldungen des Panels und fokussiert, soweit möglich, das
   betroffene Spielobjekt.
6. Unter **VERLAUF** lassen sich frühere Meldungsereignisse einsehen.

### Konfigurierbare Tastenkürzel

Ist das optionale **Keybind Framework 2.0.2+** aktiv, bietet dessen
Einstellungsseite primäre und sekundäre Belegungen für das UNMA-Fenster, die
globale Quittierung, den nächsten unquittierten Alarm und eine fünfminütige
Stummschaltung des Alarmtons. Eingebaute Rückfalltasten sind **F8** für das
Hauptfenster und **Umschalt links + F8** für den nächsten Alarm; die beiden
potenziell störenden Aktionen sind zunächst unbelegt. Die Stummschaltung
quittiert oder löscht niemals eine Meldung.

### Launcher und native Fenster

Der Launcher erscheint nur während einer aktiven Spielwelt und bei
geschlossenem Hauptfenster. Der Zusatz `+N` zeigt die Zahl noch nicht
quittierter Meldungen. Am schmalen Pfeilgriff lässt er sich frei in alle
Richtungen verschieben. UNMA hält ihn innerhalb des sichtbaren Bildschirms und
speichert seine Position je Spielwelt.

Launcher, Hauptfenster, Meldungseditor, abgekoppelte Tafeln, Formulare und
Instrumente liegen vollständig in Captain of Industrys nativer
Game-UI-Hierarchie. Ein Klick auf ein UNMA-Fenster holt Rahmen und Inhalt
gemeinsam nach vorn. Ein danach aktiviertes Spielfenster darf UNMA normal
überdecken; es existiert keine getrennte Web-, IMGUI- oder uGUI-Overlayebene.

Hauptfenster, Editor und abgekoppelte Tafeln lassen sich wie andere
Spielfenster verschieben, anheften und am Griff unten rechts vergrößern oder
verkleinern. **MINIMIEREN**, das native Schließen-Symbol und **F8** schließen
nur das Hauptfenster und bringen den Launcher zurück; ein offener Editor oder
eine abgekoppelte Tafel bleibt davon unabhängig. Fenstergrößen werden auf den
sichtbaren Bereich begrenzt.

Klicks, Ziehen und Mausradbewegungen innerhalb eines sichtbaren UNMA-Fensters
bleiben in diesem Fenster und erreichen nicht die Spielwelt dahinter. Solange
ein UNMA-Textfeld aktiv ist, erhält es die Tastatureingaben. Ein Fokuswechsel
weg von allen UNMA-Textfeldern oder das Schließen des fokussierten Fensters gibt
die Spieltastatur wieder frei.

### Tabs des Hauptfensters

| Tab | Zweck |
| --- | --- |
| **MELDETAFEL** | HOME, globale und objektbezogene Panels, Quittierung und neue Meldeschlitze |
| **MESSPULT** | Live-Instrumente, berechnete Werte, Papierschreiber und Instrumentmeldungen |
| **VERLAUF** | Aktuelle und abgeschlossene Meldungsereignisse |
| **SYSTEM** | Eingebaute Überwachung für Gesundheit, Nahrung und Arbeiter |
| **MELDUNGSOPTIONEN** | Ton, Sichtbarkeit, Protokollierung und Vanilla-Verhalten je Meldungsart |
| **OPTIONEN** | Inhaltsskalierung, Alarmfarben, Ton-Neueinlesen und Integrationsdiagnose |

## Meldungszustände

UNMA arbeitet wie eine klassische industrielle Meldeanlage.

| Kürzel | Zustand | Anzeigeverhalten |
| --- | --- | --- |
| `K` | Meldung ist gekommen und nicht quittiert | Blinkt in Aktivfarbe und wiederholt den Ton |
| `KQ` | Meldung ist gekommen und quittiert | Bleibt aktiv stehen, ohne den Ton zu wiederholen |
| `KG` | Ursache ist gegangen, Meldung aber nicht quittiert | Blinkt mit schwarzer Schrift auf weißem Hintergrund |
| `KGQ` | Ursache ist gegangen und Meldung quittiert | Abgeschlossenes Verlaufsereignis |

Das Quittieren einer aktiven Meldung beseitigt ihre Aktivfarbe nicht. Die Farbe
bleibt sichtbar, bis die überwachte Ursache wieder im Normalbereich liegt.

Abgeschlossene `KGQ`-Einträge bleiben im Verlauf gespeichert, bis sie dort
ausdrücklich gelöscht werden. Nur abgeschlossene Einträge können gelöscht
werden. Das Löschen aller abgeschlossenen Ereignisse muss innerhalb von fünf
Sekunden durch einen zweiten Druck bestätigt werden.

Die Werkzeugzeile unter **VERLAUF** durchsucht Meldung, Detail, Quelle,
Panel-ID und Alarmkennung gleichzeitig. Die Zustands- und Stufentasten
durchlaufen ihre Filter; beide Filter werden mit der Suche kombiniert. Neue
Ereignisse zeigen das Spieldatum ihres jüngsten Kommen-, Gehen- oder
Quittierübergangs. Einträge älterer UNMA-Versionen bleiben mit unbekanntem
Datum sichtbar.

**CSV** und **JSON** exportieren genau die aktuell gefilterten Zeilen nach
`%LOCALAPPDATA%\UNMA\exports`. CSV verwendet RFC-4180-Zeichenregeln; JSON
behält die rohen Spielzeit-Ticks für weitere Auswertungen. Ein Export verändert
oder löscht keinen Verlaufseintrag.

## Panels und Meldeschlitze

### HOME

HOME ist die Live-Übersicht aller aktuell anstehenden Meldungen. Angezeigt
werden `K` und `KQ` aus allen Quellen. HOME besitzt keine festen Schlitze;
inaktive, gegangene und leere Plätze werden ausgeblendet.

### Globale Panels

Globale Panels sind dauerhaft angelegte Meldetafeln für Spiel-, System-,
Provider- und eigene Meldungen.

- Mit **+ PANEL** neben der globalen Panelleiste unter **MELDETAFEL** ein neues
  Panel anlegen.
- Über das benachbarte Zahnrad Name, Spaltenzahl, Filter, automatische Quellen
  und Schlitzreihenfolge ändern.
- **PANEL DUPLIZIEREN** erstellt dort eine unabhängige Kopie einschließlich
  Reihenfolge, Filter und eigener Meldungen. Die kopierten eigenen Meldungen
  erhalten neue Kennungen und starten aus Sicherheitsgründen deaktiviert;
  bestehende Alarmzustände und der Verlauf werden nicht kopiert.
- Bekannte Meldungen gezielt in freie Schlitze aufnehmen und Plätze nach oben
  oder unten verschieben.
- Neu entdeckte Meldungen, die zu einer automatischen Quelle oder einem Filter
  passen, werden angehängt, ohne vorhandene Plätze zu verschieben.
- Panels können als eigene verschiebbare In-Game-Tafeln abgekoppelt werden;
  Einzelheiten stehen im folgenden Abschnitt.

### Objektpanels

Unterstützte Gebäude, Lager, Fahrzeuge, Förderbänder und Rohre besitzen ein
eigenes dauerhaftes Panel.

1. Das Objekt im Spiel auswählen.
2. Im Inspector die goldene **UNMA-Alarmglocke** drücken.
3. UNMA öffnet das Panel, das genau zu diesem Objekt gehört.

Der kleine Pfeil in einem objektbezogenen Meldeschlitz zentriert die Kamera auf
das Objekt und öffnet seinen Inspector.

Die Taste **Q** quittiert nur den sichtbaren Schlitz. **PANEL QUITTIEREN**
quittiert alle auf der aktuellen Tafel dargestellten Zustände, einschließlich
aller in einem Objektschlitz zusammengefassten Ereignisse. **ALLES
QUITTIEREN** bleibt die ausdrückliche globale Aktion.

Die Taste **Z** pausiert nur den Alarmton dieses Schlitzes für einen
Spielmonat. Das Kennzeichen wechselt zu **AUDIO Z · 1 MONAT**; **R** setzt den
Ton sofort fort. Bei einem Sammelschlitz gilt die Pause für alle aktuell
dahinterliegenden Ereignisse. Sie quittiert, versteckt oder löscht keine
Meldung und verändert
weder Zähler noch Verlauf. Ein späteres neues Ereignis erhält eine neue
Sequenz und ist wieder hörbar.

Ein Doppelklick auf einen eigenen Meldeschlitz öffnet die zugehörige Regel
direkt im Editor.

### Abgekoppelte Panels

Das gerade angezeigte HOME-, globale oder Objektpanel kann in ein eigenes
natives Fenster abgekoppelt werden. Dieses Fenster zeigt denselben Panel- und
Meldungszustand; es erzeugt keine zweite Meldung. Dasselbe Panel darf mehrfach
geöffnet werden. Abgekoppelte Tafeln stellen höchstens fünf Spalten dar.

Das Schließen einer abgekoppelten Tafel entfernt nur diese Ansicht. Panel,
Schlitze und Meldungen bleiben erhalten. Position und Größe werden nicht
gespeichert; eine neu abgekoppelte Ansicht beginnt an einer neuen
Vorgabeposition. Das zugrunde liegende Panel wird weiterhin je Spielwelt
gespeichert.

## Eigene Meldung erstellen

Eine Regel kann in einem globalen Panel oder in einem Objektpanel begonnen
werden.

1. Das gewünschte Zielpanel öffnen.
2. **+ NEUE MELDUNG** oder einen freien Plus-Schlitz drücken.
3. Die Quelle für die erste Bedingung wählen.
4. Einen Messwert auswählen.
5. Berechnung, Vergleichsoperator und Soll-Wert festlegen.
6. **+ ZEILE HINZUFÜGEN** drücken.
7. Bei Bedarf weitere Zeilen ergänzen.
8. Meldetext, Alarmstufe, Aktivfarbe, Ton und Quittierverhalten einstellen.
9. Die Meldung speichern.

Jede Bedingungszeile zeigt ihren aktuellen Ist-Wert, solange ihre Quelle
verfügbar ist.

### Spielobjekt als Quelle

Ein Gebäude, Lager, Fahrzeug, Förderband oder Rohr im Spiel auswählen und im
Editor **AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN** drücken.

UNMA erkennt unterstützte numerische und boolesche Werte, darunter:

- Lagerinhalt, Kapazität und Füllstand;
- produktbezogene Lagermengen;
- Inhalte von Förderbändern und Rohren;
- Fahrzeug- oder Transportfracht und deren Kapazität;
- öffentliche numerische oder boolesche Werte kompatibler Entities;
- zusätzliche Messwerte aktiver Provider-Mods.

Produktbezogene Werte werden aus Produkten ermittelt, die sich aktuell oder
früher im Objekt befanden. Ein vollständig unbenutztes und leeres Objekt bietet
anfangs möglicherweise nur allgemeine Mengen- und Füllstandswerte an.

### Globale Variablen als Quelle

Statt eines Spielobjekts **GLOBALE VARIABLEN** wählen. Globale Bedingungen
hängen nicht von einer einzelnen Entity ab und bleiben auch dann gültig, wenn
Gebäude abgerissen werden.

Derzeit stehen unter anderem folgende Gruppen zur Verfügung:

- Gesamtbevölkerung und monatliche Bevölkerungsänderung;
- freie oder fehlende Arbeiter und prozentuale Arbeitsreserve;
- Gesundheit, Krankheit, Pollution, erwartete Verluste und Krankheitsdauer;
- Nahrungsvorrat, Hunger und kürzlich Verhungerte;
- Arbeiterpuffer- und Todesspiralenwerte der eingebauten Überwachung.
- globaler Lagerbestand, Lagerkapazität und Lagerfüllstand jedes
  freigeschalteten lagerbaren Produkts;
- Wartungsfüllstand, Reserve, Kapazität, Monatsänderung sowie aktueller und
  maximaler Monatsbedarf jeder sichtbaren Wartungsart.

Der Auswahlbereich zeigt Live-Werte und bleibt während ihrer Aktualisierung
geöffnet.

Beispiel:

```text
Arbeitsreserve < 5
```

Beispielsweise im Messwert-Picker nach `Kohle` suchen und
`Kohle · globaler Lagerbestand` wählen, um eine Mangelschwelle anzulegen.

### Vergleiche

UNMA unterstützt alle sechs Vergleichsoperatoren:

```text
<   <=   =   !=   >=   >
```

Mit **ABSOLUT** wird der Messwert direkt mit dem Soll-Wert verglichen.

Mit **% VON** wird ein Wert relativ zu einem zweiten Messwert derselben Quelle
ausgewertet:

```text
Lagerinhalt % VON Lagerkapazität < 5
```

UNMA berechnet `Ist-Wert / Bezugswert × 100`. Ein fehlender, null oder negativer
Bezugswert gilt als nicht verfügbar und aktiviert die Bedingung nicht. Werte
über 100 Prozent werden nicht künstlich begrenzt.

### Alarmzeiten und Hysterese

Jede eigene Regel besitzt drei Dauern nach dem
Captain-of-Industry-Spielkalender:

- **AUSLÖSEVERZÖGERUNG** verlangt, dass die kombinierte UND-/ODER-Bedingung
  für die gewählte Zeit erfüllt bleibt;
- **RÜCKSETZVERZÖGERUNG** verlangt, dass sie vor dem Gehen entsprechend lange
  nicht erfüllt bleibt;
- **MINDESTE AKTIVZEIT** hält eine ausgelöste Meldung mindestens für die
  gewählte Dauer aktiv.

Mit `0` gilt jeweils das bisherige Sofortverhalten. Jede normale numerische
Bedingung besitzt außerdem eine **HYSTERESE**. Dieses Totband um den Grenzwert
verhindert wiederholtes Kommen und Gehen bei schwankenden Messwerten. Laufende
Timer und Hystereselatches werden je Welt gespeichert und nach dem Laden
fortgesetzt. Trendbedingungen für Zunahme oder Abnahme verwenden bewusst keine
Hysterese.

### Regeln mit mehreren Bedingungen oder Objekten

Nach dem Hinzufügen der ersten Zeile kann außerhalb des Editors ein anderes
Objekt gewählt und erneut **AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN** gedrückt
werden. Vorhandene Zeilen bleiben im Entwurf erhalten.

- **UND** wählen, wenn jede Zeile erfüllt sein muss.
- **ODER** wählen, wenn eine beliebige Zeile genügen soll.

Eine Regel kann so Werte mehrerer Objekte kombinieren. Wird ein verwendetes
Objekt endgültig abgerissen oder zerstört, entfernt UNMA die gesamte Regel,
damit sich ihre Logik nicht unbemerkt verändert. Temporär despawnte Fahrzeuge
führen nicht zum Löschen.

### Objektmeldung mit globalen Panels verknüpfen

Eine in einem Objektpanel angelegte Meldung kann zusätzlich auf einem oder
mehreren globalen Panels erscheinen. Die gewünschten Panels werden im Editor
markiert. Dadurch entstehen zusätzliche Anzeigeschlitze, aber keine doppelten
Meldungszustände.

## Eigene Meldungen bearbeiten, schließen und löschen

- Der Meldungseditor ist ein eigenes, scrollbar aufgebautes natives Fenster.
  Er kann geöffnet bleiben, während das Hauptfenster minimiert ist.
- Ein Doppelklick auf einen eigenen Schlitz öffnet die Regel zum Bearbeiten.
- Ist bereits ein anderer ungespeicherter Entwurf geöffnet, behält UNMA diesen
  bei und zeigt einen auffälligen Warnhinweis, statt ihn still zu überschreiben.
- Beim Schließen eines Editors mit Entwurf stehen vier Möglichkeiten bereit:
  - **SPEICHERN & SCHLIESSEN** speichert die Regel und schließt den Editor;
  - **MINIMIEREN** schließt das Fenster, behält den Entwurf aber für später;
  - **VERWERFEN** entfernt die ungespeicherten Änderungen.
  - **ZURÜCK ZUM EDITOR** bricht das Schließen ab und kehrt zum Entwurf zurück.
- **ENTWURF LEEREN** setzt den Editor ohne Speichern zurück.
- Beim Bearbeiten einer vorhandenen eigenen Meldung kann sie mit
  **MELDUNG LÖSCHEN** und einem zweiten Druck zur Bestätigung entfernt werden.

## Vanilla-Spielmeldungen

Unter **MELDUNGSOPTIONEN** werden bekannte Vanilla-Meldungsarten eingestellt.
Objektbezogene Meldungen lassen sich für genau ein Objekt oder für alle Objekte
desselben Prototyps konfigurieren.

| Modus | UNMA-Ton | HOME / Zähler | Verlauf |
| --- | --- | --- | --- |
| **NORMAL** | Aktiv | Sichtbar | Wird gespeichert |
| **LOGGEN · TON AUS** | Aus | Sichtbar | Wird gespeichert |
| **LOGGEN · TON AUS · AUSBLENDEN** | Aus | Ausgeblendet | Wird gespeichert |
| **NICHT LOGGEN · KOMPLETT IGNORIEREN** | Aus | Ausgeblendet | Wird nicht angelegt |

Objektregeln haben Vorrang vor Prototypregeln. Beim vollständigen Ignorieren
entfernt UNMA außerdem passende aktive und jüngere Ereignisse, die noch sicher
zugeordnet werden können.

Diese Einstellungen wirken ausschließlich auf UNMA. Die ursprüngliche
Captain-of-Industry-Benachrichtigung wird weder deaktiviert noch verändert.

Objektpanels zeigen auch inaktive Vorschauen bekannter Meldungen, die an diesem
Objekt auftreten können. Dadurch lassen sie sich bereits vor ihrem ersten
Auftreten konfigurieren. Eine mit **LOGGEN · TON AUS · AUSBLENDEN** konfigurierte
Meldung bleibt aus HOME, globalen Panels, Zählern und Audio entfernt, zeigt in
ihrem eigenen Objektpanel aber weiterhin ihre echte Aktivfarbe und ihren
Zustand. **NICHT LOGGEN · KOMPLETT IGNORIEREN** bleibt überall unsichtbar.

## Systemmeldungen

Der Tab **SYSTEM** enthält die eingebaute Überwachung für Gesundheit, Nahrung
und Arbeiter. Jede Systemmeldung kann aktiviert, vollständig bearbeitet oder
auf ihre Werkvorgabe zurückgesetzt werden. Die Stufen enthalten Messwert,
Operator, Schwelle, Hysterese, Auslöse- und Rücksetzverzögerung,
Mindestaktivzeit, Alarmstufe, Farbe und Ton. Jede Stufe wird unabhängig
zeitlich ausgewertet; anschließend zeigt UNMA die höchste passende Alarmstufe
und Priorität. Das Zurücksetzen auf Werkvorgabe muss durch einen zweiten Druck
bestätigt werden.

Der Gesundheitswert des Spiels ist keine klassische 0–100-Prozent-Skala. `10`
ist der neutrale Basiswert; ein gesundheitsbedingter Bevölkerungsverlust beginnt
unter `0`. UNMA verwendet den abgeschlossenen Monatswert und berücksichtigt
Krankheit, Pollution, erwarteten Bevölkerungsverlust und Arbeitsreserve.

In den Werkvorgaben bleibt **NOTFALL** einer aktiven Gesundheits- oder
Hungertodesspirale vorbehalten. Reiner Arbeitermangel eskaliert höchstens auf
**KRITISCH**.

## Töne

UNMA enthält Warnklingel, Industriehorn, Motorsirene und mehrere synthetische
Signale. Töne werden wiederholt, solange eine Meldung nicht quittiert ist.

Eigene PCM-WAV- oder Ogg-Vorbis-Dateien kommen nach:

```text
Mods/UNMA/Sounds/
```

Nach dem Hinzufügen unter **OPTIONEN** die Tondateien neu einlesen. Nur wenn eine
gültige Datei danach weiterhin fehlt, ist ein Neustart erforderlich. Es dürfen
nur eigene oder entsprechend lizenzierte Audiodateien verwendet und
weitergegeben werden.

Ton und automatische Quittierung beim Gehen lassen sich je bekannter
Meldungsart einstellen. Die Lautstärke ist eine globale Mod-Einstellung. Eigene
Regeln wählen ihren Ton und ihr Quittierverhalten direkt im Editor.

## Optionen

Die Seite **OPTIONEN** ist vollständig vertikal scrollbar. Den Mauszeiger über
der Seite halten und mit dem Mausrad zu den unteren Bereichen wechseln, wenn
das Fenster klein oder die Inhaltsskalierung groß ist.

- Die Inhalte von Hauptfenster, Editor und abgekoppelten Tafeln in
  25-Prozent-Schritten von 75 bis 200 Prozent skalieren oder auf 100 Prozent
  zurücksetzen. Native COI-Rahmen, Hauptnavigation und Launcher behalten die
  vom Spiel vorgegebene UI-Skalierung.
- Warn-, Kritisch- und Notfallfarbe bearbeiten und speichern.
- Den Ordner für eigene Töne anzeigen und unterstützte WAV- und Ogg-Dateien
  ohne Neustart neu einlesen.
- Informationen zu Systemmeldungen, abgekoppelten Tafeln und Zustandsmodell
  einsehen.
- Provider-JSON, API-, Sprach- und Tondaten neu laden sowie
  Integrationsdiagnosen prüfen.

Globale Panels werden unter **MELDETAFEL** verwaltet; Alarmstufen werden unter
**SYSTEM** bearbeitet. Audio-Aktivierung, Lautstärke, Prüfintervall und globale
Systemüberwachung sind Mod-Einstellungen. Ihre Startvorgaben stehen in der
folgenden Tabelle.

UNMA verhindert, dass Klicks, Ziehen oder Mausradbewegungen innerhalb seiner
sichtbaren nativen Fenster gleichzeitig die Spielwelt im Hintergrund
beeinflussen. Außerhalb der Fenster bleiben Gebäudeauswahl, Kartenbewegung und
Zoom frei. Native Textfelder blockieren Spiel-Tastenkürzel nur, solange sie den
Tastaturfokus besitzen.

Die Startvorgaben in `config.json` lauten:

| Option | Vorgabe | Zweck |
| --- | ---: | --- |
| `showOnGameStart` | `true` | UNMA nach dem Laden einer Welt öffnen |
| `enableAudio` | `true` | Alarmtöne bis zur Quittierung wiederholen |
| `audioVolumePercent` | `65` | UNMA-Lautstärke von 0 bis 100 Prozent |
| `pollIntervalMs` | `500` | Eigene Regeln alle 500 ms auswerten |
| `enableSystemAlarms` | `true` | Gesundheit, Nahrung und Arbeiter überwachen |

## Meldungen anderer Mods

Aktive Mods können UNMA um Alarmdefinitionen, Entity-Messwerte, Vorlagen und
direkt veröffentlichte Meldungszustände erweitern. Je nach Provider erzeugt
eine Vorlage einen Sammelschlitz oder einen stabilen Schlitz pro passender
Entity.

Providerfehler werden isoliert, damit eine beschädigte Erweiterung weder andere
Provider noch UNMA am Laden hindert. Sichtbare Providertexte verwenden die
aktive MultiLangLib-Sprache, sofern der Provider Übersetzungen mitliefert.

Die Programmierschnittstelle ist in der
[externen Mod-API](https://github.com/max2605/UNMA/blob/main/docs/external-mod-api.md)
dokumentiert.

## Optionale Daten für externe Anzeigen

UNMA schreibt fehlertolerante lokale JSON-Daten für optionale Begleitanzeigen:

```text
%LOCALAPPDATA%/UNMA/notifications.jsonl
%LOCALAPPDATA%/UNMA/panels.json
```

Die erste Datei enthält Meldungsübergänge, die zweite den aktuellen Panel- und
Schlitzstand. Dateisystemfehler werden protokolliert, unterbrechen aber nicht
die Spielsimulation. Für den normalen Betrieb ist keine externe Anzeige nötig.

## Gespeicherte Daten und Entfernung

UNMA speichert weltbezogene Daten in
`Mods/UNMA/unma-world-<GameId>.json`. Diese Dateien sind nicht Bestandteil des
Release-Archivs. Folgende Informationen überleben Speichern und Neuladen:

- Paneldefinitionen und Schlitzreihenfolge;
- eigene Regeln und verknüpfte Panels;
- quittierte aktive Meldungen;
- gegangene, aber noch nicht quittierte Meldungen;
- abgeschlossene Verlaufsereignisse;
- Messpulte, Instrumente, Quellen, Berechnungsarten und Anzeigeskalen;
- angepasste Systemstufen sowie Vanilla-Verhaltens- und Tonregeln;
- Inhaltsskalierung, Launcherposition sowie Größen von Hauptfenster und Editor.

Ist eine Konfigurationsdatei beschädigt, legt UNMA eine Sicherung an und ersetzt
sie durch sichere Vorgaben.

Captain of Industry kann Positionen verschiebbarer nativer Fenster zusätzlich
über sein eigenes Fenstersystem behalten. Vor einer Deinstallation den gesamten
Ordner `Mods/UNMA` sichern, wenn die UNMA-Konfiguration später wiederhergestellt
werden soll.

UNMA kann aus einem vorhandenen Spielstand entfernt werden, da die Mod keine
physischen Spiel-Entities hinzufügt. Beim Löschen des Mod-Ordners werden jedoch
auch UNMAs weltbezogene Dateien gelöscht. Die Mod nur bei geschlossenem Spiel
entfernen.

## Messpult: mehrere Lager überwachen

1. Öffne im Spiel den Inspector des ersten Lagers.
2. Öffne UNMA mit **F8** und wechsle zu **MESSPULT**.
3. Wähle **QUELLE AUS GEÖFFNETEM GEBÄUDE**.
4. Wähle beispielsweise **Füllstand**, gib die Skalenwerte `VON` und `BIS` an,
   öffne **TYP** und wähle das Instrument aus der scrollbaren Vorschaugalerie.
5. Wähle **INSTRUMENT EINBAUEN** und wiederhole die Schritte für weitere
   Kohlelager.

Mit **+ PANEL** legst du weitere benannte Messpulte an. Die Typauswahl klappt
als Galerie mit Vorschau aller Instrumente auf. Wird ein Messpult entfernt,
verschiebt UNMA seine Instrumente auf ein verbleibendes Panel, statt sie zu
löschen.

Der kleine Pfeil öffnet die erste Quelle; das **X** baut nur das Instrument
aus. Hängt ein offener Entwurf oder eine gespeicherte Meldung davon ab, muss
diese zuerst abgeschlossen oder gelöscht werden. Mit **MELD.** entsteht eine
Alarmregel direkt für diesen – auch berechneten – Messwert. Der Editor zeigt
dabei **VERKNÜPFTE WERTE: Schildname** als Quelle. Über die Wertauswahl können
auch weitere Instrumente desselben Messpults als Bedingungen ergänzt werden.
**INSTRUMENT** ist dafür der dritte Quellenbutton neben Gebäudeauswahl und
globalen Variablen. Jede Instrumentmeldung besitzt eine eigene Zielauswahl:
Ein oder mehrere Meldungspanels können gewählt werden, wobei mindestens eines
ausgewählt bleiben muss. Es wird kein Panel mehr stillschweigend übernommen. Für
Papierschreiber öffnet **ARCHIV** eine große
Historienansicht für einen Spieltag, einen Spielmonat, ein Spieljahr, zehn
Jahre, ein Jahrhundert oder die gesamte noch gespeicherte Laufzeit. Der
Schreiber läuft von links nach rechts; neue Werte
werden nicht in den vorhandenen Ausschnitt zusammengedrückt.

Instrumentdefinitionen und Panelaufteilung werden je Spielwelt gespeichert.
Die Messproben des Schreiberarchivs existieren nur während der laufenden
Sitzung und beginnen nach erneutem Laden der Welt von vorn.

Über **+ GEÖFFNETES GEBÄUDE MIT GLEICHEM MESSWERT** können weitere geöffnete
Gebäude mit derselben Messgröße in ein Instrument aufgenommen werden. Als
Berechnung stehen **EINZEL**, **SUMME**, **MITTEL**, **MIN** und **MAX** bereit.
In der zugehörigen Meldung kann entweder **WERT**, **RÜCKGANG**, **ZUNAHME**
oder **HALTEN** gewählt werden. Rückgang und Zunahme arbeiten wahlweise mit
Betrag oder Prozent. HALTEN fordert, dass Operator und Sollwert während des
kompletten Zeitraums erfüllt bleiben. Zeiträume werden in Spieltagen,
Spielmonaten, Spieljahren, Jahrzehnten oder Jahrhunderten angegeben und
berücksichtigen Pause sowie Spielgeschwindigkeit. Eine einzige Verletzung
startet HALTEN neu.

Profilanzeigen eignen sich besonders für lange Reihen. CRT und
Papierschreiber zeigen zusätzlich einen kontinuierlich verbundenen
Kurzzeittrend.

## Fehlerbehebung

- **UNMA wird nicht geladen:** Prüfen, ob UNMA und MultiLangLib installiert,
  aktiviert und mit Captain of Industry 0.8.6c kompatibel sind. Außerdem muss
  der Pfad `Mods/UNMA/manifest.json` statt
  `Mods/UNMA/UNMA/manifest.json` lauten.
- **Der Launcher fehlt:** **F8** drücken. Bei geöffnetem Hauptfenster ist der
  Launcher absichtlich ausgeblendet. Er erscheint außerdem nur in einer
  geladenen Welt und außerhalb unterdrückender Spielmenüs.
- **Ein Spielfenster überdeckt UNMA:** Das ist die normale native
  Fensterreihenfolge. Das gewünschte UNMA-Fenster anklicken, um Rahmen und
  Inhalt gemeinsam nach vorn zu holen, oder die native COI-Pin-Funktion nutzen.
- **Nur der UNMA-Inhalt bleibt über einem Spielfenster, während sein Rahmen
  dahinterliegt:** Version 0.9.18 hat dieses alte Misch-Layer-Verhalten
  entfernt. Das Spiel schließen, eine doppelte `Mods/UNMA`-Installation
  ausschließen, die DLL aktualisieren und neu starten.
- **Der untere Teil von OPTIONEN fehlt:** Mit dem Mauszeiger über der Seite
  scrollen, das Fenster vergrößern oder die Inhaltsskalierung vorübergehend
  verkleinern.
- **Eine Meldung fehlt in HOME:** HOME zeigt nur aktuell aktive Meldungen.
  Außerdem unter **MELDUNGSOPTIONEN** prüfen, ob sie ausgeblendet oder komplett
  ignoriert wird.
- **Eine bekannte Vanilla-Meldung fehlt in der Liste:** Einige Arten werden
  erst zur Laufzeit entdeckt. Im passenden Objektpanel werden registrierte
  potenzielle Meldungen bereits vor dem Auftreten angezeigt, sofern das Spiel
  sie zur Verfügung stellt.
- **Ein Produktmesswert fehlt:** Das Objekt das Produkt einmal verarbeiten
  lassen und danach die Quellauswahl neu übernehmen.
- **Eine Bedingung zeigt eine fehlende Quelle:** Das Objekt wurde möglicherweise
  entfernt, sein Prototyp hat sich geändert oder ein Providerwert ist nicht mehr
  verfügbar.
- **Eine Prozentregel löst nie aus:** Prüfen, ob der Bezugswert vorhanden und
  größer als null ist.
- **Ein eigener Ton fehlt:** Dateiformat und Ablageort prüfen, unter
  **OPTIONEN** die Tondateien neu einlesen und erst danach gegebenenfalls das
  Spiel neu starten.
- **Ein Entwurf wurde nicht ersetzt:** Das ist beabsichtigt. Den bestehenden
  Entwurf zuerst speichern, verwerfen oder leeren.

## Aktuelle Grenzen

- Logistikzonen, Designationen und abstrakte Routen sind keine auswählbaren
  Spiel-Entities und können daher nicht direkt als Objektquelle dienen.
- Ein vollständig unbenutztes, leeres Mehrproduktobjekt bietet einzelne
  Produkte möglicherweise erst an, nachdem eines vorhanden war.
- Transportkapazität beschreibt den momentanen Inhaltsraum, nicht den Durchsatz
  pro Zeit.
- Abgekoppelte Panels bleiben innerhalb der Spieloberfläche. Eigene
  Betriebssystemfenster auf weiteren Monitoren erfordern eine separate
  Begleitanwendung.

## Links

- [UNMA-Releases](https://github.com/max2605/UNMA/releases)
- [Externe Mod-API](https://github.com/max2605/UNMA/blob/main/docs/external-mod-api.md)
- [Provider-Integrations-Schnellstart](https://github.com/max2605/UNMA/blob/main/docs/provider-integration.de.md)
