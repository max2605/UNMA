# Changelog

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
