# Credentials

A credential holds the connection details for one external system. A rule links to one credential, and several rules can share it.

Form: **IDO Replication Credentials (AIR)** (`ue_AIR_IDOReplicationCredentials`)

## Adding a credential

1. Add a record and give it a name.
2. Choose or type the **Target Type**: `AzureEventHub` or `SalesforceRESTAPI`, spelled exactly, including capitals. The form then shows and labels only the values that target uses. If no value fields appear, the Target Type is misspelled.
3. Fill in the values and save.

The Target Type can only be set on a new credential. Once the credential is saved, it is locked.

**Sensitive values are masked once the credential is saved.** While you enter a new credential, every value shows as you type it, so you can check it. After you save, a sensitive value's input shows dots, like a password field, and a preview appears to its right: the first 6 and last 6 characters, such as `Endpoi ... ubname`. Use it to check that you saved the value you meant to.

| Target type | Sensitive | Not sensitive |
| --- | --- | --- |
| `AzureEventHub` | Connection String | Event Source, Max Batch KB Override |
| `SalesforceRESTAPI` | Client ID, Client Secret | Domain |

A value of 12 characters or fewer previews as `...`, so the preview never shows a whole value.

## Changing a credential

Every value stays editable on a saved credential. To change a sensitive value, for example after rotating a secret, type or paste the new value over the dots and save. The preview then shows the new value's first and last 6 characters. Rules that use the credential pick up the change on their next run.

The Target Type can't be changed. To use a different target type, add a new credential and point the rules at it.

## Values by target type

| Target type | Value 1 | Value 2 | Value 3 |
| --- | --- | --- | --- |
| `AzureEventHub` | Connection String | Event Source | Max Batch KB Override (optional) |
| `SalesforceRESTAPI` | Client ID | Client Secret | Domain |

What each value means is on the target's page:
- [Azure Event Hubs](Target-Azure-Event-Hubs)
- [Salesforce](Target-Salesforce)

## Keeping credentials safe

> [!WARNING]
> Credential values are stored as plain text in `ue_AIR_idorepl_cred_mst`. The Credentials form masks the sensitive ones with dots, which hides them from someone looking at the screen and nothing more. The Rules form doesn't show them. They can still be read through the `ue_AIR_IDOReplicationCredentials` and `ue_AIR_IDOReplicationRules` IDOs.

- Give access to the Credentials and Rules forms, and to those two IDOs, only to the administrators who need it.
- Use a credential with the least access that works. For Event Hubs, use a shared access policy on the one event hub with only the **Send** claim. For Salesforce, set the external client app to run as a dedicated integration user limited to the objects you replicate.
- Rotate a secret if it may have been seen by someone who shouldn't have it.
