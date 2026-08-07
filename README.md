# UNMA – Universelle Nachrichten-Meldeanlage

UNMA ergänzt Captain of Industry um eine frei konfigurierbare industrielle
Schlitzmelder-Tafel. Das Vorbild ist die klassische Meldeanlage: im Ruhezustand
hellgrau mit schwarzer Schrift, beim Kommen blinkend in Aktivfarbe und nach
`MASTER QUIT` dauerhaft stehend und stumm. Geht eine noch ungequittierte
Meldung, bleibt sie als `GEGANGEN · UNQUITTIERT` gespeichert; nur der Bediener
oder eine ausdrücklich gewählte automatische Quittierung setzt sie zurück.

Zielversion: Captain of Industry **0.8.6c**.

## Funktionen

- Spiegelung aller aktiven Vanilla-Benachrichtigungen über den
  `INotificationsManager`.
- Das Home-Dashboard zeigt ausschließlich aktuell anstehende Meldungen (`K` und
  `KQ`) aus allen Quellen. Normale, gegangene und leere Plätze werden dort
  vollständig ausgeblendet.
- Dauerhaft feste Meldeschlitze je Fachpanel: Eine stabile Alarm-ID behält ihren
  Platz auch zwischen `NORMAL`, `KOMMT`, `STEHT` und `GEGANGEN`. Wiederholte
  Vanilla-Ereignisse derselben Meldungsart und Entität erscheinen genau einmal;
  einzelne K/G/Q-Ereignisse bleiben im Verlauf vollständig erhalten.
- `HomelessLeft` bleibt bei negativem Bevölkerungs-`+/-` auch über den
  Monatswechsel hinweg dieselbe stehende Meldung. Wechselnde Personenzahlen
  aktualisieren nur den Text; erst ein Wert von `0` oder größer setzt sie auf
  gegangen und erlaubt danach ein wirklich neues Ereignis.
- Drei automatische Kernüberwachungen:
  - Gesundheit: `10` ist neutral; Krankheit und Pollution/Müll werden getrennt
    ausgewertet und mit der prozentualen Arbeitsreserve verknüpft;
  - Nahrung: 12 Monate, 3 Monate, Hunger/Todesfälle;
  - Arbeiter: prozentuale freie beziehungsweise fehlende Arbeitsreserve.
- Die mitgelieferten Systemmeldungen sind im **SYSTEM**-Tab vollständig und
  dauerhaft editierbar: Aktivierung, Text, Messwerte, Operatoren, Schwellen,
  Alarmstufe, Farbe und Ton.
- Die Werkvorgaben verwenden **NOTFALL** nur für eine aktive Hunger- oder
  Gesundheitstodesspirale. Dabei nutzt UNMA die vom Spiel bereits gerundete und
  mit positiven Geburten-/Ediktbeiträgen verrechnete Nettoentwicklung. Ein
  reiner Arbeitermangel eskaliert höchstens auf
  kritisch.
- Klingel für Warnungen, Industriehorn für kritische Zustände und eine E57-nahe
  Motorsirene für Notfälle. Ihr kräftigerer Lauf steigt zwei Sekunden an und
  fällt zwei Sekunden ab. Das tiefe Industriehorn tönt 3,2 Sekunden und macht
  anschließend 1,2 Sekunden Pause.
- Zusätzliche synthetische Sinus-, Rechteck-, Sägezahn-, Dreieck- und
  Impulssignale. Alle eingebauten Signale entstehen mathematisch zur Laufzeit.
- Eigene `.wav`- und `.ogg`-Dateien aus dem Ordner `Sounds`.
- Eigene Tonzuordnung und frei wählbare automatische Quittierung je bekannter
  Vanilla- und Systemmeldung; eigene Regeln wählen Ton, Alarmstufe,
  Aktivfarbe und Quittierverhalten direkt im Editor.
- Goldene UNMA-Alarmglocke direkt im Inspector jeder unterstützten Entität;
  ein Klick fügt das Objekt hinzu und öffnet die Meldetafel im sichtbaren
  Zuweisungsmodus.
