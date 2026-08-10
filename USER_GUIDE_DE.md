# UNMA Benutzeranleitung

Diese Anleitung gilt für **UNMA 0.9.17** und
**Captain of Industry 0.8.6c**.

UNMA – die Universelle Nachrichten-Meldeanlage – ergänzt Captain of Industry
um eine frei konfigurierbare industrielle Schlitzmelder-Tafel. Sie spiegelt
Spielmeldungen, führt einen dauerhaften Verlauf, überwacht wichtige globale
Spielwerte und ermöglicht eigene Meldungsregeln für Spielobjekte oder globale
Variablen.

Benötigte Abhängigkeit: **MultiLangLib 0.1.0 oder neuer**.

## Installation und Aktualisierung

1. Die aktuelle UNMA-Version herunterladen.
2. Das Archiv in den `Mods`-Ordner von Captain of Industry entpacken. Danach
   müssen `Mods/UNMA/manifest.json` und `Mods/UNMA/UNMA.dll` vorhanden sein.
3. MultiLangLib 0.1.0 oder neuer installieren und aktivieren.
4. UNMA im Mod-Menü des Spiels aktivieren.
5. Einen Spielstand laden oder ein neues Spiel beginnen.

Für eine Aktualisierung das Spiel schließen und den vorhandenen Ordner `UNMA`
durch den Ordner aus dem neuen Archiv ersetzen. Spielstandsbezogene Panels,
Regeln, Meldungszustände und Quittierungen liegen getrennt von den Mod-Dateien
und gehen dabei nicht verloren.

UNMA kann einem bestehenden Spielstand hinzugefügt und wieder daraus entfernt
werden.

## Schnellstart

1. Mit **F8** das UNMA-Hauptfenster öffnen oder schließen.
2. Alternativ den kompakten Launcher am linken Bildschirmrand verwenden.
3. Unter **MELDETAFEL** befinden sich HOME und die dauerhaft angelegten Panels.
4. **MASTER QUIT / QUITTIEREN** quittiert alle neuen sowie bereits gegangenen,
   aber noch nicht quittierten Meldungen und beendet deren Ton.
5. Unter **VERLAUF** lassen sich frühere Meldungsereignisse einsehen.

Der Launcher erscheint nur bei geschlossenem Hauptfenster. Sein Pfeilgriff
kann vertikal verschoben werden, damit er keine anderen HUD-Elemente verdeckt.
Die Position wird je Spielwelt gespeichert.

Hauptfenster, Meldungseditor und abgekoppelte Tafeln verwenden native
CoI-Rahmen. Sie lassen sich wie Spielfenster verschieben, anheften und am Griff
unten rechts vergrößern oder verkleinern. Das Hauptfenster kann über
**MINIMIEREN** oder das native Schließen-Symbol eingeklappt werden; beides
bringt den kompakten Launcher zurück.

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
werden.

## Panels und Meldeschlitze

### HOME

HOME ist die Live-Übersicht aller aktuell anstehenden Meldungen. Angezeigt
werden `K` und `KQ` aus allen Quellen. HOME besitzt keine festen Schlitze;
inaktive, gegangene und leere Plätze werden ausgeblendet.

### Globale Panels

Globale Panels sind dauerhaft angelegte Meldetafeln für Spiel-, System-,
Provider- und eigene Meldungen.

- Mit **+ PANEL** unter **OPTIONEN** ein neues Panel anlegen.
- Über das Zahnrad Name, Spaltenzahl, Filter, automatische Quellen und
  Schlitzreihenfolge ändern.
- Bekannte Meldungen gezielt in freie Schlitze aufnehmen und Plätze nach oben
  oder unten verschieben.
- Neu entdeckte Meldungen, die zu einer automatischen Quelle oder einem Filter
  passen, werden angehängt, ohne vorhandene Plätze zu verschieben.
- Panels können als eigene verschiebbare In-Game-Tafeln abgekoppelt werden.

### Objektpanels

Unterstützte Gebäude, Lager, Fahrzeuge, Förderbänder und Rohre besitzen ein
eigenes dauerhaftes Panel.

1. Das Objekt im Spiel auswählen.
2. Im Inspector die goldene **UNMA-Alarmglocke** drücken.
3. UNMA öffnet das Panel, das genau zu diesem Objekt gehört.

Der kleine Pfeil in einem objektbezogenen Meldeschlitz zentriert die Kamera auf
das Objekt und öffnet seinen Inspector.

