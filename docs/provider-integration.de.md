# Mehrsprachige UNMA-Meldungen in einem eigenen Mod

Diese Anleitung zeigt, wie ein Captain-of-Industry-Mod eigene Meldungen für
UNMA bereitstellt und die sichtbaren Texte mit MultiLangLib in allen aktuell
ausgelieferten Spielsprachen anbietet. Sie ist der praxisorientierte
Schnellstart. Der vollständige V1-Vertrag steht in
[external-mod-api.md](external-mod-api.md), das maschinenlesbare Schema in
[unma-extension-v1.schema.json](unma-extension-v1.schema.json).

Stand dieser Sprachliste: **8. August 2026**, Captain of Industry **0.8.6c**.
Die [offizielle Steam-Seite](https://store.steampowered.com/app/1594320/Captain_of_Industry/)
nennt 20 Oberflächensprachen. Die aktuelle Spielinstallation liefert außerdem
bereits `nb_NO.json` für Norwegisch (Bokmål) aus. Provider sollten deshalb alle
21 unten aufgeführten Dateinamen unterstützen.

## Welcher Integrationsweg passt?

| Anforderung | Empfohlener Weg |
|---|---|
| Vorhandenen UNMA-Messwert mit einem Grenzwert überwachen | `UNMA/*.json`, kein C# nötig |
| Eigenen oder berechneten Messwert einer Mod-Entität lesen | C# `RegisterMetric` und danach JSON- oder C#-Vorlage |
| Der Provider kennt den Alarmzustand bereits selbst | C# `PublishAlarmState` beziehungsweise `PublishAlarmStates` |

Ein JSON-Provider kann optional mit UNMA zusammenarbeiten und die Abhängigkeit
weglassen; ohne aktives UNMA bleibt sein `UNMA`-Ordner dann einfach ungenutzt.
Sobald eigener Code Typen aus `UNMA.dll` verwendet, ist die deklarierte
UNMA-Abhängigkeit zwingend.

## Wichtig: UNMA nicht in den eigenen Mod kopieren

UNMA und MultiLangLib bleiben eigenständige Mods. Ein Provider kopiert weder
`UNMA.dll` noch `MultiLangLib.dll` in sein Release. Er legt nur seine Definitionen
und Übersetzungen im eigenen Modverzeichnis ab.

Wenn UNMA für den Provider zwingend erforderlich ist, enthält dessen
`manifest.json`:

```json
"mod_dependencies": [ "UNMA>=0.8.0" ]
```

V1 ist seit UNMA 0.8.0 verfügbar. Ein reiner JSON-Provider muss MultiLangLib nicht
zusätzlich als direkte Abhängigkeit angeben: UNMA bringt diese Abhängigkeit mit
und registriert den Sprachordner des aktiven Providers. Ruft der Provider in
seinem eigenen C#-Code selbst `Lang.Get`, `Lang.Format` oder `Lang.Localized`
auf, muss er zusätzlich `MultiLangLib>=0.1.0` deklarieren.

## Empfohlene Verzeichnisstruktur

```text
MyProvider/
|-- manifest.json
|-- MyProvider.dll
|-- UNMA/
|   `-- alarms.json
`-- lang/
    |-- ca.json
    |-- cs.json
    |-- de.json
    |-- en.json
    |-- es.json
    |-- et.json
    |-- fr.json
    |-- hu.json
    |-- it.json
    |-- ja.json
    |-- ko.json
    |-- nb_NO.json
    |-- nl.json
    |-- pl.json
    |-- pt_BR.json
    |-- ru.json
    |-- sv.json
    |-- tr.json
    |-- uk.json
    |-- zh_Hans.json
    `-- zh_Hant.json
```

Der Ordnername `UNMA` ist Teil des V1-Vertrags und muss genau so geschrieben
werden. UNMA liest nur `*.json` direkt in diesem Ordner; Unterordner werden
nicht durchsucht. Die Sprachdateinamen müssen besonders bei `nb_NO`, `pt_BR`,
`zh_Hans` und `zh_Hant` exakt übernommen werden.

## 1. Eine Meldung definieren

`MyProvider/UNMA/alarms.json`:

```json
{
  "schema_version": 1,
  "mod_id": "MyProvider",
  "alarms": [
    {
      "id": "storage-low",
      "prototype_ids": [ "MyProvider_StorageT1" ],
      "scope": "aggregate",
      "panel_id": "supply",
      "localization_namespace": "MyProvider",
      "message_key": "multilanglib.MyProvider.alarm.storage_low",
      "message_fallback": "STORAGE LEVEL LOW",
      "detail_key": "multilanglib.MyProvider.alarm.storage_low.detail",
      "detail_fallback": "At least one storage unit is below 15 percent.",
      "severity": "warning",
      "sound_id": "bell",
      "auto_acknowledge_on_clear": false,
      "logic": "all",
      "conditions": [
        {
          "metric": "$stored.quantity",
          "label_key": "multilanglib.MyProvider.metric.storage_fill",
          "label_fallback": "Storage fill level",
          "operator": "<",
          "threshold": 15,
          "value_mode": "percent_of_reference",
          "reference_metric": "$stored.capacity",
          "reference_label_key": "multilanglib.MyProvider.metric.storage_capacity",
          "reference_label_fallback": "Storage capacity"
        }
      ]
    }
  ]
}
```

`$schema` ist optional. Wer Editorvalidierung möchte, kopiert das mit UNMA
gelieferte Schema in sein Quellrepository und trägt einen für dieses
Repository gültigen relativen Pfad ein. Ein relativer Pfad aus dem
UNMA-Beispiel darf nicht unverändert in ein anderes Projekt übernommen werden.

`mod_id` muss exakt der ID im umgebenden Mod-Manifest entsprechen. `id` ist
die dauerhafte fachliche Identität der Meldung und darf nach einer
Veröffentlichung nicht wegen eines neuen Texts oder Messwerts geändert werden.
Andernfalls behandelt UNMA sie als neue Meldung: Auf einem dauerhaften
Fachpanel entsteht ein neuer Schlitz und bei aktiver Bedingung ein neues
Ereignis. Das Home-Panel `main` ist dagegen ein dynamisches Dashboard und
besitzt grundsätzlich keine festen Leerschlitze.

`aggregate` erzeugt ein gemeinsames Vorkommnis für alle passenden Entitäten;
auf einem Fachpanel wie `supply` belegt es genau einen festen Schlitz.
`per_entity` erzeugt bewusst ein Vorkommnis pro stabiler Entitäts-ID und sollte
nur verwendet werden, wenn diese Menge überschaubar bleibt. Ein unbekanntes
`panel_id` fällt sicher auf das Dashboard beziehungsweise das erste Panel
zurück; benutzerdefinierte Panel-IDs eines Spielers sind für einen Provider
nicht portabel vorhersagbar.

## 2. MultiLangLib-Schlüssel anlegen

Ein kanonischer Schlüssel besteht aus:

```text
multilanglib.<Namensraum>.<Text-ID>
```

Im Beispiel ist der vollständige Schlüssel
`multilanglib.MyProvider.alarm.storage_low`. In der Sprachdatei selbst steht nur
die kurze Text-ID.

`MyProvider/lang/en.json`:

```json
{
  "alarm.storage_low": "STORAGE LEVEL LOW",
  "alarm.storage_low.detail": "At least one storage unit is below 15 percent.",
  "metric.storage_fill": "Storage fill level",
  "metric.storage_capacity": "Storage capacity"
}
```

`MyProvider/lang/de.json`:

```json
{
  "alarm.storage_low": "LAGERVORRAT NIEDRIG",
  "alarm.storage_low.detail": "Mindestens ein Lager liegt unter 15 Prozent.",
  "metric.storage_fill": "Lagerfüllstand",
  "metric.storage_capacity": "Lagerkapazität"
}
```

Die Dateien sind flache UTF-8-JSON-Objekte. Alternativ akzeptiert MultiLangLib das
COI-Arrayformat, für neue Mods ist das Objektformat leichter zu pflegen.

Regeln für Schlüssel:

- Der Namensraum beginnt mit einem ASCII-Buchstaben oder einer Ziffer und
  enthält nur Buchstaben, Ziffern, `_` oder `-`; Punkte sind dort verboten.
- Die Text-ID darf zusätzlich Punkte enthalten.
- Groß- und Kleinschreibung ist relevant.
- Mindestens `message_key` oder `message_fallback` muss vorhanden sein. Die
  Detail- und Label-Schlüssel sind optional.
- Wenn ein `*_key` gesetzt wird, muss er vollständig angegeben werden und mit
  `multilanglib.<localization_namespace>.` beginnen.
- Jeder sichtbare Schlüssel sollte zusätzlich einen lesbaren englischen
  `*_fallback` besitzen. Eine fehlende Übersetzung blockiert dann weder die
  Meldung noch den Spielstart.
- Platzhalter wie `{0}` oder `{1}` müssen in jeder Sprache unverändert und
  vollständig erhalten bleiben.

Enthält die Manifest-ID Punkte, zum Beispiel `Acme.Mod`, kann sie nicht direkt
als MultiLangLib-Namensraum dienen. Die Vorlage setzt dann beispielsweise
`"localization_namespace": "Acme_Mod"`, und alle Schlüssel beginnen mit
`multilanglib.Acme_Mod.`. Dieser Alias muss eindeutig sein. Natürliche gültige
Namensräume aktiver Mods sind reserviert; Kollisionen werden ohne Übernahme
eines fremden Sprachverzeichnisses abgewiesen.

## 3. Unterstützte Locale-Dateien

| Datei | Sprache | Offiziell auf Steam gelistet |
|---|---|:---:|
| `ca.json` | Katalanisch | ja |
| `cs.json` | Tschechisch | ja |
| `de.json` | Deutsch | ja |
| `en.json` | Englisch | ja |
| `es.json` | Spanisch (Spanien) | ja |
| `et.json` | Estnisch | ja |
| `fr.json` | Französisch | ja |
| `hu.json` | Ungarisch | ja |
| `it.json` | Italienisch | ja |
| `ja.json` | Japanisch | ja |
| `ko.json` | Koreanisch | ja |
| `nb_NO.json` | Norwegisch (Bokmål) | noch nicht; im Spiel enthalten |
| `nl.json` | Niederländisch | ja |
| `pl.json` | Polnisch | ja |
| `pt_BR.json` | Portugiesisch (Brasilien) | ja |
| `ru.json` | Russisch | ja |
| `sv.json` | Schwedisch | ja |
| `tr.json` | Türkisch | ja |
| `uk.json` | Ukrainisch | ja |
| `zh_Hans.json` | Chinesisch (vereinfacht) | ja |
| `zh_Hant.json` | Chinesisch (traditionell) | ja |

MultiLangLib übernimmt im Automatikmodus den tatsächlichen Dateinamen aus
`LocalizationManager.CurrentLangInfo.FileName`. Die Suchreihenfolge ist:

1. exakte aktive Sprache, zum Beispiel `pt_BR.json`;
2. neutrale Variante, sofern vorhanden;
3. konfigurierte Fallbacksprache, standardmäßig `en`;
4. der jeweilige `*_fallback` aus der UNMA-Definition.

Bei jeder Sprachstufe sucht MultiLangLib zuerst unter `<Provider>/lang` und danach
optional im zentralen Fallbackordner `<MultiLangLib>/lang/<Namensraum>`.

So bleibt eine Meldung auch dann lesbar, wenn eine neue COI-Sprache erscheint,
bevor der Provider eine Übersetzung nachliefert.

## 4. MultiLangLib im eigenen C#-Code verwenden

Dieser Abschnitt ist nur nötig, wenn der Provider selbst MultiLangLib aufruft. Für
reine UNMA-JSON-Definitionen übernimmt UNMA die Registrierung.

Manifest:

```json
"mod_dependencies": [ "UNMA>=0.8.0", "MultiLangLib>=0.1.0" ]
```

Projektdatei:

```xml
<Reference Include="MultiLangLib">
  <HintPath>$(APPDATA)\Captain of Industry\Mods\MultiLangLib\MultiLangLib.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Einmal im Mod-Konstruktor registrieren:

```csharp
using MultiLangLib;

public MyProviderMod(ModManifest manifest)
{
    const string LocalizationNamespace = "MyProvider";
    Lang.RegisterMod(LocalizationNamespace, manifest.RootDirectoryPath);
}
```

Bei einer Manifest-ID ohne Punkte kann `manifest.Id` direkt als Namensraum
verwendet werden. Bei IDs wie `Acme.Mod` muss hier derselbe gültige Alias wie
in `localization_namespace` stehen.

Danach können Texte aufgelöst werden:

```csharp
string title = Lang.Get("multilanglib.MyProvider.alarm.storage_low");
string text = Lang.Format("multilanglib.MyProvider.status.items", itemCount);
```

Ein Consumer darf **niemals** `Lang.Configure(...)` aufrufen. Sprache,
Fallback und Cache gehören global dem MultiLangLib-Mod.

## 5. Erweiterte C#-UNMA-API

Eigene berechnete Messwerte, C#-Vorlagen und direkt veröffentlichte Zustände
verwenden `UNMA.Api.UnmaApi` mit `ApiVersion = 1`. Die Buildreferenz bleibt
nicht privat, damit keine zweite UNMA-Assembly verteilt wird:

```xml
<Reference Include="UNMA">
  <HintPath>$(APPDATA)\Captain of Industry\Mods\UNMA\UNMA.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Ein vollständiges, kompilierbares Beispiel liegt unter
[`examples/ProviderMod`](../examples/ProviderMod). Details zu
`RegisterMetric`, `RegisterAlarmTemplate`, `PublishAlarmState`, Grenzen und
Fehlerisolation enthält die [V1-Referenz](external-mod-api.md).

Jeder `UnmaApi`-Aufruf verwendet als `ownerModId` exakt die Manifest-ID des
aufrufenden Providers, nicht den optionalen MultiLangLib-Alias. Eigene Messwertleser
werden vor allen Vorlagen registriert, die sie verwenden. Doppelte IDs
überschreiben bestehende Registrierungen nicht, sondern liefern `false`; ein
bewusster Ersatz meldet zuerst die alte Registrierung ab. Beim Entladen oder
vollständigen Neuaufbau räumt `UnregisterOwner(manifest.Id)` alle
Registrierungen dieses Providers auf.

Provider veröffentlichen nur `Active = true` oder `Active = false`. UNMA
besitzt die Zustände KOMMT/GEHT/QUITTIERT, die Blinklogik, Töne und MASTER
QUIT. Wiederholtes Veröffentlichen desselben aktiven Zustands darf keine neue
Meldungs-ID verwenden.

Beim Gehen veröffentlicht der Provider dieselbe stabile Kombination aus
`ownerModId`, `Id` und `InstanceId` zuerst mit `Active = false`. Erst nachdem
UNMA diesen Übergang ausgewertet hat, darf der Zustand optional mit
`RemoveAlarmState` entfernt werden. `PublishAlarmStates` ist ein atomarer
Update-Batch und kein vollständiger Replace-Snapshot: Nicht mitgesendete
Zustände bleiben registriert.

## 6. Im Spiel prüfen

1. Provider, UNMA und MultiLangLib im Mod-Manager aktivieren.
2. Spiel starten und in UNMA **OPTIONEN** öffnen.
3. **API / JSON / SPRACHE NEU LADEN** drücken.
4. Provider-, Datei-, Alarm- und Diagnosezähler kontrollieren.
5. MultiLangLib testweise mit `language_override` auf mehrere Locale-Codes stellen
   oder `debug_language=true` verwenden, um vollständige Schlüssel sichtbar zu
   machen.
6. Eine Bedingung aktivieren, MASTER QUIT prüfen, Bedingung gehen lassen und
   erst danach ein erneutes KOMMT testen.

## Release-Checkliste

- `mod_id` entspricht exakt der Manifest-ID.
- Alarm-, Messwert-, Prototyp- und Instanz-IDs bleiben über Releases stabil.
- Alle 21 Sprachdateien enthalten dieselben Schlüssel wie `en.json`.
- Alle Dateien sind gültiges UTF-8-JSON ohne Kommentare.
- Platzhaltermenge und Platzhalternummern entsprechen in jeder Sprache
  `en.json`.
- Jeder sichtbare Schlüssel besitzt einen verständlichen englischen Fallback.
- `UNMA.dll` und `MultiLangLib.dll` werden nicht mit dem Provider ausgeliefert.
- C#-Referenzen verwenden `<Private>false</Private>`.
- JSON wird gegen das mitgelieferte V1-Schema geprüft.
- Eine fehlende Übersetzung und ein nicht verfügbarer Messwert wurden als
  sichere Fehlerfälle getestet.
