<#
.SYNOPSIS
    Saves test records through the Mongoose IDO REST API (or through an extension class) so the AIR diagnostic
    triggers show how Syteline fires IDO events for that kind of caller.

.DESCRIPTION
    A test tool for a sandbox, not for production. See the wiki page Trigger Diagnostics for the test plan.

    Actions:
      SetupDiagnostics   Creates the diagnostic event handlers on -IDOName (ue_UpdateAESTriggers, SetupDiagnosticTriggers).
                         They are active at once, so every save to the IDO writes "Diag:" rows to the replication log.
                         With -Strict (SetupStrictDiagnosticTriggers), every handler has Ignore Failure off and the doorbell
                         is on IdoPostUpdateCollection, to see which events' writes roll back with a failed save.
      RemoveDiagnostics  Removes them again.
      Insert             Inserts -Count records with keys -KeyPrefix001, -KeyPrefix002 and so on.
      Update             Updates the records whose key starts with -KeyPrefix (at most -Count, 0 = all).
      Delete             Deletes the records whose key starts with -KeyPrefix (at most -Count, 0 = all).
      Cleanup            Deletes every record whose key starts with -KeyPrefix, by key, always through REST.
      TimeLoad           Read only. Loads -IDOName with -LoadFilter through REST -Repeat times and prints how long each load
                         took and how many rows came back, to measure what a poll for changed records costs.

    -Via Rest sends one ido/update request with every record in it. -Via ExtensionClass asks the Rules IDO's
    ue_DiagUpdateCollection method to do the same save through this.Context.Commands.UpdateCollection. Setting up
    the diagnostic triggers and -Via ExtensionClass need a Syteline super user: the Syteline user the calls run as,
    which through -IonApiFile is the user behind the file's service account.

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
      - Through the ION API, as the syteline-api-examples repository does: a token from the .ionapi file's pu + ot
        (grant_type password, with its ci, cs, saak and sask), sent as "Bearer <token>", and the base URL iu/ti/CSI.
      - ido/token/{config} with username and password headers, returning Token, and ido/load/{ido} with properties,
        filter, orderBy and recordCap, returning Items as objects: as in the syteline-api-examples repository.
      - ido/invoke/{ido}?method= with the parameters as a JSON array: not confirmed by Infor's pages or the examples.
        SetupDiagnostics, RemoveDiagnostics and -Via ExtensionClass use it. If it fails, compare it with your
        instance's Swagger document (ido/dynamic/api-docs-collection) and adjust Invoke-AIRMethod.

.PARAMETER IonApiFile
    The path of a .ionapi file (ION API credentials for a backend service account, downloaded from ION API Gateway).
    Every call then runs as the Syteline user behind that service account.
    The script signs in through the ION API with it, and takes the base URL from it. Needs -MongooseConfig. The usual
    way to reach a cloud tenant. The file holds secrets: keep it out of the repository, and nothing from it is printed.

.PARAMETER BaseUrl
    Not needed with -IonApiFile. The server, for example https://<server>, or the REST API base up to
    /IDORequestService or /ido. The rest of "/IDORequestService/ido" is added when it's missing. With an ION API token
    passed in -Authorization, give the .ionapi file's iu/ti/CSI.

.PARAMETER Authorization
    The value for the Authorization header: a Mongoose security token, or "Bearer <token>" through the ION API.
    Leave it out and pass -IonApiFile, or -Credential to get a token directly from the server.

.PARAMETER Credential
    A Syteline user name and password, for getting a token directly (ido/token/{config}). Needs -MongooseConfig.

.PARAMETER MongooseConfig
    The configuration name, sent as the X-Infor-MongooseConfig header and used to get a token.

.PARAMETER LoadFilter
    TimeLoad only: the filter, for example "RecordDate > '2099-01-01'". The default, 1=0, matches nothing without
    reading the table, so it measures only the cost of the call itself.

.PARAMETER LoadProperties
    TimeLoad only: the properties to load, comma-separated. Blank loads RecordDate only.

.PARAMETER LoadCap
    TimeLoad only: the record cap, at least 1. A poll's cost is in finding the rows, so there is no option to load every row.