Ein Doppelklick auf einen eigenen Meldeschlitz öffnet die zugehörige Regel
direkt im Editor.

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

- Ein Doppelklick auf einen eigenen Schlitz öffnet die Regel zum Bearbeiten.
- Ist bereits ein anderer ungespeicherter Entwurf geöffnet, behält UNMA diesen
  bei und zeigt einen auffälligen Warnhinweis, statt ihn still zu überschreiben.
- Beim Schließen eines Editors mit Entwurf stehen drei Möglichkeiten bereit:
  - **SPEICHERN & SCHLIESSEN** speichert die Regel und schließt den Editor;
  - **MINIMIEREN** schließt das Fenster, behält den Entwurf aber für später;
  - **VERWERFEN** entfernt die ungespeicherten Änderungen.
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
Operator, Schwelle, Alarmstufe, Farbe und Ton.

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
UNMA/Sounds/
```

Nach dem Hinzufügen muss das Spiel neu gestartet werden. Es dürfen nur eigene
oder entsprechend lizenzierte Audiodateien verwendet und weitergegeben werden.

Ton, Lautstärke und automatische Quittierung beim Gehen lassen sich je
bekannter Meldungsart einstellen. Eigene Regeln wählen diese Eigenschaften
direkt im Editor.

## Optionen

Der Tab **OPTIONEN** enthält globale UI- und Panel-Einstellungen.

- Die gesamte UNMA-Oberfläche von 75 bis 200 Prozent skalieren.
- Globale Panels anlegen und verwalten.
- UNMA-Audio ein- oder ausschalten.
- Alarmlautstärke ändern.
- Eingebaute Systemüberwachung ein- oder ausschalten.

UNMA verhindert, dass Klicks, Ziehen oder Mausradbewegungen innerhalb seiner
sichtbaren Fenster gleichzeitig die Spielwelt im Hintergrund beeinflussen.
Außerhalb der Rahmen bleiben Gebäudeauswahl, Kartenbewegung und Zoom frei.

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

UNMA speichert weltbezogene Daten in `unma-world-<GameId>.json`. Folgende
Informationen überleben Speichern und Neuladen:

- Paneldefinitionen und Schlitzreihenfolge;
- eigene Regeln und verknüpfte Panels;
- quittierte aktive Meldungen;
- gegangene, aber noch nicht quittierte Meldungen;
- abgeschlossene Verlaufsereignisse;
- UI-Skalierung, Launcherposition und Fenstergrößen.

Ist eine Konfigurationsdatei beschädigt, legt UNMA eine Sicherung an und ersetzt
sie durch sichere Vorgaben.

UNMA kann aus einem vorhandenen Spielstand entfernt werden, da die Mod keine
physischen Spiel-Entities hinzufügt. Die Mod nur bei geschlossenem Spiel
entfernen.

## Messpult: mehrere Lager überwachen

1. Öffne im Spiel den Inspector des ersten Lagers.
2. Öffne UNMA mit **F8** und wechsle zu **MESSPULT**.
3. Wähle **QUELLE AUS GEÖFFNETEM GEBÄUDE**.
4. Wähle beispielsweise **Füllstand**, gib den Skalenbereich `0` bis `100`
   an und schalte mit der Typ-Taste zum gewünschten Instrument.
5. Wähle **INSTRUMENT EINBAUEN** und wiederhole die Schritte für weitere
   Kohlelager.

Mit **+ PANEL** legst du weitere benannte Messpulte an. Die Typauswahl klappt
als Galerie mit Vorschau aller Instrumente auf. Wird ein Messpult entfernt,
verschiebt UNMA seine Instrumente auf ein verbleibendes Panel, statt sie zu
löschen.

Der kleine Pfeil öffnet die erste Quelle; das **X** baut nur das Instrument
aus. Mit **MELD.** entsteht eine Alarmregel direkt für diesen – auch
berechneten – Messwert. Der Editor zeigt dabei
**VERKNÜPFTE WERTE: Schildname** als Quelle. Über die Wertauswahl können auch
weitere Instrumente desselben Messpults als Bedingungen ergänzt werden.
**INSTRUMENT** ist dafür der dritte Quellenbutton neben Gebäudeauswahl und
globalen Variablen. Jede Instrumentmeldung besitzt eine eigene Zielauswahl:
Ein oder mehrere Meldungspanels können gewählt werden, wobei mindestens eines
ausgewählt bleiben muss. Es wird kein Panel mehr stillschweigend übernommen. Für
Papierschreiber öffnet **ARCHIV** eine große
Historienansicht für einen Spieltag, einen Spielmonat, ein Spieljahr, zehn
Jahre, ein Jahrhundert oder die gesamte noch gespeicherte Laufzeit. Der
Schreiber läuft von links nach rechts; neue Werte
werden nicht in den vorhandenen Ausschnitt zusammengedrückt.

Über **QUELLE HINZUFÜGEN** können weitere geöffnete Gebäude mit derselben
Messgröße in ein Instrument aufgenommen werden. Als Berechnung stehen
**EINZEL**, **SUMME**, **MITTEL**, **MIN** und **MAX** bereit. In der zugehörigen
Meldung kann entweder **WERT**, **RÜCKGANG**, **ZUNAHME** oder **HALTEN**
gewählt werden. Rückgang und Zunahme arbeiten wahlweise mit Betrag oder
Prozent. HALTEN fordert, dass Operator und Sollwert während des kompletten
Zeitraums erfüllt bleiben. Zeiträume werden in Spieltagen, -monaten, -jahren,
Jahrzehnten oder Jahrhunderten angegeben und berücksichtigen Pause sowie
Spielgeschwindigkeit. Eine einzige Verletzung startet HALTEN neu.

Profilanzeigen eignen sich besonders für lange Reihen. CRT und
Papierschreiber zeigen zusätzlich einen kontinuierlich verbundenen
Kurzzeittrend.

## Fehlerbehebung

- **UNMA wird nicht geladen:** Prüfen, ob UNMA und MultiLangLib installiert,
  aktiviert und mit Captain of Industry 0.8.6c kompatibel sind.
- **Der Launcher fehlt:** **F8** drücken. Bei geöffnetem Hauptfenster ist der
  Launcher absichtlich ausgeblendet.
- **Eine Meldung fehlt in HOME:** HOME zeigt nur aktuell aktive Meldungen.
  Außerdem unter **MELDUNGSOPTIONEN** prüfen, ob sie ausgeblendet oder komplett
  ignoriert wird.
- **Eine bekannte Vanilla-Meldung fehlt in der Liste:** Einige Arten werden
  erst zur Laufzeit entdeckt. Im passenden Objektpanel werden registrierte
  potenzielle Meldungen bereits vor dem Auftreten angezeigt, sofern das Spiel
  sie zur Verfügung stellt.
- **Die Auswahl globaler Variablen schließt sich sofort:** Auf UNMA 0.9.11 oder
  neuer aktualisieren.
- **Ein Produktmesswert fehlt:** Das Objekt das Produkt einmal verarbeiten
  lassen und danach die Quellauswahl neu übernehmen.
- **Eine Bedingung zeigt eine fehlende Quelle:** Das Objekt wurde möglicherweise
  entfernt, sein Prototyp hat sich geändert oder ein Providerwert ist nicht mehr
  verfügbar.
- **Eine Prozentregel löst nie aus:** Prüfen, ob der Bezugswert vorhanden und
  größer als null ist.
- **Ein eigener Ton fehlt:** Dateiformat und Ablageort prüfen und das Spiel neu
  starten.
- **Ein Entwurf wurde nicht ersetzt:** Das ist beabsichtigt. Den bestehenden
  Entwurf zuerst speichern, verwerfen oder leeren.

## Aktuelle Grenzen

- Logistikzonen, Designationen und abstrakte Routen sind keine auswählbaren
  Spiel-Entities und können daher nicht direkt als Objektquelle dienen.
- Ein vollständig unbenutztes, leeres Mehrproduktobjekt bietet einzelne
  Produkte möglicherweise erst an, nachdem eines vorhanden war.
- Transportkapazität beschreibt den momentanen Inhaltsraum, nicht den Durchsatz
  pro Zeit.
- Abgekoppelte Panels bleiben innerhalb der Spieloberfläche. Native Fenster auf
  weiteren Monitoren erfordern eine separate Begleitanwendung.

## Links

- [UNMA-Releases](https://github.com/max2605/UNMA/releases)
- [Externe Mod-API](https://github.com/max2605/UNMA/blob/main/docs/external-mod-api.md)
- [Provider-Integrations-Schnellstart](https://github.com/max2605/UNMA/blob/main/docs/provider-integration.de.md)