- Auf einem Fachpanel verknüpft ein Klick auf eine vorhandene eigene Meldung das
  Objekt mit deren Bedingungen. Das hervorgehobene `+ NEUE MELDUNG`-Karree
  startet stattdessen einen neuen Alarm an genau diesem festen Panelplatz.
  HOME dient nur als aktive Übersicht und nimmt keine festen Zuordnungen auf.
- AWL-artige Bedingungstabelle mit sichtbarem Ist-Wert, verständlicher Kennung,
  allen sechs Vergleichszeichen, Soll-Wert und UND-/ODER-Verknüpfung.
- Absolute Schwellen und `% VON` einem wählbaren Bezugs-Messwert. Kapazitäten
  werden für Lager, Förderer/Rohre und Fahrzeugfracht automatisch empfohlen.
- Automatische Erkennung öffentlicher numerischer und boolescher Messwerte von
  Gebäuden, Lagern, Fahrzeugen, Rohren und Förderbändern.
- Offene Erweiterungsschnittstelle für andere Mods: aktive Provider können
  automatische Alarmvorlagen in `UNMA/*.json` mitliefern, eigene Messwert-
  Reader über die versionierte C#-API registrieren oder Alarmzustände direkt
  veröffentlichen. `aggregate` erzeugt einen festen Sammelschlitz,
  `per_entity` auf Wunsch je Instanz einen stabilen Schlitz.
- LangLib-Grundanbindung für die UNMA-Fensterhülle und alle Providertexte. In Definitionen
  bleiben stabile Übersetzungsschlüssel gespeichert; ein verpflichtender
  Fallback verhindert sichtbare Schlüssel bei fehlenden Sprachdateien.
- Typisierte Produktmengen für Lager, Förderbänder/Rohre sowie Frachten von
  Trucks, Baggern, Tree Plantern, Tree Harvestern und Güterwaggons.
- Mehrere Bedingungen pro Sammelmeldung mit UND- oder ODER-Verknüpfung.
- Eigene Meldungen lassen sich nach dem Speichern erneut in den Editor laden
  und vollständig ändern.
- Ein Doppelklick auf einen eigenen Meldeschlitz öffnet dessen Regel direkt im
  Editor; ungespeicherte andere Entwürfe werden dabei nicht überschrieben.
- Mehrprodukt-Gebäude wie Lebensmittelmärkte bieten jeden zugewiesenen Artikel
  als verständlichen Messwert an, beispielsweise `Kartoffeln · Bestand`.
  Bedingungen können absolut (`< 400`) oder automatisch relativ zur summierten
  Produktkapazität (`< 50 %`) definiert werden.
- Frei wählbare Meldetexte, Alarmstufen, Aktivfarben und Töne.
- Beliebig viele Panels mit klar getrennter Auswahl, Bearbeitung und Neuanlage,
  Spaltenzahl, festen hoch/runter sortierbaren Schlitzen, gezieltem Hinzufügen
  bekannter Meldungen sowie Vanilla-/System-Automatik und kommagetrennten
  Suchfiltern. Neu entdeckte passende Meldungsarten werden einmalig hinten
  angehängt und verschieben vorhandene Plätze nicht.
- Mehrere gleichzeitig abgekoppelte, verschiebbare In-Game-Tafeln.
- Der persistente **VERLAUF** führt jedes Alarmereignis mit `K` (gekommen),
  `KQ` (gekommen und quittiert), `KG` (gekommen und gegangen) oder `KGQ`
  (gekommen, gegangen und quittiert). `K` blinkt rot, `KG` blinkt mit
  schwarzer Schrift auf weißem Hintergrund; `KQ` und `KGQ` stehen schwarz auf
  weiß. Nur abgeschlossene `KGQ`-Einträge lassen sich ausdrücklich löschen.
- Spielstandsbezogene Persistenz in `unma-world-<GameId>.json`; Entity-Regeln
  werden zusätzlich gegen Typ, Prototyp und gegebenenfalls Produkt geprüft.
  Auch `STEHT`-Quittierungen und `GEGANGEN · UNQUITTIERT` überleben Speichern
  und Neuladen.
