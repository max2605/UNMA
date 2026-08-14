# UNMA Benutzeranleitung

Diese Anleitung gilt für **UNMA 0.10.5** und
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
   **ALLE**, **NICHT ZUGEORDNET** oder einen Betriebsbereich auswählen, um die
   Tafel einzugrenzen.
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
Solange ein per Tastatur bedienbares UNMA-Element – etwa Textfeld, Taste oder
Schalter – den Fokus besitzt, unterdrückt UNMA die Spiel-Tastenkürzel. Dadurch
kann auch eine neu belegte Buchstabentaste beim Schreiben oder Bedienen keine
zweite Operatoraktion auslösen.

Der Meldungseditor zeigt seine lokalen Tastaturaktionen in der festen
Aktionsleiste: **Strg+Eingabe** speichert einen vollständigen Entwurf mit der
aktuell gewählten Einstellung **MELDUNG AKTIV**, **Esc** fordert das Schließen
des Editors an. Bei ungespeicherten Änderungen erscheint zuvor der
Schließen-Dialog; sie werden nicht still verworfen.

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
eine abgekoppelte Tafel bleibt davon unabhängig. Positionen und Größen werden
je Spielwelt gespeichert und auf den sichtbaren Bereich begrenzt.

Klicks, Ziehen und Mausradbewegungen innerhalb eines sichtbaren UNMA-Fensters
bleiben in diesem Fenster und erreichen nicht die Spielwelt dahinter. Solange
ein UNMA-Textfeld, Button oder Schalter den Tastaturfokus besitzt, werden
Spiel- und Mod-Shortcuts blockiert. Ein Fokuswechsel aus den UNMA-Bedienelementen
oder das Schließen des fokussierten Fensters gibt die Spieltastatur wieder frei.

Rückmeldungen und Validierungsfehler erscheinen in einer festen Statusfläche
außerhalb des scrollbaren Inhalts. Dadurch bleibt das Ergebnis einer Aktion
sichtbar, ohne zum Anfang oder Ende einer Seite zu scrollen. Vorübergehende
Hinweise verschwinden nach acht Sekunden; dauerhaft angezeigte Fehler lassen
sich mit der Taste **×** schließen.

### Tabs des Hauptfensters

| Tab | Zweck |
| --- | --- |
| **MELDETAFEL** | HOME, globale und objektbezogene Panels, Quittierung und neue Meldeschlitze |
| **MESSPULT** | Live-Instrumente, berechnete Werte, Papierschreiber und Instrumentmeldungen |
| **VERLAUF** | Aktuelle und abgeschlossene Meldungsereignisse |
| **SYSTEM** | Eingebaute Überwachung für Gesundheit, Nahrung und Arbeiter |
| **MELDUNGSOPTIONEN** | Ton, Sichtbarkeit, Protokollierung und Vanilla-Verhalten je Meldungsart |
| **OPTIONEN** | Inhaltsskalierung, Alarmfarben, spielstandsübergreifendes Profil, Ton-Neueinlesen und Integrationsdiagnose |

## Meldungszustände

UNMA arbeitet wie eine klassische industrielle Meldeanlage.

| Kürzel | Zustand | Anzeigeverhalten |
| --- | --- | --- |
| `K` | Meldung ist gekommen und nicht quittiert | Aktivfarbe; blinkt ohne „Reduzierte Bewegung“ und kann den Ton wiederholen |
| `KQ` | Meldung ist gekommen und quittiert | Bleibt aktiv stehen, ohne den Ton zu wiederholen |
| `KG` | Ursache ist gegangen, Meldung aber nicht quittiert | Ruhige kontrastreiche Verlaufsmarkierung bis zur Quittierung; tönt nach dem Gehen nicht weiter |
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

HOME ist unter **ALLE** die Live-Übersicht aller aktuell anstehenden Meldungen.
Angezeigt werden `K` und `KQ` aus allen Quellen. HOME besitzt keine festen
Schlitze; inaktive, gegangene und leere Plätze werden ausgeblendet.

HOME ordnet die Karten nach Alarmstufe von Notfall bis Hinweis und danach nach
der stabilen Alarmkennung. Quittierung, ein weiteres Vorkommen oder der reine
Zeitablauf lassen sonst unveränderte Karten deshalb nicht mehr umherspringen.

