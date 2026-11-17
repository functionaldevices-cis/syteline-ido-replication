# Target: Salesforce

Target type: `SalesforceRESTAPI`

Upserts records into a Salesforce object by external ID. Deletes are optional, per rule.

## Credential values

| Value | Name | What to enter |
| --- | --- | --- |
| 1 | Client ID | The consumer key of your Salesforce external client app |
| 2 | Client Secret | The consumer secret of the app |
| 3 | Domain | Your org's My Domain name. See below. |

All three are required.

### Signing in

Sign-in uses the OAuth client credentials flow, and only that flow. It is headless: Syteline sends the client ID and secret to Salesforce and gets a token back. No username, password or security token is stored in Syteline.

In Salesforce:

1. Create an external client app with OAuth turned on, and give it API access.
2. Turn on the client credentials flow for the app.
3. Choose the user the app runs as. Use a dedicated integration user with access only to the objects you replicate. Every record this project writes is written as that user.
4. Copy the app's consumer key and consumer secret into the credential.

An older connected app works the same way, as long as it has the client credentials flow turned on. Salesforce no longer lets you create new connected apps by default.

The username-password flow isn't supported. External client apps don't offer it.

### Domain

The Domain decides where sign-in goes: `https://<Domain>.my.salesforce.com`. Enter only the My Domain name, as shown in Salesforce Setup under My Domain.

| Org | Domain | Signs in at |
| --- | --- | --- |
| Production | `mycompany` | `https://mycompany.my.salesforce.com` |
| Sandbox named `partial` | `mycompany--partial.sandbox` | `https://mycompany--partial.sandbox.my.salesforce.com` |

- To replicate to a sandbox, set the Domain to the sandbox's My Domain name. This is the way to test a rule before pointing it at production: make one credential for the sandbox and one for production, and switch the rule's credential. Each org needs its own external client app, so the client ID and secret differ too.
- If you paste a URL or host name that contains the name, such as `https://mycompany.my.salesforce.com/` or `mycompany.lightning.force.com`, it is cut down to the name.
- A name is one word (`mycompany`), or a word and a suffix (`mycompany--partial.sandbox`, or `mycompany-dev-ed.develop` for a Developer Edition org). Only letters, digits and hyphens are allowed in each part.
- Don't enter a sign-in host such as `test.salesforce.com` or `login.salesforce.com`. Those are rejected with a message that says what to enter.

Domain works the same way as the `Domain` property of the Salesforce credential in SyteBridge and the Bridjer Salesforce library, so a value that works there works here.

## Rule options

| Option | Name | What to enter |
| --- | --- | --- |
| 1 | sObject name | The API name of the object, for example `Account` or `Order__c` |
| 2 | External ID field name | The API name of the object's external ID field, for example `Syteline_Id__c` |
| 3 | Delete Behavior | Choose `Skip` (default, also when blank) or `Delete` from the list |

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

A sensitive value shows as dots on the Credentials form. To change one, type the new value over the dots and save.

| Message | Fix |
| --- | --- |
| `Unable to load access token. Server Error: ...` | Check the client ID and secret, that the app has the client credentials flow turned on, and that it has a user to run as. For a sandbox, check that the Domain is the sandbox's My Domain name and that the client ID and secret come from the sandbox's app. |
| `The Salesforce credential is missing a Client ID.` (or `Client Secret`, or `Domain`) | Fill in the value on the credential. All three are required. |
| `The Salesforce credential's Domain '...' isn't a My Domain name.` | Enter only the My Domain name, for example `mycompany` or `mycompany--partial.sandbox`, not a host such as `test.salesforce.com` |
| `Batch failed: ...` | The whole batch was rejected. Check the sObject name and the external ID field name. |
| `The external ID matches N records, so none were deleted` | Make the external ID unique in Salesforce |
| `Delete skipped, because the rule's DeleteBehavior isn't Delete.` | Expected with the default. Set Delete Behavior to `Delete` if you want deletes. |