- Beim endgültigen Abriss beziehungsweise Zerstören einer überwachten Entity
  löscht UNMA deren eigene Regel samt festem Schlitz und aktivem Zustand
  automatisch. Bei einer Sammelmeldung wird die ganze UND-/ODER-Regel entfernt,
  damit ihre Logik nicht still verändert wird. Historische KG/KGQ-Einträge
  bleiben im Verlauf; temporär despawnte Fahrzeuge werden nicht gelöscht.
  Beschädigte Konfigurationen werden gesichert und durch sichere Standardwerte
  ersetzt.

## Bedienung

1. Mod im Spiel aktivieren und einen Spielstand laden.
2. UNMA mit `F8` oder dem kompakten Launcher am linken Rand öffnen.
   Die Schaltfläche erscheint nur bei geschlossener Zentrale und lässt sich am
   `↕`-Griff aus anderen HUD-Bereichen herausziehen; ihre Position wird
   gespeichert.
3. In **MELDETAFEL** zeigt **HOME** alle aktiven Meldungen. Für die dauerhaft
   definierte Schlitztafel ein Fachpanel wählen.
4. `MASTER QUIT · QUITTIEREN` quittiert alle kommenden und bereits gegangenen
   Meldungen und stoppt deren Ton. Bei einer weiterhin anstehenden Meldung
   bleibt die Aktivfarbe sichtbar, bis die Ursache verschwindet.
5. In **VERLAUF** zeigt eine eigene Zeile je Alarmereignis den Zustand `K`,
   `KQ`, `KG` oder `KGQ`. Vollständig abgeschlossene `KGQ`-Zeilen bleiben
   gespeichert, bis sie dort ausdrücklich gelöscht werden.
6. Für eine eigene Meldung die Entität anklicken und im geöffneten Inspector
   die goldene UNMA-Glocke drücken. UNMA merkt Name, ID und Messwerte und zeigt
   auf der **MELDETAFEL** dauerhaft den Zuweisungsmodus mit Abbruchknopf.
7. Eine vorhandene **eigene** Meldung anklicken, um das Objekt als weitere
   Bedingung zu verknüpfen. Für eine neue Meldung das hervorgehobene
   `+ NEUE MELDUNG`-Karree anklicken; nach dem Speichern wird daraus an dieser
   Stelle ein fester Schlitz.
8. Im geöffneten Objekt-Alarmfenster Kennung und eines der sechs
   Steuerzeichen wählen, Soll-Wert eingeben und die AWL-Zeile hinzufügen. Für
   relative Bedingungen `% VON` und anschließend den Bezugs-Messwert wählen.
   Die Meldung zunächst speichern, danach für Sammelmeldungen weitere Entitäten
   ebenso hinzufügen und global UND oder ODER festlegen. Der **EDITOR** bietet
   denselben Ablauf sowie die Panelpflege.
9. Unter **TÖNE** können Ton und Verhalten beim Gehen für jede bereits
   bekannte Vanilla-Meldung separat festgelegt werden.
10. Unter **SYSTEM** können Gesundheit, Nahrung und Arbeiter einschließlich
   ihrer Warn-, Kritisch- und Todesspiralenbedingungen jederzeit angepasst
   oder auf die Werkvorgabe zurückgesetzt werden.

Die Gesundheitsanzeige des Spiels ist keine klassische 0–100-%-Skala:
`10` ist der neutrale Basiswert und erst unter `0` entsteht ein
gesundheitsbedingter Bevölkerungsverlust. UNMA verwendet deshalb den
abgeschlossenen Monatswert. Eine zeitlich begrenzte Krankheit mit großer
Arbeitsreserve bleibt niedriger priorisiert; dauerhafte Pollution/Müll unter
der Verlustgrenze oder eine Krankheit, deren erwarteter Nettoverlust die freie
Arbeitsreserve kurzfristig aufbraucht, gilt als Todesspirale.