### Betriebsbereiche

Betriebsbereiche ordnen globale Panels, ohne einen zweiten Alarmzustand,
Verlaufseintrag, Historian-Datenstrom oder Audiostatus zu erzeugen. Die
Filterzeile bietet **ALLE**, **NICHT ZUGEORDNET** und jeden selbst angelegten
Bereich:

- **ALLE** erhält die vollständige bisherige Panel- und HOME-Ansicht.
- **NICHT ZUGEORDNET** zeigt globale Panels ohne Betriebsbereich.
- Ein konkreter Bereich zeigt nur seine Mitgliedspanels. Sein HOME sammelt
  deren aktive Meldungen und führt sie zu genau einem Schlitz je zugrunde
  liegendem Alarm zusammen.

Der ausgewählte Bereichschip zeigt die Zahl seiner Panels sowie aktiver und
unbestätigter Alarme. **BEREICH QUITT.** und **BEREICH WEITER** arbeiten nur
mit den Alarmen der ausgewählten Bereichs- beziehungsweise
**NICHT ZUGEORDNET**-Ansicht. **ALLES QUITTIEREN** bleibt immer die
ausdrückliche globale Aktion.

Die Quittierung gehört weiterhin zur zugrunde liegenden Alarmfolge und nicht
zur Bereichsansicht. Ist derselbe Alarm über Panels in mehreren Bereichen
sichtbar, wird er durch eine Quittierung in einem Bereich überall quittiert.
UNMA erzeugt dafür weder getrennte Zustände noch doppelte Verlaufseinträge.

Über **⚙ BEREICHE** lassen sich Bereiche anlegen, umbenennen, umsortieren und
zum Löschen vormerken. Alle Änderungen bleiben in einem gemeinsamen Entwurf
und werden erst beim Speichern atomar angewendet. Beim Löschen bleiben Panels,
Schlitze, Regeln und Alarmzustände erhalten; die Panels werden lediglich
**NICHT ZUGEORDNET**. Warnt UNMA vor ungespeicherten Bereichs- oder
Panel-Einstellungen, muss der Entwurf gespeichert, verworfen oder weiter
bearbeitet werden.

Die Zuordnung eines globalen Panels befindet sich in seinen
Zahnrad-Einstellungen. Ein dupliziertes Panel übernimmt den Bereich seiner
Quelle. Ein neues Panel übernimmt nur den konkret ausgewählten Bereich; unter
**ALLE** oder **NICHT ZUGEORDNET** beginnt es unzugeordnet. Objektpanels und
HOME werden keinem Bereich zugewiesen.

### Incident-Linse

Die **INCIDENT-LINSE** erscheint ausschließlich über dem HOME-Dashboard. Ihre
eingeklappte Leiste zeigt globalen Alarmdruck und Zähler; **ERWEITERN** öffnet
eine nur lesende Ansicht zeitlicher Cluster unter den aktiven Alarmen des
aktuellen Dashboard-Bereichs. Auf festen globalen oder Objektpanels erscheint
die Linse nicht.

Die Gruppierung ist bewusst eine Heuristik. Aufeinanderfolgende aktive
Alarmvorkommen bilden einen zeitlichen Incident-Cluster, wenn zwischen ihren
Kommen-Zeitpunkten höchstens zwei Spieltage liegen. **ERSTES SIGNAL** ist nur
das früheste beobachtete Mitglied dieses Clusters. Es ist weder eine
bestätigte Ursache oder Grundursache noch ein Beweis dafür, dass eine Meldung
eine andere ausgelöst hat.

Die Cluster-Mitgliedschaft folgt dem gewählten Filter **ALLE**, **NICHT
ZUGEORDNET** oder einem konkreten Betriebsbereich. Die Druckanzeige bleibt
absichtlich global, damit ein enger Filter einen inselweiten Alarmsturm nicht
unsichtbar macht. Sie betrachtet die letzten zehn Spieltage und gewichtet
jedes Vorkommen nach Alarmstufe:

| Alarmstufe | Gewicht |
| --- | ---: |
| Hinweis | 1 |
| Warnung | 2 |
| Kritisch | 4 |
| Notfall | 8 |

