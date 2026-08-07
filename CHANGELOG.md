# Changelog

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
