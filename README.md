# UNMA – Universelle Nachrichten-Meldeanlage

UNMA ergänzt Captain of Industry um eine frei konfigurierbare industrielle
Schlitzmelder-Tafel. Das Vorbild ist die klassische Meldeanlage: im Ruhezustand
hellgrau mit schwarzer Schrift, beim Kommen blinkend in Aktivfarbe und nach
`MASTER QUIT` dauerhaft stehend und stumm.

Zielversion: Captain of Industry **0.8.6c**.

## Funktionen

- Spiegelung aller aktiven Vanilla-Benachrichtigungen über den
  `INotificationsManager`.
- Drei automatische Kernüberwachungen:
  - Gesundheit: Warnung, kritisch, Notfall;
  - Nahrung: 12 Monate, 3 Monate, Hunger/Todesfälle;
  - Arbeiter: Fehlbestand mit dynamischer Eskalation nach Bevölkerungsgröße.
- Klingel für Warnungen, Industriehorn für kritische Zustände und eine
  auf-/abschwellende E51-artige Sirene für Notfälle.
- Zusätzliche synthetische Sinus-, Rechteck-, Sägezahn-, Dreieck- und
  Impulssignale. Alle eingebauten Signale entstehen mathematisch zur Laufzeit.
- Eigene `.wav`- und `.ogg`-Dateien aus dem Ordner `Sounds`.
- Eigene Tonzuordnung je bekannter Vanilla- und Systemmeldung; eigene Regeln
  wählen Ton, Alarmstufe und Aktivfarbe direkt im Editor.
- Eigene Meldungen für die aktuell im Spiel inspizierte Entität.
- Automatische Erkennung öffentlicher numerischer und boolescher Messwerte von
  Gebäuden, Lagern, Fahrzeugen, Rohren und Förderbändern.
- Typisierte Produktmengen für Lager, Förderbänder/Rohre sowie Frachten von
  Trucks, Baggern, Tree Plantern, Tree Harvestern und Güterwaggons.
- Mehrere Bedingungen pro Sammelmeldung mit UND- oder ODER-Verknüpfung.
- Frei wählbare Meldetexte, Alarmstufen, Aktivfarben und Töne.
- Beliebig viele Panels, Spaltenzahl, Vanilla-/Systemfilter und
  kommagetrennte Suchfilter.
- Mehrere gleichzeitig abgekoppelte, verschiebbare In-Game-Tafeln.
- Spielstandsbezogene Persistenz in `unma-world-<GameId>.json`; Entity-Regeln
  werden zusätzlich gegen Typ, Prototyp und gegebenenfalls Produkt geprüft.
  Beschädigte Konfigurationen werden gesichert und durch sichere Standardwerte
  ersetzt.

## Bedienung

1. Mod im Spiel aktivieren und einen Spielstand laden.
2. UNMA mit `F8` oder dem kompakten Launcher am linken Rand öffnen.
   Die Schaltfläche erscheint nur bei geschlossener Zentrale und lässt sich am
   `↕`-Griff aus anderen HUD-Bereichen herausziehen; ihre Position wird
   gespeichert.
3. In **MELDETAFEL** ein Panel wählen und aktuelle Meldungen beobachten.
4. `MASTER QUIT · QUITTIEREN` stoppt den Ton aller aktuell anstehenden
   Meldungen. Die Aktivfarbe bleibt sichtbar, bis die Ursache verschwindet.
5. Für eine eigene Meldung zuerst eine Entität im Spiel anklicken und deren
   Inspector offen lassen.
6. In **EDITOR** die aktuelle Spiel-Auswahl übernehmen, Messwert, Operator und
   Schwelle wählen und die Bedingung hinzufügen.
7. Für eine Sammelmeldung weitere Entitäten nacheinander auswählen und
   Bedingungen ergänzen. Danach UND/ODER, Stufe, Farbe und Ton festlegen.
8. Unter **MELDUNGSTÖNE** kann der Ton jeder bereits bekannten Vanilla- und
   Systemmeldung separat überschrieben oder auf lautlos gestellt werden.

Ein Lager kann beispielsweise über `Lagerinhalt = 0` überwacht und mit der
produktbezogenen Menge auf einem Förderband über UND verknüpft werden. Bei
Transporten stehen Gesamtmenge, theoretischer Inhaltsraum, Füllstand und die
aktuell vorkommenden Produkte zur Wahl. Gespeicherte Produktbedingungen lesen
auch nach dem Leerlaufen weiterhin korrekt `0`.

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
- Abgekoppelte Panels bleiben innerhalb des Hauptfensters. Die offizielle
  Mod-API stellt keine beliebig verschiebbaren nativen Betriebssystemfenster
  für Monitor 2, 3 usw. bereit. Das wäre ein separates Companion-Projekt mit
  IPC und eigener Sicherheits-/Kompatibilitätsprüfung.

## Bauen

Voraussetzungen:

- Captain of Industry 0.8.6c;
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