Ein Druck unter 8 ist **NORMAL**, 8–15 ist **ERHÖHT**, 16–31 ist **STURM**
und ab 32 **SCHWER**. Dieselbe Übersicht nennt die letzten Vorkommen und die
Zahl verschiedener Alarmkennungen. Wiederholungen erhöhen damit den ersten
Zähler, ohne als zusätzliche Alarmart ausgegeben zu werden.

Die erweiterte Ansicht stellt höchstens sechs Incident-Karten und acht
Mitglieder je Karte dar. Eine Zeile `+ N WEITERE` erhält bei Erreichen dieser
Anzeigegrenze die vollständigen Zähler. **FOKUS** springt zu einem weiterhin
sichtbaren Mitglied und, soweit verfügbar, zu seinem Spielobjekt. Der Fokus
quittiert, verbirgt oder löscht keine Meldung, schaltet sie nicht stumm und
verändert weder Verlauf noch Audiostatus.

Incident-Snapshots sind vorübergehende, aus den aktuellen Alarm- und
Verlaufssnapshots abgeleitete Ergebnisse. Sie fügen keine Speicherfelder
hinzu und benötigen in 0.10.2 keine neue Schema-Migration. Für sichere
Performance fragt die UI höchstens einmal je Frame und Filter ab. Der
Laufzeitverlauf wird nur bei geänderter Revision neu kopiert, der globale
Druck ist auf die neuesten 8.192 Vorkommen begrenzt, und Sortierung sowie
Analyse laufen außerhalb des Alarm-Locks. Bei fortlaufenden Änderungen liefert
UNMA nach höchstens zwei Versuchen ein konsistentes ungecachtes Ergebnis,
statt den Renderpfad zu blockieren.

### Globale Panels

Globale Panels sind dauerhaft angelegte Meldetafeln für Spiel-, System-,
Provider- und eigene Meldungen.

- Mit **+ PANEL** neben der globalen Panelleiste unter **MELDETAFEL** ein neues
  Panel anlegen.
- Über das benachbarte Zahnrad Name, Spaltenzahl, Filter, automatische Quellen
  und Schlitzreihenfolge ändern.
- Mit **SPALTEN −/+** direkt auf der Tafel festlegen, wie viele Karten in eine
  Zeile passen. **KARTEN · KOMPAKT/NORMAL** schaltet die Kartenhöhe für die
  laufende Sitzung zwischen kompakten 104 und normalen 142 Pixeln um; das gilt
  im Hauptfenster und in abgekoppelten Tafeln.
- **PANEL DUPLIZIEREN** erstellt dort eine unabhängige Kopie einschließlich
  Reihenfolge, Filter und eigener Meldungen. Die kopierten eigenen Meldungen
  erhalten neue Kennungen und starten aus Sicherheitsgründen deaktiviert;
  bestehende Alarmzustände und der Verlauf werden nicht kopiert. Das neue
  Panel behält die Bereichszuordnung seiner Quelle.
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

Eigene Meldeschlitze bieten eine sichtbare Aktion **BEARBEITEN**. Der bisherige
Doppelklick öffnet die zugehörige Regel weiterhin als Schnellaktion direkt im
Editor. Die Symbolaktionen zum Öffnen des Objekts, Quittieren sowie Pausieren
oder Fortsetzen des Tons erklären ihre vollständige Funktion im Tooltip.

### Abgekoppelte Panels

Das gerade angezeigte HOME-, globale oder Objektpanel kann in ein eigenes
natives Fenster abgekoppelt werden. Dieses Fenster zeigt denselben Panel- und
Meldungszustand; es erzeugt keine zweite Meldung. Für jedes Panel gibt es
höchstens ein abgekoppeltes Fenster; erneutes Abkoppeln holt das vorhandene
Fenster nach vorn. Abgekoppelte Tafeln stellen höchstens fünf Spalten dar.

Das Schließen einer abgekoppelten Tafel entfernt nur diese Ansicht. Panel,
Schlitze und Meldungen bleiben erhalten. Position, Größe und Öffnungszustand
werden je Spielwelt gespeichert. Beim erneuten Öffnen verwendet UNMA die letzte
Position; Tafeln, die beim Speichern offen waren, werden beim nächsten Start
wiederhergestellt.

## Eigene Meldung erstellen

Eine Regel kann in einem globalen Panel oder in einem Objektpanel begonnen
werden.