Ein Lager kann beispielsweise über `Lagerinhalt = 0` überwacht und mit der
produktbezogenen Menge auf einem Förderband über UND verknüpft werden. Bei
Transporten stehen Gesamtmenge, theoretischer Inhaltsraum, Füllstand und die
aktuell vorkommenden Produkte zur Wahl. Gespeicherte Produktbedingungen lesen
auch nach dem Leerlaufen weiterhin korrekt `0`.

Für `Lagerinhalt % VON Lagerkapazität < 5` berechnet UNMA beispielsweise den
vergleichbaren Wert als `Istwert / Bezugswert × 100`. Ein fehlender, null oder
negativer Bezugswert erfüllt die Bedingung bewusst nicht und wird als nicht
berechenbar angezeigt; Werte über 100 % werden nicht künstlich begrenzt.

## API für andere Mods

UNMA lädt ausschließlich Definitionen tatsächlich aktiver Mods aus
`<Provider-Mod>/UNMA/*.json`. Eine defekte Datei wird isoliert protokolliert
und kann weder andere Provider noch den Spielstart blockieren. Erweiterte
Messwerte und direkt veröffentlichte Zustände stehen über
`UNMA.Api.UnmaApi` mit `ApiVersion = 1` bereit.

Der vollständige Vertrag, das JSON-Schema und ein lauffähiges Providerbeispiel
stehen in [docs/external-mod-api.md](docs/external-mod-api.md),
[docs/unma-extension-v1.schema.json](docs/unma-extension-v1.schema.json) und
[examples/ProviderMod](examples/ProviderMod).

## Audio und Lizenzen

Eigene Dateien kommen nach:

```text
UNMA/Sounds/mein-alarm.wav
UNMA/Sounds/mein-alarm.ogg
```

Technisch unterstützt werden PCM-WAV und Ogg Vorbis. Ein Dateiformat macht den
Inhalt nicht automatisch lizenzfrei. Verwende eigene Aufnahmen, CC0-Material
oder Dateien mit einer Lizenz, die Weitergabe und Nutzung erlaubt.

## Grenzen der ersten Version

- Physische Straßen, Gebäude, Fahrzeuge und Transporte sind Entities und damit
  auswählbar. Logistikzonen, Designationen und abstrakte Routen sind in der
  Spiel-API dagegen keine anklickbaren `IEntity`-Objekte; dafür ist später ein
  eigener Zonen-/Routen-Picker nötig.
- Produktzeilen werden beim Anlegen für Produkte angeboten, die sich gerade im
  Lager, Transport oder Fahrzeug befinden. Ein völlig leeres, noch nie
  benutztes Objekt bietet zunächst nur Gesamtmenge und Füllstand an.
- Die Transportkapazität beschreibt den momentanen Inhaltsraum, nicht den
  Durchsatz pro Zeit.
- Vanilla- und Systemmeldungen besitzen eigene feste Auswertungsmodelle und
  nehmen deshalb keine Entity-Bedingungen direkt auf. Zum Verknüpfen wird eine
  eigene Meldung beziehungsweise das freie Plus-Karree verwendet.
- Abgekoppelte Panels bleiben innerhalb des Hauptfensters. Die offizielle
  Mod-API stellt keine beliebig verschiebbaren nativen Betriebssystemfenster
  für Monitor 2, 3 usw. bereit. Das wäre ein separates Companion-Projekt mit
  IPC und eigener Sicherheits-/Kompatibilitätsprüfung.

## Bauen

Voraussetzungen:

- Captain of Industry 0.8.6c;
- LangLib 0.1.0 oder neuer als aktivierte Mod-Abhängigkeit;
- .NET Framework 4.8 Reference Assemblies;
- Visual Studio Build Tools 2022 oder `dotnet` mit passenden Referenzen.

```powershell
$env:COI_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry'
dotnet build .\source\UNMA.csproj -c Release
```

Der Release-Build kopiert `UNMA.dll` automatisch in den Mod-Stammordner.

## Lizenz

Der Quellcode und die mathematischen Tongeneratoren stehen unter der MIT-Lizenz.
Captain of Industry und seine Assets sind Eigentum von MaFi Games.
