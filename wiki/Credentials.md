# Credentials

A credential holds the connection details for one external system. A rule links to one credential, and several rules can share it.

Form: **IDO Replication Credentials (AIR)** (`ue_AIR_IDOReplicationCredentials`)

## Adding a credential

1. Add a record and give it a name.
2. Enter the **Target Type**: `AzureEventHub` or `SalesforceRESTAPI`, spelled exactly. The form then shows and labels only the values that target uses.
3. Fill in the values and save.

## Values by target type

| Target type | Value 1 | Value 2 | Value 3 | Value 4 | Value 5 | Value 6 | Value 7 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `AzureEventHub` | Connection String | Event Source | Max Batch KB Override (optional) | | | | |
| `SalesforceRESTAPI` | Client ID | Client Secret | Username | Password | Security Token | Domain (optional) | Auth Flow (optional) |

What each value means is on the target's page:
- [Azure Event Hubs](Target-Azure-Event-Hubs)
- [Salesforce](Target-Salesforce)

## Keeping credentials safe

> [!WARNING]
> Credential values are stored as plain text in `ue_AIR_idorepl_cred_mst`. They are also shown on the Rules form and can be read through the `ue_AIR_IDOReplicationCredentials` and `ue_AIR_IDOReplicationRules` IDOs.

- Give access to the Credentials and Rules forms, and to those two IDOs, only to the administrators who need it.
- Use a credential with the least access that works. For Event Hubs, use a shared access policy on the one event hub with only the **Send** claim. For Salesforce, use a dedicated integration user limited to the objects you replicate.
- Rotate a secret if it may have been seen by someone who shouldn't have it.
