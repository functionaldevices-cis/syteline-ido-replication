# Replication Rules and Field Maps

A rule says: when a record in this IDO changes, send these fields to this target. An IDO can have several rules, for example one for Event Hubs and one for Salesforce. Each rule is sent on its own, so one failing target doesn't stop the others.

Form: **IDO Replication Rules (AIR)** (`ue_AIR_IDOReplicationRules`)

## Adding a rule

1. Add a record. Set the **IDO Name** to the IDO collection you want to replicate, for example `SLCustomers`.
2. Enter the **Target Type**: `AzureEventHub` or `SalesforceRESTAPI`, spelled exactly.
3. On the **Target Credentials** tab, pick a [credential](Credentials) of the same target type.
4. On the **Target Options** tab, fill in the options for the target (below).
5. On the **Field Mapping** tab, add the fields to send (below).
6. Check **Is Active** when the rule is ready. Only active rules replicate.

A rule does nothing until the IDO also has active triggers. See [Setting Up Triggers](Setting-Up-Triggers).

## Target options

| Target type | Option 1 | Option 2 | Option 3 |
| --- | --- | --- | --- |
| `AzureEventHub` | Event Schema URI (optional) | ID field name | Operation Field Name (optional) |
| `SalesforceRESTAPI` | sObject name | External ID field name | Delete Behavior: `Skip` (default) or `Delete` |

Details are on the [Azure Event Hubs](Target-Azure-Event-Hubs) and [Salesforce](Target-Salesforce) pages.

**Set Option 2 on every rule.** It names the output field that identifies the record. The replication log uses it as the record ID, and the retry task uses it to tell when a failed record was replicated again later.

## Field maps

Each map field is one field in the output record.

| Column | Meaning |
| --- | --- |
| Field Seq | The order of the field in the output |
| Name | The output field name, as the target expects it |
| Source Type | `IDOProperty` or `Literal` |
| Source Value | For `IDOProperty`, the IDO property name. For `Literal`, the fixed value to send. |
| Transformation | Optional. See below. |

Notes:
- Property names must exist on the IDO. The properties of every active rule for an IDO are loaded together, so one misspelled property makes the load fail for all of that IDO's rules.
- A map field with a blank Source Value is left out, unless its Source Type is `Literal`.
- Without a transformation, a value is sent with the type the IDO returns (number, date, text).
- Output field names should be unique within a rule.

### Transformations

| Transformation | Result |
| --- | --- |
| DateTime to Date | Text in `yyyy-MM-dd` form |
| Decimal to Integer | A whole number, with halves rounded away from zero |
| Decimal to USD String | Text such as `$1,234.50` |
| Checkbox (Byte) to Boolean | `true` or `false` |
| Checkbox (Byte) to True/False String | `"True"` or `"False"` |
| Checkbox (Byte) to Yes/No String | `"Yes"` or `"No"` |

- Empty values stay empty.
- If a value can't be converted, it is sent unchanged. The record's replication log row gets a warning that names the field and the value.

## Example

A rule on `SLCustomers` that sends three fields:

| Field Seq | Name | Source Type | Source Value | Transformation |
| --- | --- | --- | --- | --- |
| 1 | `CustomerNumber` | IDOProperty | `CustNum` | |
| 2 | `Name` | IDOProperty | `Name` | |
| 3 | `Source` | Literal | `Syteline` | |

Output record:

```json
{ "CustomerNumber": "C000123", "Name": "Example Customer", "Source": "Syteline" }
```

With Option 2 set to `CustomerNumber`, the log shows `C000123` as the record ID.

## Not working yet

- **Import Map** on the Rules form opens a window to paste a saved rule file. The method behind it isn't finished.
- **Map field sources** (`ue_AIR_IDOReplicationMapFieldSources`) exist in the data model, but replication reads only the single source on each map field.
- **Output preview.** The Rules IDO has a `ue_LoadOutputPreview` method that returns up to 5 remapped records (first 20 fields) for a rule, including an inactive one. The packaged Rules form doesn't show it yet.