1. Das gewünschte Zielpanel öffnen.
2. **+ NEUE MELDUNG** oder einen freien Plus-Schlitz drücken.
3. Oben den **MELDUNGSTITEL** eingeben. Er erscheint im Meldeschlitz und im
   Verlauf.
4. Die Quelle für die erste Bedingung wählen.
5. Einen Messwert auswählen.
6. Berechnung, Vergleichsoperator und Soll-Wert festlegen.
7. **+ ZEILE HINZUFÜGEN** drücken.
8. Bei Bedarf weitere Zeilen ergänzen.
9. Alarmstufe, Aktivfarbe, Ton und Quittierverhalten einstellen und mit
   **MELDUNG AKTIV** festlegen, ob die Regel nach dem Speichern ausgewertet
   werden soll.
   Auslöse-/Rücksetzzeiten und Eskalation liegen unter **ERWEITERTE
   EINSTELLUNGEN**. Der eingeklappte Kopf zeigt, ob Standardwerte gelten,
   Einstellungen vorhanden sind oder eine Eingabe geprüft werden muss.
10. Die feste Aktionsleiste prüfen. **SPEICHERN & AKTIVIEREN** steht bereit,
    sobald Titel, Zielpanel, mindestens eine Bedingung, Farbe und Zeitwerte
    gültig sind und **MELDUNG AKTIV** gewählt ist. Ist der Schalter aus, wird
    dieselbe vollständig konfigurierte Regel mit **INAKTIV SPEICHERN**
    gespeichert, aber nicht ausgewertet.

Jede Bedingungszeile zeigt ihren aktuellen Ist-Wert, solange ihre Quelle
verfügbar ist.

Die Aktionsleiste bleibt beim Scrollen sichtbar und kennzeichnet
**BEREIT ZUM SPEICHERN** oder **NOCH UNVOLLSTÄNDIG**. Kann eine Regel nicht
gespeichert werden, nennt die Validierung direkt den fehlenden Meldungstitel,
eine fehlende Bedingung oder Zieltafel sowie ungültige Farb- oder Zeitwerte.
Bei extrem kleinem Fenster und hoher UI-Skalierung wird dieselbe Leiste auf
eine kompakte Zeile reduziert; die vollständigen Aktionsnamen stehen in den
Tooltips.

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

### Eskalation und Operator-Aufmerksamkeit

**ESKALATION** wird für eine eigene Regel aktiviert, wenn eine weiterhin aktive
Meldung nach einer gewählten Spielzeit auf eine strikt höhere Alarmstufe
angehoben werden soll. Dafür kann ein eigener Ton gewählt oder mit
**GRUNDTON ÜBERNEHMEN** der bisherige Ton beibehalten werden. Die Eskalation
beginnt eine neue Meldungsfolge: Sie verlangt erneut eine Quittierung und
übernimmt keine folgengebundene Tonpause des vorherigen Zustands.

Die optionale Operatoraktion öffnet das passende UNMA-Panel und scrollt zur
Meldung. Eine zweite Variante beendet zusätzlich ausschließlich die
vorübergehende Fünf-Minuten-Stummschaltung von UNMA. Sie bewegt nie die Kamera,
öffnet keinen Objektinspektor, ändert weder globale Audioeinstellung noch
Schlitz-Tonpause, quittiert nichts und steuert keine Maschine.

Systemalarmstufen bieten dieselben Aktionen. Sie werden nur ausgeführt, wenn
ein bereits aktiver Systemalarm in eine neue Stufe wechselt, nicht bei seiner
Erstaktivierung. Für eine gestufte Eskalation wird eine niedrige Sofortstufe
mit einer höheren, auslöseverzögerten Stufe kombiniert.

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
- **ERWEITERTE EINSTELLUNGEN** ist zunächst eingeklappt, damit für eine
  Standardmeldung nicht an Zeitsteuerung und Eskalation vorbeigescrollt werden
  muss. Die Zusammenfassung kennzeichnet Standardwerte, konfigurierte Werte
  und fehlerhafte verdeckte Eingaben; der Kopf liegt in der normalen
  Tastatur-Fokusreihenfolge.
- **BEARBEITEN** auf einem eigenen Schlitz öffnet die Regel; ein Doppelklick
  bleibt als Schnellaktion erhalten.
