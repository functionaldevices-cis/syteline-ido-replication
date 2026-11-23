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
      Cleanup            Deletes every record whose key starts with -KeyPrefix, by key, always through REST.

    -Via Rest sends one ido/update request with every record in it. -Via ExtensionClass asks the Rules IDO's
    ue_DiagUpdateCollection method to do the same save through this.Context.Commands.UpdateCollection. Setting up
    the diagnostic triggers and -Via ExtensionClass need a Syteline super user.

    -KeyPrefix must be at least 2 characters with no SQL wildcard (% _ [ ]), because Update, Delete and Cleanup act on
    every record whose key starts with it.

    -Identify says how Update and Delete name each record: ItemId (the ItemId only, no properties), Keys (the key
    property only, with an empty ItemId) or KeysAndRowPointer (the key and the RowPointer).

    -FailLast makes the last record fail (a duplicate key on Insert, a key that doesn't exist on Update and Delete),
    so the whole save should roll back. Check whether its Diag rows rolled back too.

    What is and isn't confirmed about the REST API (version 2):
      - ido/update/{ido}?refresh=true with a body of { Changes: [ { Action, ItemId, Properties, UpdateLocking } ] },
        Action 1 = insert, 2 = update, 4 = delete: documented by Infor (UpdateCollection), and used the same way in
        the syteline-api-examples repository, including an update matched on key properties sent with Modified false.
      - ido/token/{config} with username and password headers, returning Token, and ido/load/{ido} with properties,
        filter, orderBy and recordCap, returning Items as objects: as in the syteline-api-examples repository.
      - ido/invoke/{ido}?method= with the parameters as a JSON array: not confirmed by Infor's pages or the examples.
        SetupDiagnostics, RemoveDiagnostics and -Via ExtensionClass use it. If it fails, compare it with your
        instance's Swagger document (ido/dynamic/api-docs-collection) and adjust Invoke-AIRMethod.

.PARAMETER BaseUrl
    The server, for example https://<server>, or the REST API base up to /IDORequestService or /ido. The rest of
    "/IDORequestService/ido" is added when it's missing. Through the ION API, give the gateway's URL up to /ido.

.PARAMETER Authorization
    The value for the Authorization header: a Mongoose security token, or "Bearer <token>" through the ION API.
    Leave it out and pass -Credential to get a token directly from the server instead.

.PARAMETER Credential
    A Syteline user name and password, for getting a token directly (ido/token/{config}). Needs -MongooseConfig.

.PARAMETER MongooseConfig
    The configuration name, sent as the X-Infor-MongooseConfig header and used to get a token.

.EXAMPLE
    $conn = @{ BaseUrl = 'https://<server>'; Credential = (Get-Credential); MongooseConfig = '<config>' }
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

    [string] $Authorization = '',

    [pscredential] $Credential = $null,

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

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$BaseUrl = $BaseUrl.TrimEnd('/')
if ($BaseUrl -match '/IDORequestService$') {
    $BaseUrl = $BaseUrl + '/ido'
}
elseif ($BaseUrl -notmatch '/ido$') {
    $BaseUrl = $BaseUrl + '/IDORequestService/ido'
}

# POWERSHELL 7 VALIDATES THE AUTHORIZATION HEADER AND REJECTS A RAW MONGOOSE TOKEN, SO SKIP THAT CHECK THERE, AS THE
# syteline-api-examples REPOSITORY DOES WITH TryAddWithoutValidation. WINDOWS POWERSHELL 5.1 DOESN'T CHECK IT.

$SkipHeaderValidation = @{}
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $SkipHeaderValidation['SkipHeaderValidation'] = $true
}

function Get-AIRField {
    param($Object, [string] $Name)
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) {
        return $Object.$Name
    }
    return $null
}
$RulesIDO = 'ue_AIR_IDOReplicationRules'
$ConfigsIDO = 'ue_AIR_IDOReplicationConfigs'

if ($KeyPrefix.Trim().Length -lt 2 -or $KeyPrefix -match "[%_\[\]']") {
    throw 'Use a -KeyPrefix of at least 2 characters, with no % _ [ ] or quote, that no real record starts with. Update, Delete and Cleanup act on every record whose key starts with it.'
}

if ($null -eq $UpdateProperties) {
    $UpdateProperties = @{ Description = 'AIR test ' + (Get-Date -Format 'HH:mm:ss') }
}

function Get-AIRHeaders {
    $headers = @{ Authorization = $Authorization; Accept = 'application/json' }
    if ($MongooseConfig -ne '') {
        $headers['X-Infor-MongooseConfig'] = $MongooseConfig
    }
    return $headers
}

