# Changelog

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
- Synthetische Klingel, Industriehorn, E51-artige Sirene und Oszillatortöne.
- Import eigener PCM-WAV- und Ogg-Vorbis-Dateien.
- Mehrere abkoppelbare In-Game-Panels.
- Individuelle Ton-Overrides für Vanilla- und Systemmeldungen.
- Spielstandsbezogene Zustandsdateien mit Typ-/Prototypvalidierung.
- Typisierte Produktadapter für Förderbänder, Rohre und Fahrzeugfracht.
- Simulationssichere Auswertung über `UpdateEndForUi` und virtualisierte
  Darstellung großer Meldetafeln.
