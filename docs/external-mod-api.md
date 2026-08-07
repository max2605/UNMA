# UNMA External Extension V1

UNMA 0.8.0 stellt anderen Captain-of-Industry-Mods zwei bewusst kleine
Integrationswege bereit:

1. **Deklarative Automatikmeldungen** aus `<ModRoot>/UNMA/*.json`. Dafür ist
   keine Referenz auf `UNMA.dll` erforderlich.
2. **Die öffentliche C#-API** `UNMA.Api.UnmaApi` für eigene Messwertleser,
   programmgesteuerte Meldungsvorlagen und Push-Meldungen.

Beide Wege verwenden LangLib für sichtbare Texte und dieselbe
Schlitzmelder-Zustandsmaschine wie die eingebauten UNMA-Meldungen. Eine
Integration darf deshalb den Alarmzustand liefern, aber niemals selbst `Q`
(quittiert) setzen. Die Quittierung gehört dem Spieler und UNMA.

## Schnellstart: Automatikmeldung ohne C#

Ein Provider-Mod mit der Manifest-ID `ExampleProvider` kann folgende Struktur
mitliefern:

```text
ExampleProvider/
|-- manifest.json
|-- ExampleProvider.dll
|-- UNMA/
|   `-- alarms.json
`-- lang/
    |-- de.json
    `-- en.json
```

`UNMA` liest beim Start sowie bei einem manuellen Neuladen alle `*.json`
direkt aus diesem Ordner. Unterordner gehören nicht zum V1-Vertrag. Der
vertragliche Ordnername ist exakt `UNMA`; diese Schreibweise funktioniert
auch auf Dateisystemen mit beachteter Groß-/Kleinschreibung.

Eine minimale Datei sieht so aus:

```json
{
  "schema_version": 1,
  "mod_id": "ExampleProvider",
  "alarms": [
    {
      "id": "storage-low",
      "prototype_ids": [ "ExampleProvider_StorageT1" ],
      "scope": "aggregate",
      "panel_id": "supply",
      "localization_namespace": "ExampleProvider",
      "message_key": "langlib.ExampleProvider.alarm.storage_low",
      "message_fallback": "LAGERVORRAT NIEDRIG",
      "severity": "warning",
      "sound_id": "bell",
      "active_color": "#F0C541",
      "auto_acknowledge_on_clear": false,
      "logic": "all",
      "conditions": [
        {
          "metric": "$stored.quantity",
          "label_key": "langlib.ExampleProvider.metric.storage_fill",
          "label_fallback": "Lagerfüllstand",
          "operator": "<=",
          "threshold": 15,
          "value_mode": "percent_of_reference",
          "reference_metric": "$stored.capacity"
        }
      ]
    }
  ]
}
```

Die vollständige Beispieldatei liegt unter
`examples/ProviderMod/UNMA/alarms.json`.

## Besitz und stabile Identitäten

Der Provider wird aus dem `manifest.json` des Verzeichnisses ermittelt.
`mod_id` ist verpflichtend und muss exakt dieser Manifest-ID entsprechen. Ein
Mod kann dadurch keine Meldungen im Namensraum eines anderen Mods anlegen.

Die fachliche Identität einer Vorlage ist das Paar
`(<Provider-Mod-ID>, <Alarm-ID>)`. Dateiname, Meldetext und aktueller Messwert
sind ausdrücklich **nicht** Teil dieser Identität. Änderungen an Zahlen oder
Übersetzungen erzeugen daher weder einen zweiten Schlitz noch ein neues
`KOMMT`.

Automatische Vorlagen werden anhand von `prototype_ids` auf Spielentitäten
angewendet:

- `aggregate` ist der Standard und erzeugt genau einen festen Schlitz für die
  Vorlage. Er wird aktiv, wenn mindestens eine passende Entität die Vorlage
  erfüllt.
- `per_entity` ist ein ausdrückliches Opt-in. Es erzeugt pro stabiler
  Entitäts-ID einen festen Schlitz und kann bei großen Fahrzeugflotten viele
  Schlitze anlegen.

Die technischen Schlüssel lauten sinngemäß
`external:<Provider>:<Alarm>` beziehungsweise
`external:<Provider>:<Alarm>:entity:<EntityId>`. Provider sollten diese
Darstellung nicht selbst zusammensetzen; sie ist hier nur zur Erklärung der
Stabilität angegeben.

## JSON-Felder

### Wurzelobjekt

| Feld | Pflicht | Bedeutung |
|---|---:|---|
| `$schema` | nein | Editor-Hinweis auf das mitgelieferte JSON Schema. |
| `schema_version` | ja | Für diesen Vertrag exakt `1`. |
| `mod_id` | ja | Muss exakt der ID des umgebenden Mod-Manifests entsprechen. |
| `alarms` | ja | Liste automatischer Meldungsvorlagen. |

### Meldungsvorlage

| Feld | Pflicht | Standard / Bedeutung |
|---|---:|---|
| `id` | ja | Stabile, innerhalb des Provider-Mods eindeutige Alarm-ID. Nach Veröffentlichung nicht umbenennen. |
| `prototype_ids` | ja | Mindestens eine COI-Prototyp-ID für Gebäude oder Fahrzeuge. |
| `scope` | nein | `aggregate` (Standard) oder `per_entity`. |
| `panel_id` | nein | Zielpanel, zum Beispiel `main` oder `supply`. Ein unbekanntes Panel fällt sicher auf das Standardpanel zurück. |
| `localization_namespace` | nein | LangLib-Mod-Namensraum; standardmäßig die Provider-ID. |
| `message_key` | bedingt | Voller LangLib-Schlüssel, zum Beispiel `langlib.ExampleProvider.alarm.storage_low`. |
| `message_fallback` | bedingt | Lesbarer Text, falls der LangLib-Schlüssel fehlt. |
| `detail_key` | nein | Optionaler voller LangLib-Schlüssel für die Detailzeile. |
| `detail_fallback` | nein | Optionale lesbare Detailzeile. |
| `severity` | nein | `notice`, `warning`, `critical` oder `emergency`; Standard `warning`. |
| `sound_id` | nein | UNMA-Ton-ID, zum Beispiel `auto`, `bell`, `horn` oder `siren`; Standard `auto`. |
| `active_color` | nein | Aktive Farbe als `#RRGGBB` oder `#RRGGBBAA`; ohne Angabe folgt sie der Stufe. |
| `auto_acknowledge_on_clear` | nein | Standard `false`. Bei `true` darf UNMA beim Gehen zusätzlich quittieren. |
| `logic` | nein | `all` (UND, Standard) oder `any` (ODER). |
| `conditions` | ja | Eine bis 32 Bedingungen. |