.PARAMETER Strict
    With -Action SetupDiagnostics, creates the strict set: Ignore Failure off on every handler, no asynchronous handler,
    and the doorbell on IdoPostUpdateCollection. Every save to the IDO fails while the Rules assembly can't run them.

.PARAMETER Repeat
    TimeLoad only: how many times to run the load.

.EXAMPLE
    $conn = @{ IonApiFile = 'C:\path\to\credentials.ionapi'; MongooseConfig = '<config>' }
    .\Test-AIRTriggers.ps1 @conn -Action SetupDiagnostics
    .\Test-AIRTriggers.ps1 @conn -Action Insert -Count 3
    .\Test-AIRTriggers.ps1 @conn -Action Update -Identify ItemId
    .\Test-AIRTriggers.ps1 @conn -Action Delete -Identify Keys
    .\Test-AIRTriggers.ps1 @conn -Action RemoveDiagnostics
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SetupDiagnostics', 'RemoveDiagnostics', 'Insert', 'Update', 'Delete', 'Cleanup', 'TimeLoad')]
    [string] $Action,

    [string] $BaseUrl = '',

    [string] $IonApiFile = '',

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

    [switch] $Force,

    [switch] $Strict,

    [string] $LoadFilter = '1=0',

    [string] $LoadProperties = '',

    [ValidateRange(1, 100000)]
    [int] $LoadCap = 200,

    [ValidateRange(1, 50)]
    [int] $Repeat = 5
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# THROUGH THE ION API, AS IN THE syteline-api-examples REPOSITORY: THE .ionapi FILE GIVES THE TOKEN ENDPOINT (pu + ot), THE CLIENT
# (ci, cs), THE SERVICE ACCOUNT (saak, sask) AND THE BASE URL (iu/ti/CSI). NOTHING FROM THE FILE IS PRINTED.

$IonApi = $null
if ($IonApiFile -ne '') {
    if ($MongooseConfig -eq '') {
        throw '-IonApiFile needs -MongooseConfig, the configuration name, for example <tenant>_<site>.'
    }
    # A PARSE ERROR CAN QUOTE THE FILE'S TEXT, WHICH HOLDS SECRETS, SO IT IS REPLACED WITH A MESSAGE THAT DOESN'T
    try {
        $IonApi = Get-Content -Raw -LiteralPath $IonApiFile -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw ("Couldn't read {0} as a .ionapi file. Download it again from ION API Gateway." -f $IonApiFile)
    }
    foreach ($field in @('ti', 'iu', 'pu', 'ot', 'ci', 'cs', 'saak', 'sask')) {
        if ([string]::IsNullOrEmpty([string]$IonApi.$field)) {
            throw ("The .ionapi file has no '{0}'. Use the file ION API Gateway gives for a backend service account." -f $field)
        }
    }
    if ($BaseUrl -eq '') {
        $BaseUrl = '{0}/{1}/CSI' -f ([string]$IonApi.iu).TrimEnd('/'), $IonApi.ti
    }
}

if ($BaseUrl -eq '') {
    throw 'Pass -IonApiFile, or -BaseUrl with -Authorization or -Credential.'
}

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

# AN ION API TOKEN, AS IN THE syteline-api-examples REPOSITORY: A PASSWORD GRANT FROM THE .ionapi FILE'S pu + ot. EVERY CALL THEN
# RUNS AS THE SYTELINE USER BEHIND THE FILE'S SERVICE ACCOUNT, SO THAT USER IS THE ONE THE SUPER-USER CHECKS SEE.

if ($Authorization -eq '' -and $null -ne $IonApi) {
    $ionTokenUri = [Uri](([string]$IonApi.pu).TrimEnd('/') + '/' + ([string]$IonApi.ot).TrimStart('/'))
    if ($ionTokenUri.Scheme -ne 'https' -and -not $ionTokenUri.IsLoopback) {
        throw 'The .ionapi token URL isn''t https, so the secrets would be sent without encryption.'
    }
    $ionTokenBody = @{
        grant_type    = 'password'
        client_id     = [string]$IonApi.ci
        client_secret = [string]$IonApi.cs
        username      = [string]$IonApi.saak
        password      = [string]$IonApi.sask
    }
    $ionTokenResponse = Invoke-RestMethod -Method 'POST' -Uri $ionTokenUri -Body $ionTokenBody -ContentType 'application/x-www-form-urlencoded'
    if ([string]::IsNullOrEmpty([string](Get-AIRField $ionTokenResponse 'access_token'))) {
        throw 'The ION API returned no access token. Check that the .ionapi file is for this tenant and its service account is active.'
    }
    $Authorization = 'Bearer ' + [string](Get-AIRField $ionTokenResponse 'access_token')
}