function Invoke-AIRRequest {
    param([string] $Method, [string] $Url, $Body = $null)

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $parameters = @{ Method = $Method; Uri = $Url; Headers = (Get-AIRHeaders); ContentType = 'application/json' } + $SkipHeaderValidation
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

    if ((Get-AIRField $response 'Success') -eq $false) {
        Write-Host ("The save failed: {0}" -f (Get-AIRField $response 'Message')) -ForegroundColor Yellow
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

# ido/load, PAGED BY BOOKMARK AS IN THE syteline-api-examples REPOSITORY: EVERY REQUEST SENDS loadType=NEXT, LATER ONES WITH THE
# PREVIOUS RESPONSE'S Bookmark, AND EACH RESPONSE HAS Items (OBJECTS), Bookmark AND MoreRowsExist. ROWS GIVEN AS LISTS OF
# Name/Value PAIRS ARE READ TOO. $Cap 0 MEANS EVERY MATCHING RECORD. THE LOOP ALSO STOPS WHEN THE BOOKMARK REPEATS OR A PAGE
# ADDS NO NEW KEY, SO A SERVER THAT IGNORES THE BOOKMARK CAN'T KEEP IT RETURNING THE SAME PAGE.

function Get-AIRRecords {
    param([int] $Cap)

    $pageSize = if ($Cap -gt 0 -and $Cap -lt 200) { $Cap } else { 200 }
    $filter = "{0} LIKE '{1}%'" -f $KeyProperty, $KeyPrefix
    $bookmark = ''
    $records = @()
    $seenKeys = @{}

    do {
        $url = "{0}/load/{1}?properties={2}&filter={3}&recordCap={4}&orderBy={5}&loadType=NEXT" -f $BaseUrl, $IDOName,
            [Uri]::EscapeDataString("$KeyProperty,RowPointer,_ItemId"), [Uri]::EscapeDataString($filter), $pageSize, [Uri]::EscapeDataString($KeyProperty)
        if ($bookmark -ne '') {
            $url += '&bookmark=' + [Uri]::EscapeDataString($bookmark)
        }
        $response = Invoke-AIRRequest -Method 'GET' -Url $url

        if ((Get-AIRField $response 'Success') -eq $false) {
            throw ('The load failed: {0}' -f (Get-AIRField $response 'Message'))
        }

        $newKeys = 0
        foreach ($item in @(Get-AIRField $response 'Items')) {
            if ($null -eq $item) { continue }
            $values = @{}
            if ($item -is [Array]) {
                foreach ($pair in $item) { $values[[string](Get-AIRField $pair 'Name')] = Get-AIRField $pair 'Value' }
            }
            else {
                foreach ($property in $item.PSObject.Properties) { $values[$property.Name] = $property.Value }
            }
            $key = [string]$values[$KeyProperty]
            if ($key -ne '' -and $seenKeys.ContainsKey($key)) { continue }
            if ($key -ne '') { $seenKeys[$key] = $true; $newKeys++ }
            $records += [pscustomobject]@{
                Key        = $key
                RowPointer = [string]$values['RowPointer']
                ItemId     = [string]$values['_ItemId']
            }
        }

        $previousBookmark = $bookmark
        $bookmark = [string](Get-AIRField $response 'Bookmark')
        $more = (Get-AIRField $response 'MoreRowsExist') -eq $true -and $bookmark -ne '' -and $bookmark -ne $previousBookmark -and $newKeys -gt 0
    } while ($more -and ($Cap -eq 0 -or $records.Count -lt $Cap))

    if ($Cap -gt 0 -and $records.Count -gt $Cap) {
        $records = $records[0..($Cap - 1)]
    }

    # A ROW WITHOUT A KEY MEANS THE RESPONSE ISN'T SHAPED AS EXPECTED. STOP, RATHER THAN SAVE CHANGES WITH A BLANK KEY.

    if (@($records | Where-Object { $_.Key -eq '' -or -not $_.Key.StartsWith($KeyPrefix) }).Count -gt 0) {
        Write-Host (ConvertTo-Json -InputObject $response -Depth 5)
        throw 'The load returned rows without the expected keys, so nothing was saved. Compare the response above with Get-AIRRecords.'
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

# A DIRECT TOKEN, AS IN THE syteline-api-examples REPOSITORY: ido/token/{config} WITH THE USER NAME AND PASSWORD AS HEADERS

if ($Authorization -eq '') {
    if ($null -eq $Credential -or $MongooseConfig -eq '') {
        throw 'Pass -Authorization, or -Credential with -MongooseConfig to get a token.'
    }
    $tokenHeaders = @{
        username = $Credential.UserName
        password = $Credential.GetNetworkCredential().Password
        'X-Infor-MongooseConfig' = $MongooseConfig
        Accept = 'application/json'
    }
    $tokenUri = [Uri]("{0}/token/{1}" -f $BaseUrl, [Uri]::EscapeDataString($MongooseConfig))
    if ($tokenUri.Scheme -ne 'https' -and -not $tokenUri.IsLoopback) {
        throw 'The password would be sent without encryption. Use an https:// server address.'
    }
    $tokenResponse = Invoke-RestMethod -Method 'GET' -Uri $tokenUri -Headers $tokenHeaders
    if ((Get-AIRField $tokenResponse 'Success') -ne $true -or [string]::IsNullOrEmpty([string](Get-AIRField $tokenResponse 'Token'))) {
        throw ('Could not get a token: {0}' -f (Get-AIRField $tokenResponse 'Message'))
    }
    $Authorization = [string](Get-AIRField $tokenResponse 'Token')
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
        if ($FailLast -and $Count -lt 2) { throw '-FailLast on Insert needs a -Count of at least 2: the last record fails by repeating the first key.' }
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