- Inaktive eigene Regeln tragen das Kennzeichen **INAKTIV** und werden nicht
  ausgewertet, bis sie gespeichert und aktiviert werden.
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
Sie lassen sich global nach Meldungstyp, für genau ein Objekt oder für alle
Objekte desselben Prototyps konfigurieren.

| Modus | UNMA-Ton | HOME / Zähler | Verlauf |
| --- | --- | --- | --- |
| **NORMAL** | Aktiv | Sichtbar | Wird gespeichert |
| **LOGGEN · TON AUS** | Aus | Sichtbar | Wird gespeichert |
| **LOGGEN · TON AUS · AUSBLENDEN** | Aus | Ausgeblendet | Wird gespeichert |
| **NICHT LOGGEN · KOMPLETT IGNORIEREN** | Aus | Ausgeblendet | Wird nicht angelegt |

Objektregeln haben Vorrang vor Prototypregeln; diese wiederum haben Vorrang vor
globalen Meldungstypregeln. Beim vollständigen Ignorieren
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
Mindestaktivzeit, Alarmstufe, Farbe, Ton und eine optionale Operatoraktion.
Jede Stufe wird unabhängig zeitlich ausgewertet; anschließend zeigt UNMA die
höchste passende Alarmstufe und Priorität. Das Zurücksetzen auf Werkvorgabe muss
durch einen zweiten Druck bestätigt werden.

Der Gesundheitswert des Spiels ist keine klassische 0–100-Prozent-Skala. `10`
ist der neutrale Basiswert; ein gesundheitsbedingter Bevölkerungsverlust beginnt
unter `0`. UNMA verwendet den abgeschlossenen Monatswert und berücksichtigt
Krankheit, Pollution, erwarteten Bevölkerungsverlust und Arbeitsreserve.

Pollution wird innerhalb der Systemmeldung **GESUNDHEIT** konfiguriert und ist
keine eigene Hauptkategorie. Unter **SYSTEM → GESUNDHEIT** lässt sich die Stufe
**VERSCHMUTZUNG KRITISCH** bearbeiten. Ihre Werkbedingung lautet
**Verschmutzungs-/Müllbeitrag ≤ −5 Punkte**; sie kann wie jede andere
Systemstufe aktiviert, deaktiviert oder angepasst werden.

In den Werkvorgaben bleibt **NOTFALL** einer aktiven Gesundheits- oder
Hungertodesspirale vorbehalten. Reiner Arbeitermangel eskaliert höchstens auf
**KRITISCH**.

## Töne

UNMA enthält Warnklingel, Industriehorn, Motorsirene und mehrere synthetische
Signale. Töne werden wiederholt, solange eine Meldung aktiv und unquittiert ist.

Hörbar ist nur eine noch aktive, unquittierte, weder tonpausierte noch
unterdrückte Meldung mit einem hörbaren Ton. Ein gegangener, aber noch
unquittierter `KG`-Zustand bleibt für Quittierung und Verlauf erhalten, wird
jedoch sofort stumm, sobald seine Ursache nicht mehr ansteht.

**TÖNE · AN/AUS** unter **MELDETAFEL** ist die sitzungsweite
Master-Stummschaltung. **AUS** sperrt sofort jeden UNMA-Ton einschließlich
Hörproben, ohne eine Meldung zu quittieren, zu verstecken oder zu verändern.
Nach **AN** darf die aktuell berechtigte Meldung wieder tönen. Der Zustand wird
bewusst weder in der Weltdatei noch im spielstandsübergreifenden Profil
gespeichert.

Während eine Meldung tatsächlich tönt, erhält ihre Karte eine blaue Kontur und
eine **TÖNE**-Markierung. Eine Leiste oberhalb der Karten nennt Alarmstufe,
Meldungsname und stabile Kennung; ein Druck darauf wählt die Meldung in HOME
und scrollt zu ihr. So lässt sich die Quelle eines Horns oder einer Sirene auch
bei geöffnetem anderem Panel eindeutig finden.

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
  vom Spiel vorgegebene UI-Skalierung. Die Mindestgrößen der Fenster wachsen
  innerhalb des verfügbaren Bildschirms mit; Panel- und Bereichseinstellungen
  stapeln ihre Bedienelemente, damit alle notwendigen Aktionen auch bei
  200 Prozent erreichbar bleiben.
