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
| 6 | Domain | Optional. Your org's My Domain name. See below. |
| 7 | Auth Flow | Optional. `UsernamePassword` (default, also when blank) or `ClientCredentials`. |

Values 3 to 5 are only used by the `UsernamePassword` flow.

### Domain

The Domain decides where sign-in goes: `https://<Domain>.my.salesforce.com`. Enter only the My Domain name, as shown in Salesforce Setup under My Domain.

| Org | Domain | Signs in at |
| --- | --- | --- |
| Production | `mycompany` | `https://mycompany.my.salesforce.com` |
| Sandbox named `partial` | `mycompany--partial.sandbox` | `https://mycompany--partial.sandbox.my.salesforce.com` |
| Blank | | `https://login.salesforce.com` (production only, `UsernamePassword` only) |

- To replicate to a sandbox, set the Domain to the sandbox's My Domain name. This is the way to test a rule before pointing it at production: make one credential for the sandbox and one for production, and switch the rule's credential.
- If you paste a URL or host name that contains the name, such as `https://mycompany.my.salesforce.com/` or `mycompany.lightning.force.com`, it is cut down to the name.
- A name is one word (`mycompany`), or a word and a suffix (`mycompany--partial.sandbox`, or `mycompany-dev-ed.develop` for a Developer Edition org). Only letters, digits and hyphens are allowed in each part.
- Don't enter a sign-in host such as `test.salesforce.com` or `login.salesforce.com`. Those are rejected with a message that says what to enter.

Domain works the same way as the `Domain` property of the Salesforce credential in SyteBridge and the Bridjer Salesforce library, so a value that works there works here.

### Auth Flow

| Auth Flow | Needs | Notes |
| --- | --- | --- |
| `UsernamePassword` | Client ID, Client Secret, Username, Password, Security Token | The connected app and the org must allow the username-password flow. Domain is optional for production and required for a sandbox. |
| `ClientCredentials` | Client ID, Client Secret, Domain | No user password is stored in Syteline. In Salesforce, turn on the client credentials flow for the connected app and choose the user it runs as. |

Whichever flow you use, the Salesforce user should be a dedicated integration user with access only to the objects you replicate.

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
| `Unable to load access token. Server Error: ...` | Check the credential values, and that the connected app allows the Auth Flow you chose. For a sandbox, check that the Domain is the sandbox's My Domain name. |
| `The salesforce authentication flow is set to ClientCredentials, but the credentials are missing a domain.` | Set the Domain. `ClientCredentials` can't sign in without it. |
| `The AuthFlow config must be set to either UsernamePassword or ClientCredentials.` | Fix the Auth Flow value, or leave it blank for `UsernamePassword` |
| `The Salesforce credential's Domain '...' isn't a My Domain name.` | Enter only the My Domain name, for example `mycompany` or `mycompany--partial.sandbox`, not a host such as `test.salesforce.com` |
| `Batch failed: ...` | The whole batch was rejected. Check the sObject name and the external ID field name. |
| `The external ID matches N records, so none were deleted` | Make the external ID unique in Salesforce |
| `Delete skipped, because the rule's DeleteBehavior isn't Delete.` | Expected with the default. Set Delete Behavior to `Delete` if you want deletes. |
