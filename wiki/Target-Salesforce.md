# Target: Salesforce

Target type: `SalesforceRESTAPI`

Upserts records into a Salesforce object by external ID. Deletes are optional, per rule.

## Credential values

| Value | Name | What to enter |
| --- | --- | --- |
| 1 | Client ID | The consumer key of your Salesforce connected app |
| 2 | Client Secret | The consumer secret of the connected app |
| 3 | Username | The integration user's username |
| 4 | Password | The integration user's password |
| 5 | Security Token | The integration user's security token |

Sign-in uses the OAuth username-password flow against `login.salesforce.com`. That means:
- The connected app and the org must allow the username-password flow.
- Sandboxes, which sign in at `test.salesforce.com`, aren't supported yet.

Use a dedicated integration user with access only to the objects you replicate.

## Rule options

| Option | Name | What to enter |
| --- | --- | --- |
| 1 | sObject name | The API name of the object, for example `Account` or `Order__c` |
| 2 | External ID field name | The API name of the object's external ID field, for example `Syteline_Id__c` |
| 3 | Delete Behavior | `Skip` (default, also when blank) or `Delete` |

## Field map

- Use Salesforce API field names as the output field names.
- Map the external ID field. Records are matched on it.
- The external ID should be unique in Salesforce. If one value matches several records, Salesforce rejects the upsert or delete for that record.

## Upserts

- An insert or update in Syteline upserts the record in Salesforce: it is created if no record has that external ID, and updated if one does.
- Records are sent 200 at a time through the sObject Collections upsert API (version 61.0).
- A batch isn't all-or-nothing. Each record succeeds or fails on its own, and each gets its own row in the [replication log](Replication-Log-and-Retries).

## Deletes

By default, a delete in Syteline does **not** delete anything in Salesforce. The record is logged as `Skipped`.

To delete in Salesforce, set the rule's Delete Behavior to `Delete`. Then:
- The record is deleted by its external ID.
- Deletes are sent 25 at a time through the Composite API.
- A record that is already gone from Salesforce counts as deleted.
- A record with no external ID value can't be deleted and is logged as failed.

## Common errors

| Message | Fix |
| --- | --- |
| `Unable to load access token. Server Error: ...` | Check the five credential values, and that the connected app allows the username-password flow |
| `Batch failed: ...` | The whole batch was rejected. Check the sObject name and the external ID field name. |
| `The external ID matches N records, so none were deleted` | Make the external ID unique in Salesforce |
| `Delete skipped, because the rule's DeleteBehavior isn't Delete.` | Expected with the default. Set Delete Behavior to `Delete` if you want deletes. |