- Warn-, Kritisch- und Notfallfarbe im Format `#RRGGBB` bearbeiten und
  speichern. Ungültige Farbcodes werden am Feld erklärt und nicht übernommen.
- Unter **BARRIEREARMUT** mit **REDUZIERTE BEWEGUNG** blinkende
  Alarmhintergründe durch eine ruhige, dauerhaft sichtbare Markierung ersetzen.
  Alarmzustand, Stufe und Quittierung bleiben weiterhin als Text erkennbar.
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
Zoom frei. Spiel-Tastenkürzel werden nur blockiert, solange ein per Tastatur
bedienbares UNMA-Element den Fokus besitzt.

Die Startvorgaben in `config.json` lauten:

| Option | Vorgabe | Zweck |
| --- | ---: | --- |
| `showOnGameStart` | `true` | UNMA nach dem Laden einer Welt öffnen |
| `enableAudio` | `true` | Töne für aktive, unquittierte Meldungen wiederholen |
| `audioVolumePercent` | `65` | UNMA-Lautstärke von 0 bis 100 Prozent |
| `pollIntervalMs` | `500` | Eigene Regeln alle 500 ms auswerten |
| `enableSystemAlarms` | `true` | Gesundheit, Nahrung und Arbeiter überwachen |
| `autoPauseEnabled` | `false` | Das Spiel bei passenden neuen UNMA-Meldungsfolgen pausieren |
| `autoPauseMinimumSeverity` | `2` | Mindeststufe: 0 Hinweis, 1 Warnung, 2 Kritisch, 3 Notfall |
| `autoPauseVanilla` | `true` | Auto-Pause für Vanilla-Meldungen zulassen |
| `autoPauseSystem` | `true` | Auto-Pause für eingebaute Systemalarme zulassen |
| `autoPauseCustom` | `true` | Auto-Pause für selbst erstellte Alarme zulassen |
| `autoPauseExternal` | `true` | Auto-Pause für Alarme anderer Mods zulassen |
| `muteAudioWhilePaused` | `false` | UNMA-Töne bei pausiertem Spiel unabhängig von Auto-Pause stummschalten |
| `transferProfilePath` | `""` | Roaming-Standardpfad verwenden oder Datei-/Ordnerpfad vorgeben |

### Automatische Pause

Auto-Pause ist standardmäßig abgeschaltet. Ist sie aktiv, fordert UNMA nur bei
einem neuen, unquittierten Vorkommen ab der gewählten Mindeststufe eine
Spielpause an und nur für zugelassene Quellkategorien. Eine lediglich weiterhin
aktive Meldung fordert nicht immer wieder eine Pause an. Vanilla-Meldungen,
eingebaute Systemalarme, selbst erstellte Alarme und Alarme anderer Mods lassen
sich unabhängig voneinander einbeziehen.

`muteAudioWhilePaused` ist davon getrennt. Bei aktivierter Option bleibt jeder
UNMA-Ton stumm, solange die Simulation pausiert ist, und wird beim Fortsetzen
wieder freigegeben. Die Option funktioniert mit oder ohne Auto-Pause; die
sitzungsweite Master-Stummschaltung **TÖNE · AUS** hat weiterhin Vorrang.

### Spielstandsübergreifendes Standardprofil

Unter **OPTIONEN** kann die aktuelle Konfiguration in einem Standardprofil
gespeichert und in einem anderen Spielstand importiert werden. Das Profil liegt
außerhalb der Weltdateien unter:

```text
%APPDATA%\Captain of Industry\UNMA\profiles\default.json
```

Beim ersten Start nach der Aktualisierung kopiert UNMA ein vorhandenes
Legacy-Profil aus `%LOCALAPPDATA%\UNMA\profiles\default.json` atomar an den
neuen Ort, sofern dort noch keine Datei existiert. Die Quelldatei bleibt
erhalten. Scheitert das Kopieren, verwendet UNMA für diese Sitzung weiter die
Legacy-Datei und versucht die zerstörungsfreie Migration bei einem späteren
Start erneut. Eine bereits vorhandene Roaming-Datei hat immer Vorrang.

