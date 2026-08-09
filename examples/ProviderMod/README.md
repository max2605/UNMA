# ExampleProvider

Dieses Verzeichnis zeigt beide UNMA-Extension-V1-Wege in einem kleinen
Provider-Mod:

- `UNMA/alarms.json` definiert zwei automatisch ausgewertete Meldungen.
- `lang/*.json` liefert die Texte über MultiLangLib in allen 21 aktuell von COI
  ausgelieferten Locales; `en.json` bleibt der vollständige Fallback.
- `UnmaIntegration.cs` registriert einen mod-eigenen Messwert, eine
  automatische C#-Vorlage und eine programmgesteuerte Push-Meldung.
- `ProviderMod.ApiSample.csproj` kompiliert den isolierten C#-Beispielcode
  gegen das aktuelle UNMA-Projekt.
- `manifest.example.json` zeigt die direkte Abhängigkeit für die C#-API.

Für einen reinen JSON-Provider entfallen `UnmaIntegration.cs`, die
UNMA-Assemblyreferenz und die direkte `UNMA>=0.8.0`-Abhängigkeit. Die
Sprachdateien bleiben bestehen, weil UNMA sie über seine eigene
MultiLangLib-Abhängigkeit lädt.

Die Typen `ProviderTankEntity` und `ProviderPumpEntity` im C#-Beispiel sind
bewusst Platzhalter. Ein echter Provider ersetzt sie durch seine eigenen
Entitätstypen und verwendet deren dauerhaft gespeicherte ID als
`entityStableId`.

Siehe [`docs/provider-integration.de.md`](../../docs/provider-integration.de.md)
für den deutschsprachigen Schnellstart,
[`docs/external-mod-api.md`](../../docs/external-mod-api.md) für den
vollständigen Vertrag und
[`docs/unma-extension-v1.schema.json`](../../docs/unma-extension-v1.schema.json)
für die maschinenlesbare Validierung.