Mindestens `message_key` oder `message_fallback` muss gesetzt sein; für robuste
und übersetzbare Provider werden immer beide Felder empfohlen.
Unbekannte Zusatzfelder werden innerhalb derselben Hauptversion für spätere
Erweiterungen toleriert. Die als Pflicht markierten Felder werden dennoch beim
Einlesen geprüft; ein Tippfehler in ihnen macht die betroffene Datei oder
Vorlage ungültig.

Die Vorlage liefert nur Werkvorgaben. Eine spätere Benutzeranpassung in UNMA,
insbesondere Ton und automatische Quittierung, hat Vorrang.

### Bedingung

| Feld | Pflicht | Standard / Bedeutung |
|---|---:|---|
| `metric` | ja | Eingebauter UNMA-Messwertpfad oder per C# registrierte Messwert-ID. |
| `label_key` | nein | Voller LangLib-Schlüssel für die Bezeichnung des Ist-Werts. |
| `label_fallback` | nein | Lesbare Bezeichnung bei fehlender Übersetzung. |
| `operator` | ja | Einer von `<`, `<=`, `==`, `!=`, `>=`, `>`. |
| `threshold` | ja | Endlicher Sollwert. `NaN` und unendliche Werte sind ungültig. |
| `value_mode` | nein | `absolute` (Standard) oder `percent_of_reference`. |
| `reference_metric` | bedingt | Bei `percent_of_reference` verpflichtender Bezugswert. |
| `reference_label_key` | nein | Voller LangLib-Schlüssel für den Bezugswert. |
| `reference_label_fallback` | nein | Lesbare Bezeichnung des Bezugswerts. |

Bei `percent_of_reference` vergleicht UNMA
`Istwert / Bezugswert * 100` mit `threshold`. Ist der Bezugswert null, nicht
endlich oder nicht lesbar, ist die Bedingung nicht erfüllt und es wird eine
fehlende beziehungsweise nicht berechenbare Quelle im Schlitz angezeigt; das
Spiel läuft weiter.