Mit `transferProfilePath` in `config.json` kann ein anderer Ablageort gewählt
werden. Der Wert darf Umgebungsvariablen enthalten. Ein vorhandener Ordner, ein
mit einem Pfadtrenner endender Wert oder jeder Pfad ohne Dateiendung gilt als
Ordner; UNMA hängt daran `default.json` an. Für eine bestimmte Datei ist ein
Pfad mit Dateiendung wie `.json` anzugeben. Eine gültige ausdrückliche Vorgabe
hat Vorrang vor beiden Standardorten.

Nur wenn diese Datei tatsächlich fehlt, erzeugt und persistiert UNMA das
eingebaute Profil **UNMA Recommended Quiet**. Exakt erkannte, unveränderte
frühere Built-ins – **UNMA Recommended Silent** mit sechs Silent-Regeln sowie
der Quiet-Zwischenstand mit zwei zusätzlichen Hidden-Regeln – werden beim Laden
nur im Speicher auf das aktuelle Quiet-Profil gebracht; ihre Seed-Dateien
bleiben unverändert. Abweichende und benutzerdefinierte Profile werden weder
ergänzt noch überschrieben. Das eingebaute Profil wird nicht automatisch in
einen Spielstand importiert: Auch dafür müssen zuerst die Vorschau geprüft und
der Import ausdrücklich bestätigt werden.

Das empfohlene Profil setzt ausschließlich diese globalen Meldungsarten auf
**SILENT** beziehungsweise **LOGGEN · TON AUS**:

- `UpgradeInProgress`;
- `DowngradeInProgress`;
- `VehicleGoalStruggling`;
- `VehicleNoReachableDesignations`;
- `NoTreesToHarvest`;
- `ExcavatorHasNoValidTruck`.

**SILENT** schaltet nur den UNMA-Ton dieser Meldungen aus. Die ursprüngliche
Captain-of-Industry-Meldung bleibt unverändert; UNMA zeigt und protokolliert
sie weiterhin in HOME und im Verlauf.

Zusätzlich setzt das Profil diese Meldungsarten auf **IGNORED** beziehungsweise
**NICHT LOGGEN · KOMPLETT IGNORIEREN**:

- `TruckCannotDeliver`;
- `TruckCannotDeliverMixedCargo`.

CoI nimmt diese Fahrzeugmeldungen häufig zurück und sendet sie mit einer neuen,
flüchtigen `NotificationId` erneut. **IGNORED** verwirft jedes neue Ereignis in
UNMA noch vor `SetAlarm`, Verlaufserzeugung und Persistenz. Dadurch wachsen
weder der UNMA-Verlauf noch die Incident-Linse und die Spielstand-Persistenz
mit jedem Flackern weiter. Beim bestätigten Import und beim Normalisieren der
Konfiguration bereinigt UNMA passende aktive Zustände und Memories. Ältere
globale Verlaufseinträge werden ebenfalls entfernt, sofern keine spezifischere,
nicht ignorierende Entity- oder Prototypregel erhalten bleiben muss. Die
ursprüngliche Captain-of-Industry-Meldung bleibt sichtbar und unverändert.
`CannotDeliverFromMineTower`, `VehicleGoalUnreachable` und `VehicleNoFuel` sind
bewusst nicht Teil dieser Empfehlung und bleiben normal sowie hörbar.

Beim Speichern und Importieren sind folgende Kategorien einzeln auswählbar:

- Meldungsregeln einschließlich Tonzuordnung und automatischer Quittierung;
- Systemalarm-Konfiguration;
- Alarmfarben und UI-Skalierung;
- Fensterpositionen, -größen und offene abgekoppelte Tafeln; diese Kategorie
  ist standardmäßig abgewählt.

Kategorien und einzelne Regelzeilen zeigen `[X]` für ausgewählt und `[ ]` für
abgewählt. Die hervorgehobene Auswahl wird sofort aktualisiert und bleibt beim
Vorbereiten der Vorschau sichtbar.

Die globalen Startoptionen aus `config.json`, einschließlich globaler
Audio-Aktivierung und Lautstärke, gelten bereits unabhängig vom Spielstand und
werden nicht in das Profil dupliziert.