# A DIRECT TOKEN, AS IN THE syteline-api-examples REPOSITORY: ido/token/{config} WITH THE USER NAME AND PASSWORD AS HEADERS

if ($Authorization -eq '') {
    if ($null -eq $Credential -or $MongooseConfig -eq '') {
        throw 'Pass -IonApiFile, -Authorization, or -Credential with -MongooseConfig to get a token.'
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

    'TimeLoad' {
        # READ ONLY: ONE ido/load PER REPEAT, NO PAGING, SO EACH TIME IS ONE QUERY. THE FIRST RUN CAN BE SLOWER (A COLD CACHE).
        $properties = if ($LoadProperties -ne '') { $LoadProperties } else { 'RecordDate' }
        $url = "{0}/load/{1}?properties={2}&filter={3}&recordCap={4}" -f $BaseUrl, $IDOName,
            [Uri]::EscapeDataString($properties), [Uri]::EscapeDataString($LoadFilter), $LoadCap
        # A FAILED OR TIMED-OUT RUN IS A RESULT TOO (A SCAN TOO SLOW FOR THE GATEWAY OR THE SQL TIMEOUT), SO IT IS PRINTED AND THE
        # OTHER RUNS STILL GO. ONLY RUNS THAT WORKED COUNT IN THE SUMMARY. THE FIRST RUN IS USUALLY THE SLOWEST (A COLD CACHE).
        $times = @()
        $failures = 0
        for ($i = 1; $i -le $Repeat; $i++) {
            $stopwatch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $response = Invoke-AIRRequest -Method 'GET' -Url $url
                $stopwatch.Stop()
                if ((Get-AIRField $response 'Success') -eq $false) {
                    throw (Get-AIRField $response 'Message')
                }
                $rows = @(Get-AIRField $response 'Items' | Where-Object { $null -ne $_ }).Count
                $times += $stopwatch.Elapsed.TotalMilliseconds
                Write-Host ("Run {0}: {1:N0} ms, {2} row(s), more rows: {3}" -f $i, $stopwatch.Elapsed.TotalMilliseconds, $rows, (Get-AIRField $response 'MoreRowsExist'))
            }
            catch {
                $stopwatch.Stop()
                $failures++
                Write-Host ("Run {0}: failed after {1:N0} ms: {2}" -f $i, $stopwatch.Elapsed.TotalMilliseconds, $_.Exception.Message) -ForegroundColor Yellow
            }
        }
        $label = "{0} with filter {1}, properties {2}, record cap {3}" -f $IDOName, $LoadFilter, $properties, $LoadCap
        if ($times.Count -eq 0) {
            Write-Host ("{0}: every run failed ({1} of {1})." -f $label, $Repeat) -ForegroundColor Yellow
        }
        else {
            $sorted = @($times | Sort-Object)
            Write-Host ("{0}: first run that worked {1:N0} ms, fastest {2:N0} ms, median {3:N0} ms, slowest {4:N0} ms, over {5} run(s) that worked; {6} failed." -f $label,
                $times[0], $sorted[0], $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)], $sorted[-1], $sorted.Count, $failures) -ForegroundColor Green
        }
    }

    'SetupDiagnostics' {
        $setupAction = if ($Strict) { 'SetupStrictDiagnosticTriggers' } else { 'SetupDiagnosticTriggers' }
        Invoke-AIRMethod -IDO $ConfigsIDO -Method 'ue_UpdateAESTriggers' -Parameters @($IDOName, $setupAction, '') | Out-Null
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

if ($Action -ne 'TimeLoad') {
    Write-Host ''
    Write-Host ("Started at {0:yyyy-MM-dd HH:mm:ss} on this computer. To see what fired, load ue_AIR_IDOReplicationLogs with:" -f $startedAt)
    Write-Host ("  IDOName = '{0}' AND Source LIKE 'Diag:%'  (newest first by CreateDate)" -f $IDOName)
}