Zu den eingebauten Pfaden gehören insbesondere:

- `$entity.enabled`, `$entity.paused`, `$entity.destroyed`
- `$stored.quantity`, `$stored.capacity`, `$stored.percent`
- `$transport.quantity`, `$transport.capacity`, `$transport.percent`
- `$cargo.quantity`, `$cargo.capacity`, `$cargo.percent`

Öffentliche numerische Eigenschaften einer Entität können ebenfalls als
Messwertpfad verfügbar sein. Für mod-eigene, verschachtelte oder berechnete
Werte ist `RegisterMetric` stabiler als das Verlassen auf Reflection.

## LangLib

UNMA 0.8.0 erklärt `LangLib>=0.1.0` selbst als verpflichtende Abhängigkeit.
Ein reiner JSON-Provider muss `LangLib.dll` deshalb weder referenzieren noch
kopieren. In `message_key`, `detail_key` und den Label-Schlüsseln steht der
vollständige kanonische LangLib-Schlüssel:

```text
langlib.ExampleProvider.alarm.storage_low
```

Die provider-eigenen Sprachdateien liegen in `<ModRoot>/lang/de.json` und
`<ModRoot>/lang/en.json`:

```json
{
  "alarm.storage_low": "LAGERVORRAT NIEDRIG",
  "metric.storage_fill": "Lagerfüllstand"
}
```

In der Sprachdatei selbst reicht wie gewohnt die kurze Text-ID.
Groß-/Kleinschreibung ist bei Mod- und Text-IDs relevant. LangLib-Namensräume
dürfen Buchstaben, Ziffern, `_` und `-`, aber keine Punkte enthalten. Besitzt
die COI-Manifest-ID Punkte, muss eine Vorlage daher einen gültigen
`localization_namespace` angeben. Alle ihre `*_key`-Felder müssen exakt mit
`langlib.<localization_namespace>.` beginnen. Fehlt eine Übersetzung,
verwendet UNMA den mitgelieferten Fallbacktext.

Nur wenn der Provider selbst `Lang.Get`, `Lang.Format` oder andere
LangLib-Aufrufe verwendet, sollte sein Manifest zusätzlich
`LangLib>=0.1.0` deklarieren und sein Root mit `Lang.RegisterMod(...)`
registrieren. Ein Provider darf **niemals** `Lang.Configure(...)` aufrufen;
die globale Konfiguration gehört dem LangLib-Mod.

## Öffentliche C#-API

Ein Provider, der C#-Funktionen nutzt, erklärt eine direkte Abhängigkeit:

```json
"mod_dependencies": [ "UNMA>=0.8.0" ]
```

Wenn er LangLib auch direkt aufruft:

```json
"mod_dependencies": [ "UNMA>=0.8.0", "LangLib>=0.1.0" ]
```

Die Assemblyreferenz darf nicht in den Provider-Mod kopiert werden:

```xml
<Reference Include="UNMA">
  <HintPath>$(APPDATA)\Captain of Industry\Mods\UNMA\UNMA.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Der Einstiegspunkt ist die statische Klasse `UNMA.Api.UnmaApi`;
`UnmaApi.ApiVersion` ist für diesen Vertrag `1`. V1 stellt folgende
Kernoperationen bereit:

```csharp
public static bool RegisterMetric(
    string ownerModId,
    ExternalMetricDefinition definition);

public static bool TryRegisterMetric(
    string ownerModId,
    ExternalMetricDefinition definition,
    out string error);

public static bool UnregisterMetric(
    string ownerModId,
    string prototypeId,
    string metricId);

public static bool RegisterAlarmTemplate(
    string ownerModId,
    ExternalAlarmTemplateDefinition definition);

public static bool TryRegisterAlarmTemplate(
    string ownerModId,
    ExternalAlarmTemplateDefinition definition,
    out string error);

public static bool UnregisterAlarmTemplate(
    string ownerModId,
    string alarmId);

public static bool PublishAlarmState(
    string ownerModId,
    ExternalAlarmState state);

public static bool TryPublishAlarmState(
    string ownerModId,
    ExternalAlarmState state,
    out string error);

public static bool PublishAlarmStates(
    string ownerModId,
    IEnumerable<ExternalAlarmState> states);

public static bool TryPublishAlarmStates(
    string ownerModId,
    IEnumerable<ExternalAlarmState> states,
    out string error);

public static bool RemoveAlarmState(
    string ownerModId,
    string alarmId,
    string instanceId = "default");