Zum Übertragen zuerst im Quellspielstand die gewünschten Kategorien wählen und
das Standardprofil speichern. Im Zielspielstand den Import öffnen und die
Vorschau prüfen. Sie unterscheidet neue, geänderte, unveränderte und
übersprungene Werte. Erst die Bestätigung führt die ausgewählten Werte atomar
mit der Zielkonfiguration zusammen. Gleiche Schlüssel werden durch den
Profilwert ersetzt; andere Zielwerte und nicht ausgewählte Kategorien bleiben
erhalten. Schlägt die Prüfung oder der erste atomare Konfigurationsschreibvorgang
fehl, bleibt die Zielkonfiguration vollständig unverändert. Ein seltener Fehler
beim anschließenden Speichern des abgeglichenen Live-Alarmzustands wird als
Teilfehler gemeldet; die bereits erfolgreich importierten Einstellungen bleiben
dabei bestehen.

Vanilla-Regeln sind portabel, wenn sie nach stabiler Meldungsart
(`NotificationType`) oder nach Meldungsart plus Entity-Prototyp gespeichert
sind. Regeln für eine konkrete Entity-ID gehören zu genau einer Welt und werden
sicher übersprungen; Vorschau und Ergebnis weisen sie aus. Dadurch kann etwa
`UpgradeInProgress` für alle Objekte eines Förderband-Prototyps weiterhin
vollständig in UNMA ignoriert werden, ohne eine zufällig gleich nummerierte
Entity im neuen Spielstand zu treffen.

Verlauf, aktive Alarme, Quittierungen, laufende Verzögerungen, Eskalationen,
Snooze-Zustände und andere zeitliche Memories werden niemals in das Profil
geschrieben oder daraus importiert. Eine übertragene Ignore-Regel beeinflusst
nur UNMA. Die ursprüngliche Captain-of-Industry-Meldung wird weder deaktiviert
noch verändert. Die Profildatei wird atomar mit temporärer Datei und Sicherung
geschrieben.

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

- Paneldefinitionen, Schlitzreihenfolge, Betriebsbereiche und Zuordnungen;
- eigene Regeln und verknüpfte Panels;
- quittierte aktive Meldungen;
- gegangene, aber noch nicht quittierte Meldungen;
- abgeschlossene Verlaufsereignisse;
- Messpulte, Instrumente, Quellen, Berechnungsarten und Anzeigeskalen;
- angepasste Systemstufen sowie Vanilla-Verhaltens- und Tonregeln;
- Inhaltsskalierung, Launcherposition sowie Positionen, Größen und
  Öffnungszustand von Hauptfenster, Editor und abgekoppelten Tafeln.

Schema 20 übernimmt Konfigurationen früherer UNMA-Versionen mit allen
vorhandenen Panels als **NICHT ZUGEORDNET**. Das bisherige Verhalten unter
**ALLE** bleibt dadurch unverändert, bis Bereiche bewusst angelegt und
zugewiesen werden.
Die Incident-Linse speichert weder Konfiguration noch Ergebnis. Auch das
separate Standardprofil erweitert die Weltdatei nicht; 0.10.5 bleibt daher bei
Schema 20. Erkennt diese Version eine Konfiguration aus einem neueren
UNMA-Schema, lässt sie Hauptdatei und Sicherungsartefakte bytegenau unangetastet,
verwendet sichere Vorgaben und sperrt Konfigurationsschreibvorgänge für die
laufende Sitzung, statt unbekannte Zukunftsfelder zu verwerfen.

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
Mit **HIST** öffnet jedes Instrument unabhängig von seinem Anzeigetyp eine
große Historienansicht für einen Spieltag, einen Spielmonat, ein Spieljahr,
zehn Jahre, ein Jahrhundert oder die gesamte noch gespeicherte Laufzeit. Das
gewählte Fenster gilt gleichzeitig für Diagramm und Analyse. Angezeigt werden
aktueller Wert, Minimum, Mittelwert, Maximum, lineare Rate pro Spielmonat und
R². Bei einem belastbaren Trend folgt eine gerichtete ETA zur oberen oder
unteren Skalenbegrenzung; zu wenige, stabile, unzuverlässige oder weiter als
100 Spieljahre entfernte Verläufe werden ausdrücklich benannt.

Instrumentdefinitionen und Panelaufteilung werden je Spielwelt gespeichert.
Die Messproben des Historians existieren nur während der laufenden
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
