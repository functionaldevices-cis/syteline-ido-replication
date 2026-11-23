<#
.SYNOPSIS
    Saves test records through the Mongoose IDO REST API (or through an extension class) so the AIR diagnostic
    triggers show how Syteline fires IDO events for that kind of caller.

.DESCRIPTION
    A test tool for a sandbox, not for production. See the wiki page Trigger Diagnostics for the test plan.

    Actions:
      SetupDiagnostics   Creates the diagnostic event handlers on -IDOName (ue_UpdateAESTriggers, SetupDiagnosticTriggers).
                         They are active at once, so every save to the IDO writes "Diag:" rows to the replication log.
      RemoveDiagnostics  Removes them again.
      Insert             Inserts -Count records with keys -KeyPrefix001, -KeyPrefix002 and so on.
      Update             Updates the records whose key starts with -KeyPrefix (at most -Count, 0 = all).
      Delete             Deletes the records whose key starts with -KeyPrefix (at most -Count, 0 = all).
      Cleanup            Deletes every record whose key starts with -KeyPrefix, by key.

    -Via Rest sends one ido/update request with every record in it. -Via ExtensionClass asks the Rules IDO's
    ue_DiagUpdateCollection method to do the same save through this.Context.Commands.UpdateCollection.

    -Identify says how Update and Delete name each record: ItemId (the ItemId only, no properties), Keys (the key
    property only, with an empty ItemId) or KeysAndRowPointer (the key and the RowPointer).

    -FailLast makes the last record fail (a duplicate key on Insert, a key that doesn't exist on Update and Delete),
    so the whole save should roll back. Check whether its Diag rows rolled back too.

    What is and isn't confirmed about the REST API:
      - ido/update/{ido}?refresh=true with a body of { Changes: [ { Action, ItemId, Properties, UpdateLocking } ] },
        Action 1 = insert, 2 = update, 4 = delete, is documented by Infor (REST API version 2, UpdateCollection).
      - ido/load/{ido} and ido/invoke/{ido}?method= are not in the pages that were read. They are used here in the
        shape Mongoose's REST API version 2 commonly takes. If a call fails or its result reads oddly, compare it with
        your instance's Swagger document (ido/dynamic/api-docs-collection) and adjust Get-AIRRecords or Invoke-AIRMethod.

.PARAMETER BaseUrl
    The REST API base, up to and including /ido. For example https://<server>/IDORequestService/ido, or the ION API
    gateway's equivalent.

.PARAMETER Authorization
    The value for the Authorization header: a Mongoose security token, or "Bearer <token>" through the ION API.

.PARAMETER MongooseConfig
    The configuration name for the X-Infor-MongooseConfig header. Needed through the ION API.

.EXAMPLE
    $conn = @{ BaseUrl = 'https://<server>/IDORequestService/ido'; Authorization = $token; MongooseConfig = '<config>' }
    .\Test-AIRTriggers.ps1 @conn -Action SetupDiagnostics
    .\Test-AIRTriggers.ps1 @conn -Action Insert -Count 3
    .\Test-AIRTriggers.ps1 @conn -Action Update -Identify ItemId
    .\Test-AIRTriggers.ps1 @conn -Action Delete -Identify Keys
    .\Test-AIRTriggers.ps1 @conn -Action RemoveDiagnostics
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SetupDiagnostics', 'RemoveDiagnostics', 'Insert', 'Update', 'Delete', 'Cleanup')]
    [string] $Action,

    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $Authorization,

    [string] $MongooseConfig = '',

    [ValidateSet('Rest', 'ExtensionClass')]
    [string] $Via = 'Rest',

    [string] $IDOName = 'SLEndtypes',

    [string] $KeyProperty = 'EndUserType',

    [string] $KeyPrefix = 'ZQ',

    [ValidateRange(0, 9999)]
    [int] $Count = 3,

    [ValidateSet('ItemId', 'Keys', 'KeysAndRowPointer')]
    [string] $Identify = 'Keys',

    [switch] $FailLast,

    [hashtable] $InsertProperties = @{ Description = 'AIR trigger test' },

    [hashtable] $UpdateProperties = $null,

    [switch] $Force
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$BaseUrl = $BaseUrl.TrimEnd('/')
$RulesIDO = 'ue_AIR_IDOReplicationRules'
$ConfigsIDO = 'ue_AIR_IDOReplicationConfigs'

if ($KeyPrefix.Trim().Length -lt 2) {
    throw 'Use a -KeyPrefix of at least 2 characters that no real record starts with. Update, Delete and Cleanup act on every record whose key starts with it.'
}

if ($null -eq $UpdateProperties) {
    $UpdateProperties = @{ Description = 'AIR test ' + (Get-Date -Format 'HH:mm:ss') }
}

function Get-AIRHeaders {
    $headers = @{ Authorization = $Authorization }
    if ($MongooseConfig -ne '') {
        $headers['X-Infor-MongooseConfig'] = $MongooseConfig
    }
    return $headers
}

function Invoke-AIRRequest {
    param([string] $Method, [string] $Url, $Body = $null)

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $parameters = @{ Method = $Method; Uri = $Url; Headers = (Get-AIRHeaders); ContentType = 'application/json' }
    if ($null -ne $Body) {
        $parameters['Body'] = [Text.Encoding]::UTF8.GetBytes($Body)
    }

    try {
        $response = Invoke-RestMethod @parameters
    }
    finally {
        $stopwatch.Stop()
        Write-Host ("{0} {1} took {2:N0} ms" -f $Method, ($Url -replace '\?.*$', ''), $stopwatch.Elapsed.TotalMilliseconds)
    }

    return $response
}

# ido/update: DOCUMENTED. One request with every change in it, so it is one UpdateCollection.

function Send-AIRUpdate {
    param([object[]] $Changes)

    $body = ConvertTo-Json -InputObject @{ Changes = @($Changes) } -Depth 8
    $response = Invoke-AIRRequest -Method 'POST' -Url ("{0}/update/{1}?refresh=true" -f $BaseUrl, $IDOName) -Body $body

    if ($response.PSObject.Properties['Success'] -and -not $response.Success) {
        Write-Host ("The save failed: {0}" -f $response.Message) -ForegroundColor Yellow
    }
    else {
        Write-Host ("The save of {0} record(s) went through." -f @($Changes).Count) -ForegroundColor Green
    }

    return $response
}

function New-AIRProperty {
    param([string] $Name, $Value, [bool] $Modified)
    return @{ Name = $Name; Value = $Value; Modified = $Modified; IsNull = ($null -eq $Value) }
}

# ido/load: NOT CONFIRMED in the docs that were read. Accepts rows either as objects or as lists of Name/Value pairs.

function Get-AIRRecords {
    param([int] $Cap)

    $filter = "{0} LIKE '{1}%'" -f $KeyProperty, $KeyPrefix.Replace("'", "''")
    $url = "{0}/load/{1}?properties={2}&filter={3}&recordCap={4}&orderBy={5}" -f $BaseUrl, $IDOName,
        [Uri]::EscapeDataString("$KeyProperty,RowPointer,_ItemId"), [Uri]::EscapeDataString($filter), $Cap, [Uri]::EscapeDataString($KeyProperty)
    $response = Invoke-AIRRequest -Method 'GET' -Url $url

    $records = @()
    foreach ($item in @($response.Items)) {
        $values = @{}
        if ($item -is [Array]) {
            foreach ($pair in $item) { $values[[string]$pair.Name] = $pair.Value }
        }
        else {
            foreach ($property in $item.PSObject.Properties) { $values[$property.Name] = $property.Value }
        }
        $records += [pscustomobject]@{
            Key        = [string]$values[$KeyProperty]
            RowPointer = [string]$values['RowPointer']
            ItemId     = [string]$values['_ItemId']
        }
    }

    if ($records.Count -gt 0 -and @($records | Where-Object { $_.Key -eq '' }).Count -gt 0) {
        Write-Host 'The load returned rows without keys. Its response shape may differ from what this script expects:' -ForegroundColor Yellow
        Write-Host (ConvertTo-Json -InputObject $response -Depth 5)
    }

    return ,$records
}

# ido/invoke: NOT CONFIRMED in the docs that were read. Parameters go by position as a JSON array of text.

function Invoke-AIRMethod {
    param([string] $IDO, [string] $Method, [object[]] $Parameters)

    $body = ConvertTo-Json -InputObject @($Parameters | ForEach-Object { [string]$_ }) -Depth 3
    $response = Invoke-AIRRequest -Method 'POST' -Url ("{0}/invoke/{1}?method={2}" -f $BaseUrl, $IDO, $Method) -Body $body
    Write-Host (ConvertTo-Json -InputObject $response -Depth 5)
    return $response
}

function Confirm-AIRDelete {
    param([object[]] $Records)

    if ($Force) { return }

    Write-Host ("About to delete {0} {1} record(s): {2}" -f $Records.Count, $IDOName, (($Records | Select-Object -First 10 | ForEach-Object { $_.Key }) -join ', '))
    $answer = Read-Host 'Type YES to continue'
    if ($answer -ne 'YES') {
        throw 'Nothing was deleted.'
    }
}

$startedAt = Get-Date

switch ($Action) {

    'SetupDiagnostics' {
        Invoke-AIRMethod -IDO $ConfigsIDO -Method 'ue_UpdateAESTriggers' -Parameters @($IDOName, 'SetupDiagnosticTriggers', '') | Out-Null
    }

    'RemoveDiagnostics' {
        Invoke-AIRMethod -IDO $ConfigsIDO -Method 'ue_UpdateAESTriggers' -Parameters @($IDOName, 'RemoveDiagnosticTriggers', '') | Out-Null
    }

    { ($_ -in @('Insert', 'Update', 'Delete')) -and ($Via -eq 'ExtensionClass') } {
        if ($Action -eq 'Delete' -and -not $Force) {
            $answer = Read-Host ("About to delete up to {0} {1} record(s) whose {2} starts with '{3}' (0 = all). Type YES to continue" -f $Count, $IDOName, $KeyProperty, $KeyPrefix)
            if ($answer -ne 'YES') { throw 'Nothing was deleted.' }
        }
        $extra = if ($Action -eq 'Insert') { $InsertProperties } elseif ($Action -eq 'Update') { $UpdateProperties } else { @{} }
        $extraText = ($extra.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join ';'
        Invoke-AIRMethod -IDO $RulesIDO -Method 'ue_DiagUpdateCollection' -Parameters @(
            $IDOName, $Action, $KeyProperty, $KeyPrefix, $Count, $extraText, $Identify, [int][bool]$FailLast, ''
        ) | Out-Null
        break
    }

    'Insert' {
        if ($Count -lt 1) { throw 'Insert needs a -Count of at least 1.' }
        $digits = [Math]::Max(3, ([string]$Count).Length)
        $changes = @()
        for ($i = 1; $i -le $Count; $i++) {
            # THE LAST RECORD REPEATS THE FIRST KEY WHEN -FailLast IS SET, SO THE INSERT FAILS ON A DUPLICATE KEY
            $number = if ($FailLast -and $i -eq $Count -and $Count -gt 1) { 1 } else { $i }
            $properties = @(New-AIRProperty -Name $KeyProperty -Value ($KeyPrefix + $number.ToString().PadLeft($digits, '0')) -Modified $true)
            foreach ($entry in $InsertProperties.GetEnumerator()) {
                $properties += New-AIRProperty -Name $entry.Key -Value $entry.Value -Modified $true
            }
            $changes += @{ Action = 1; ItemId = ''; Properties = $properties; UpdateLocking = 1 }
        }
        Send-AIRUpdate -Changes $changes | Out-Null
    }

    { $_ -in @('Update', 'Delete', 'Cleanup') } {
        $records = Get-AIRRecords -Cap $(if ($Action -eq 'Cleanup') { 0 } else { $Count })
        if ($records.Count -eq 0) {
            Write-Host ("No {0} record has a {1} that starts with '{2}'." -f $IDOName, $KeyProperty, $KeyPrefix)
            break
        }

        $isDelete = $Action -ne 'Update'
        $identifyAs = if ($Action -eq 'Cleanup') { 'Keys' } else { $Identify }

        if ($isDelete) { Confirm-AIRDelete -Records $records }

        $changes = @()
        for ($i = 0; $i -lt $records.Count; $i++) {
            $record = $records[$i]
            $failThisOne = $FailLast -and $Action -ne 'Cleanup' -and $i -eq $records.Count - 1
            # A KEY THAT CAN'T EXIST, THE SAME LENGTH AS A REAL ONE: THE PREFIX, THEN TILDES
            $key = if ($failThisOne) { $KeyPrefix + ('~' * [Math]::Max(1, $record.Key.Length - $KeyPrefix.Length)) } else { $record.Key }

            $properties = @()
            $itemId = ''
            if ($identifyAs -eq 'ItemId' -and -not $failThisOne) {
                $itemId = $record.ItemId
            }
            else {
                $properties += New-AIRProperty -Name $KeyProperty -Value $key -Modified $false
                if ($identifyAs -eq 'KeysAndRowPointer' -and -not $failThisOne) {
                    $properties += New-AIRProperty -Name 'RowPointer' -Value $record.RowPointer -Modified $false
                }
            }
            if (-not $isDelete) {
                foreach ($entry in $UpdateProperties.GetEnumerator()) {
                    $properties += New-AIRProperty -Name $entry.Key -Value $entry.Value -Modified $true
                }
            }

            $changes += @{ Action = $(if ($isDelete) { 4 } else { 2 }); ItemId = $itemId; Properties = $properties; UpdateLocking = 1 }
        }
        Send-AIRUpdate -Changes $changes | Out-Null
    }
}

Write-Host ''
Write-Host ("Started at {0:yyyy-MM-dd HH:mm:ss} on this computer. To see what fired, load ue_AIR_IDOReplicationLogs with:" -f $startedAt)
Write-Host ("  IDOName = '{0}' AND Source LIKE 'Diag:%'  (newest first by CreateDate)" -f $IDOName)