public static bool UnregisterOwner(string ownerModId);
public static ExternalRegistrySnapshot GetSnapshot();
```

`ownerModId` muss immer die Manifest-ID des aufrufenden Mods sein. Registrierte
IDs sind nur innerhalb dieses Besitzers eindeutig. Ein doppelter
Registrierungsversuch überschreibt nichts, sondern liefert `false`. Die
`Try...`-Varianten geben zusätzlich eine lesbare Fehlerbeschreibung aus. Ein
Provider registriert daher einmal und entfernt eine alte Registrierung
gezielt, bevor er sie bewusst ersetzt.

### Eigene Messwerte

`RegisterMetric` verbindet eine stabile Messwert-ID mit einem sicheren Leser.
Der Callback steht in `ExternalMetricDefinition.Reader`. Er erhält die
konkrete Spielentität als `object` und gibt entweder einen endlichen `double`
oder `null` zurück. `PrototypeId` begrenzt ihn auf einen Prototyp; `*` ist der
Standard für alle. Er muss fremde Entitätstypen ohne Ausnahme mit `null`
ablehnen. Ausnahmen eines Provider-Lesers werden von UNMA abgefangen und als
nicht verfügbarer Messwert behandelt; sie dürfen die Spielschleife nicht
abbrechen.

`ExternalMetricDefinition.LabelKey` ist leer oder ein vollständiger
`langlib.<LocalizationNamespace>.<Text-ID>`-Schlüssel. Bei einem vom
Provider-Namen abweichenden Namensraum muss der Provider dessen Root über
LangLib registrieren oder denselben Namensraum in einer JSON-/C#-Vorlage
verwenden. `SuggestedReferenceMetric` kann Editoren einen passenden Bezugswert
für Prozentbedingungen vorschlagen.

Die Messwert-ID kann anschließend in JSON- und C#-Vorlagen verwendet werden.
Registriere Leser möglichst einmal während der Mod-Initialisierung und nicht
in jedem Tick.

### Automatische C#-Vorlagen

`RegisterAlarmTemplate` entspricht fachlich einer Vorlage aus einer
UNMA-JSON-Datei. Eine Vorlage benötigt mindestens eine Prototyp-ID und eine
Bedingung und wird von UNMA automatisch ausgewertet. Damit kann ein Provider
alle Definitionen in C# erzeugen, ohne Dateien zu generieren. Für editierbare,
übersetzbare Standardregeln ist JSON meistens leichter zu warten.

### Push-Meldungen

Eine Push-Meldung wird als vollständiger `ExternalAlarmState` veröffentlicht;
eine gesonderte Vorlage ist dafür nicht erforderlich. `Id` plus `InstanceId`
bilden die stabile Vorkommnis-ID. Der Provider setzt darin:

- `Active = true`: Bedingung steht an.
- `Active = false`: Bedingung ist gegangen.

`InstanceId` muss über Laden/Speichern hinweg dieselbe fachliche Entität oder
dasselbe Vorkommnis bezeichnen; `EntityKey` kann die fachliche Entitäts-ID für
Anzeige und Diagnose ergänzen. Für eine echte Sammelmeldung kann der Provider
beispielsweise die konstante `InstanceId = "aggregate"` verwenden.
Wiederholtes Publizieren desselben Zustands erzeugt kein neues `KOMMT`.
Ein unverändert erneut publizierter Zustand verändert auch die API-Revision
nicht. Für Flotten und andere größere Zustandsmengen sollte der Provider
`PublishAlarmStates` verwenden: Alle Einträge werden zuerst geprüft und danach
mit nur einem unveränderlichen Snapshot-Update veröffentlicht.

Der Provider publiziert zuerst `Active = false`. Nachdem UNMA diesen Übergang
beobachten konnte, darf er den Eintrag mit `RemoveAlarmState` entfernen. Beim
Entladen oder vollständigen Neuaufbau seiner Integration ruft er
`UnregisterOwner(ownerModId)` auf; dadurch verschwinden Messwertleser,
Vorlagen und Push-Zustände dieses Besitzers aus dem API-Katalog.

Ein vollständiges Beispiel für Messwert, Vorlage und Push-Zustand steht in
`examples/ProviderMod/UnmaIntegration.cs`.

## K/G/Q: UNMA besitzt die Zustandsmaschine

Provider liefern nur **steht an** oder **steht nicht an**. UNMA bildet daraus:

| Übergang / Bedienung | Zustand | Darstellung |
|---|---|---|
| inaktiv -> aktiv | `K` | Unquittiert, rot blinkend; Ton wiederholt sich. |
| MASTER QUIT während aktiv | `KQ` | Schwarzer Text auf weißem Hintergrund; Ton endet. |
| aktiv -> inaktiv, noch nicht quittiert | `KG` | Schwarz auf weiß blinkend; weiterhin quittierpflichtig. |
| gegangen und quittiert | `KGQ` | Schwarz auf weiß, bleibt bis zum manuellen Löschen stehen. |

`auto_acknowledge_on_clear=true` erlaubt als bewusste Ausnahme, beim Übergang
nach inaktiv zusätzlich `Q` zu setzen. Standard und Kraftwerkslogik bleiben
manuell: Eine Meldung quittiert sich nie allein.

Solange eine Bedingung wahr bleibt, ändern neue Messwerte oder Meldedetails
weder den Schlitz noch `K/Q`. Erst nachdem die Bedingung wirklich gegangen
ist und später erneut aktiv wird, entsteht ein neues `KOMMT` und eine frühere
Quittierung gilt nicht für das neue Ereignis.

## Validierung, Schutzgrenzen und Fehlerisolation

V1 verwendet feste Grenzen, damit fehlerhafte oder absichtlich übergroße
Providerdateien den Spielstart nicht blockieren:

- höchstens **1 MiB pro JSON-Datei**,
- höchstens **64 JSON-Dateien pro Provider**,
- höchstens **256 Meldungen pro Datei**,
- höchstens **256 JSON-Meldungen insgesamt pro Provider**; bei mehreren
  Dateien gilt die dokumentierte, alphabetische Dateireihenfolge,
- höchstens **32 Bedingungen pro Meldung**,
- höchstens **128 Prototyp-IDs pro Meldung**,
- über die C#-API höchstens **256 Messwerte** und **256 Vorlagen pro
  Provider**,
- über die C#-API höchstens **4096 gleichzeitig veröffentlichte
  Push-Zustände pro Provider** und **4096 Zustände pro Batch**.

Zusätzlich gelten Schema-, Längen- und Werteprüfungen. JSON kann keinen Code,
Typnamen, Dateipfad, Methodenaufruf oder privaten Member angeben. Ein
Messwertpfad darf nur die von UNMA unterstützten öffentlichen Eigenschaften
lesen; komplexe Werte benötigen den isolierten C#-Reader. `mod_id` wird gegen
den besitzenden Mod geprüft, Pfade verlassen niemals `<ModRoot>/UNMA`, und
Duplikate innerhalb eines Besitzers werden nicht still überschrieben.
Ein LangLib-Namensraum gehört beim JSON-Laden genau einem Provider; eine
Kollision deaktiviert die spätere Definition statt einen fremden Sprachpfad
zu übernehmen.

Eine ungültige Datei, Vorlage, Bedingung oder ein fehlerhafter C#-Messwertleser
wird isoliert protokolliert. Andere Provider und die restlichen UNMA-Meldungen
arbeiten weiter. Unbekannte Prototypen oder Messwerte aktivieren keine
Meldung. Die Integrationsübersicht in `OPTIONEN` zeigt geladene
Definitionen und Diagnosen; nach Dateiänderungen kann dort neu geladen werden.

## Compatibility rules for providers (English)

- Put declarative V1 files in `<ModRoot>/UNMA/*.json` and translations in
  `<ModRoot>/lang/<locale>.json`.
- JSON-only providers do not reference or redistribute `UNMA.dll`.
- C# providers declare `UNMA>=0.8.0`, reference the shared DLL with
  `Private=false`, and use `UNMA.Api.UnmaApi`.
- Register custom metric readers before templates that reference them. A
  reader returns `null` for unsupported entities.
- Publish the `ExternalAlarmState.Active` property as `true`/`false`. UNMA owns
  acknowledgement and K/G/Q.
- Keep provider, alarm, metric, prototype and entity IDs stable across
  releases. A changed value must never be represented as a new alarm ID.
- Prefer `aggregate`; use `per_entity` only when a separate physical slot for
  every matching entity is intentional.
- Treat the JSON Schema and the public V1 API as the compatibility contract;
  do not depend on `UNMA.Runtime` or other implementation namespaces.
