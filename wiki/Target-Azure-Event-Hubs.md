# Target: Azure Event Hubs

Target type: `AzureEventHub`

Sends each record as one JSON event through the Event Hubs REST batch endpoint. It works with any Event Hubs tier and with Microsoft Fabric Eventstreams through a custom endpoint, including standard, [inferred multi-schema](https://learn.microsoft.com/fabric/real-time-intelligence/event-streams/process-events-with-multiple-schemas) and [Event Schema](https://learn.microsoft.com/fabric/real-time-intelligence/schema-sets/use-event-schemas) Eventstreams.

## Credential values

| Value | Name | What to enter |
| --- | --- | --- |
| 1 | Connection String | The connection string from a shared access policy **on the event hub itself**, not on the namespace. It must include `Endpoint`, `SharedAccessKeyName`, `SharedAccessKey` and `EntityPath`. The policy only needs the **Send** claim. |
| 2 | Event Source | A URL that identifies your organization, for example your company's website. Required when a rule sets an Event Schema URI. |
| 3 | Max Batch KB Override | Optional. Leave blank for the default of about 220 KB, which is safe for every tier. Standard tier and above can set up to 850. |

For a Fabric Eventstream, add a custom endpoint source and use its Event Hub connection string.

## Rule options

| Option | Name | What to enter |
| --- | --- | --- |
| 1 | Event Schema URI | Optional. Only for Fabric Event Schema Eventstreams. The schema's full URL, in the form `https://<host>/schemagroups/<schema set id>/schemas/<schema name>/versions/<version>`. |
| 2 | ID field name | The output field that identifies the record. Used as the record ID in the replication log and in failure messages. Without it, a retry can resend older data after newer data. |
| 3 | Operation Field Name | Optional. Adds the operation to each event body under this field name. |

## What is sent

**Body.** The output record from the rule's field map, as JSON:

```json
{ "CustomerNumber": "C000123", "Name": "Example Customer" }
```

**The operation.** Every event carries the operation as a user property named `operation`. A trigger sends `Insert`, `Update` or `Delete`. A [bulk load](Bulk-Loads) or a test sends `Upsert`, which means insert or update, whichever applies: the run doesn't know if the consumer already has the record.

If you set Operation Field Name, the operation is also written into the body under that name. It is off by default because Event Schema Eventstreams reject fields their schema doesn't define. If you turn it on with an event schema, add the field to the schema.

**CloudEvents properties.** When the rule has an Event Schema URI, each event also carries these user properties, which Fabric Event Schema Eventstreams require:

| Property | Value |
| --- | --- |
| `cloudEvents:specversion` | `1.0` |
| `cloudEvents:type` | The schema name from the URI |
| `cloudEvents:source` | The credential's Event Source |
| `cloudEvents:id` | A new GUID per event |
| `cloudEvents:time` | The send time, in UTC |
| `cloudEvents:datacontenttype` | `application/json` |
| `cloudEvents:dataschema` | The Event Schema URI |

Without an Event Schema URI, plain events are sent.

## Reading the operation in Fabric

Fabric keeps user properties in event metadata only when the Eventstream's Event Hub source uses **Extended features**. Read the operation in a SQL operator with `GETMETADATAPROPERTYVALUE`.

If your destination only reads the event body, set Operation Field Name instead.

## Batching and limits

- Events are sent in batches. A batch is closed when the next event would take it over the size limit.
- The limit depends on the tier: 256 KB for Basic, 1 MB for Standard and above. The default batch size of about 220 KB fits every tier.
- An event larger than the batch limit is sent on its own. If it is also over the tier's limit, the Event Hub rejects it and the record is logged as failed.
- Only an HTTP `201 Created` response counts as accepted. Any other response fails every record in that batch.

## Deletes

A delete is sent like any other event, with `operation` set to `Delete`. The body holds the record as it was just before it was deleted. What the consumer does with it is up to you.

## Common errors

A sensitive value shows as dots on the Credentials form. To change one, type the new value over the dots and save.

| Message | Fix |
| --- | --- |
| `The Azure Event Hub connection string is missing ... EntityPath` | Use the connection string from a policy on the event hub, not the namespace |
| `The rule has an Event Schema URI, but its credential has no Event Source` | Set Event Source on the credential |
| `No '/schemas/<name>' segment found in the Event Schema URI` | Paste the schema's full URL, including `/schemagroups/`, `/schemas/` and `/versions/` |
| `HTTP 401 ...` | Check the key, and that the policy has the Send claim |
