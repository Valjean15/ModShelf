<#
.SYNOPSIS
    Records the mod.io API answers that the mod.io source's tests will replay, with your credentials and name left out.

.DESCRIPTION
    Calls mod.io's REST API with your own credentials and saves each answer as a JSON file under Recorded, next to this
    script. The tests serve these files with WireMock.Net, so CI never calls mod.io.

    mod.io answers nothing without a credential, and ModShelf will take either of the two a user can make on mod.io's
    API Access page (https://mod.io/me/access), so the script asks for both, and for the API path shown there:
    - a personal access token, made under "OAuth 2" with the read scope only, so nothing on mod.io can be changed with
      it (from MODIO_TOKEN, or asked for without being shown);
    - your API key (from MODIO_API_KEY, or asked for without being shown);
    - your API path, such as https://u-12345.modapi.io/v1 (from MODIO_API_PATH, or asked for).
    None of them is written to a file or the screen. Revoke the token on the same page once the run is done.

    Before a file is written, your user ID, name and profile are replaced with stand-ins, and every file is checked for
    the token, the key (any 12 characters of either), your user ID, your name, your profile's address, this computer's
    name and your home folder: the script stops before writing a file that has one. Review the files before committing
    them anyway.

    It records:
    - which hosts answer which credential: api.mod.io, your API path, and the game's own host (probe-*);
    - who the token belongs to (me), and your public profile as the key reads it (user);
    - how many games there are, how many let third parties use their API, download directly, or only with a token, and
      the games open to third parties, 100 a page, by name and by a word of the game's name;
    - one open game with many mods: the game, its mods 50 a page in each order a search can ask for, two pages deep for
      the newest and the latest updated, and narrowed by words, author, tag, update date and explicit content;
    - one of its mods with several files: the mod, the mod found by the name in its link, its files, its live file, what
      it depends on, and where its live file's download link leads (asked for one byte, so nothing is downloaded);
    - the game's file events since 28 days ago, its latest events, and its oldest, to see how far back they reach;
    - your subscriptions, with the token and with the key;
    - a game closed to third parties, a game, mod and file that don't exist, and a token and a key mod.io refuses.

    A run that completes replaces the earlier recordings; one that stops leaves them as they were. It makes about 90
    requests, one at a time, a little over a second apart (mod.io allows a key 60 a minute), and stops if mod.io asks
    it to slow down.

    Long text the source never reads (descriptions, metadata) is left out, and lists the source reads only the start of
    (pictures) are cut to 2.

.PARAMETER Game
    The game's ID on mod.io. By default, the game open to third parties with direct downloads whose mods were
    downloaded most.

.PARAMETER ModId
    One of the game's mods, by its ID. By default, of the game's 20 most subscribed mods, the first with 3 files or
    more, one that depends on other mods if there is one.

.PARAMETER SearchWords
    Words to search the game's mods for. By default, the first word of 3 letters or more of the mod's name.

.EXAMPLE
    ./Record-ModIoFixtures.ps1

.EXAMPLE
    $env:MODIO_API_PATH = 'https://u-12345.modapi.io/v1'; ./Record-ModIoFixtures.ps1 -Game 306
#>

#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateRange(1, [int]::MaxValue)]
    [int] $Game,

    [ValidateRange(1, [int]::MaxValue)]
    [int] $ModId,

    [ValidateLength(3, 100)]
    [string] $SearchWords,

    # Only for testing this script against a local fake of mod.io: the hosts, each ending in /v1/; {0} is the game's ID.
    [uri] $DefaultApi = 'https://api.mod.io/v1/',
    [string] $GameApi = 'https://g-{0}.modapi.io/v1/',
    [uri] $UserApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# How many mods a page of a game's mods holds in the recordings: the source asks for up to 100.
$PageSize = 50

# A little over a second between requests: a key may make 60 a minute.
$PauseMilliseconds = 1100

# An ID no game, mod or file has.
$MissingId = 999999999

# A search that finds nothing.
$NoMatch = 'qqzxjvnomatchqqzxjv'

# The orders a game's mods are recorded in, by the file each is saved to, as mod.io's _sort takes them.
$Sorts = [ordered]@{
    'newest'      = '-date_live'
    'oldest'      = 'date_live'
    'updated'     = '-date_updated'
    'downloads'   = '-downloads_total'
    'today'       = '-downloads_today'
    'subscribers' = '-subscribers_total'
    'rating'      = '-ratings_weighted_aggregate'
    'name'        = 'name'
    'name-desc'   = '-name'
}

# What a game allows third parties (api_access_options): to use its API, to download directly, only with a token.
$Open = 1
$DirectDownloads = 2
$TokenDownloads = 4

# A mod marked explicit (maturity_option).
$Explicit = 8

# Text the source never reads, left out, and lists cut to their first few.
$LeftOut = @('description', 'description_plaintext', 'metadata_blob', 'instructions')
$Kept = @{ 'images' = 2; 'metadata_kvp' = 0 }

# Who stands in for you in the files.
$Stand = @{ UserId = '1000001'; Name = 'ModShelfTester'; NameId = 'modshelf-tester' }
$RedactedHash = 'REDACTED-HASH'

$UserAgent = 'ModShelf-fixtures/1.0'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

# A secret from its environment variable, or asked for without being shown.
function Get-Secret([string] $Variable, [string] $What) {
    $value = [Environment]::GetEnvironmentVariable($Variable)
    if ([string]::IsNullOrWhiteSpace($value)) {
        $secure = Read-Host -AsSecureString -Prompt "$What (not shown)"
        $value = [System.Net.NetworkCredential]::new('', $secure).Password
    }

    $value = $value.Trim()
    if (-not $value) {
        throw "No $What was entered."
    }

    return $value
}

# Your API path, as mod.io's API Access page shows it, and your user ID in it.
function Get-UserPath {
    $path = $env:MODIO_API_PATH
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Read-Host -Prompt 'Your API path, as the API Access page shows it, such as https://u-12345.modapi.io/v1'
    }

    $path = $path.Trim().TrimEnd('/')
    if ($path -notmatch '^https://u-(\d+)\.modapi\.io/v1$') {
        throw "'$path' isn't an API path such as https://u-12345.modapi.io/v1. Copy it from mod.io's API Access page."
    }

    return [pscustomobject]@{ Api = [uri]"$path/"; UserId = $Matches[1] }
}

# A query string from names and values, each escaped, in the order given.
function Join-Query([System.Collections.Specialized.OrderedDictionary] $Parameters) {
    if (-not $Parameters) {
        return ''
    }

    $pairs = foreach ($parameter in $Parameters.GetEnumerator()) {
        [uri]::EscapeDataString([string]$parameter.Key) + '=' + [uri]::EscapeDataString([string]$parameter.Value)
    }

    return $pairs -join '&'
}

# The base of a server: default (api.mod.io), user (your API path) or game (the game's own).
function Get-Base([string] $Server, [int] $ForGame) {
    switch ($Server) {
        'default' { return $DefaultApi }
        'user' { return $script:UserApi }
        'game' { return [uri]($GameApi -f $ForGame) }
        default { throw "Unknown server $Server." }
    }
}

# A request to mod.io: to a path under a server's /v1/, with the token, the key or neither. -Absolute asks an address as
# mod.io gave it, such as a download link, for one byte. The key goes in the query, as mod.io takes it, and is left out
# of what's recorded.
function Invoke-ModIo {
    param(
        [string] $Server = 'user',
        [string] $Path,
        [System.Collections.Specialized.OrderedDictionary] $Parameters,
        [ValidateSet('token', 'key', 'none')] [string] $Auth = 'token',
        [int] $ForGame = $script:Game,
        [uri] $Absolute,
        [string] $Token = $script:Token,
        [string] $Key = $script:Key
    )

    Start-Sleep -Milliseconds $PauseMilliseconds
    $query = Join-Query $Parameters
    if ($Absolute) {
        $target = $Absolute
        $recordedPath = $Absolute.AbsolutePath
        $query = $Absolute.Query.TrimStart('?')
    }
    else {
        $sent = $query
        if ($Auth -eq 'key') {
            $sent = (@($query, "api_key=$([uri]::EscapeDataString($Key))") | Where-Object { $_ }) -join '&'
        }

        $relative = if ($sent) { "$($Path.TrimStart('/'))?$sent" } else { $Path.TrimStart('/') }
        $target = [uri]::new((Get-Base $Server $ForGame), $relative)
        $recordedPath = '/v1/' + $Path.TrimStart('/')
    }

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $target)
    $request.Headers.Add('Accept', 'application/json')
    if ($Auth -eq 'token') {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
    }

    if ($Absolute) {
        $request.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new(0, 0)
    }

    $response = $script:Http.Send($request)
    try {
        if ([int]$response.StatusCode -eq 429) {
            $wait = if ($response.Headers.RetryAfter) { " It asked to wait $($response.Headers.RetryAfter)." } else { '' }
            throw "mod.io asked to slow down (HTTP 429).$wait Nothing more was recorded; wait a few minutes, then run the script again."
        }

        $headers = [ordered]@{}
        if ($response.Content.Headers.ContentType) {
            $headers['Content-Type'] = $response.Content.Headers.ContentType.ToString()
        }

        foreach ($name in 'x-modio-error-ref', 'retry-after') {
            $values = $null
            if ($response.Headers.TryGetValues($name, [ref]$values)) {
                $headers[$name] = $values -join ', '
            }
        }

        if ($response.Headers.Location) {
            # Where a download leads, without its query, which may sign it.
            $location = $response.Headers.Location
            $headers['Location'] = if ($location.IsAbsoluteUri) { $location.GetLeftPart([UriPartial]::Path) } else { $location.OriginalString.Split('?')[0] }
        }

        $text = if ($Absolute) { '' } else { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        return [pscustomobject]@{
            Method  = 'GET'
            Server  = if ($Absolute) { 'download' } else { $Server }
            Auth    = $Auth
            Path    = $recordedPath
            Query   = $query
            Status  = [int]$response.StatusCode
            Headers = $headers
            Text    = $text
        }
    }
    finally {
        $response.Dispose()
        $request.Dispose()
    }
}

function ConvertTo-Node([string] $Text) {
    if (-not $Text) {
        return , [System.Text.Json.Nodes.JsonValue]::Create('')
    }

    try {
        return , [System.Text.Json.Nodes.JsonNode]::Parse($Text)
    }
    catch {
        # Not JSON, such as an HTML error page: kept as a string.
        return , [System.Text.Json.Nodes.JsonValue]::Create($Text)
    }
}

function Get-Text($Node) {
    if ($null -eq $Node) { return $null }
    return $Node.ToString()
}

# The body of an answer that had to succeed; stops the script, saying what failed, when it didn't.
function Read-Answer($Exchange, [string] $What) {
    if ($Exchange.Status -ne 200) {
        $ref = if ($Exchange.Headers.Contains('x-modio-error-ref')) { ", error $($Exchange.Headers['x-modio-error-ref'])" } else { '' }
        throw "mod.io didn't give $What (HTTP $($Exchange.Status)$ref). Check the credentials and parameters, then try again."
    }

    $body = ConvertTo-Node $Exchange.Text
    if ($body -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "mod.io gave $What as something other than a JSON object."
    }

    return , $body
}

# The records of a list's answer.
function Get-Records($Body) {
    $data = $Body['data']
    if ($data -is [System.Text.Json.Nodes.JsonArray]) {
        return @($data | Where-Object { $null -ne $_ })
    }

    return @()
}

function Limit-Array($Array, [int] $Count) {
    if ($Array -is [System.Text.Json.Nodes.JsonArray]) {
        while ($Array.Count -gt $Count) {
            $Array.RemoveAt($Array.Count - 1)
        }
    }
}

# Leaves out the long text the source never reads and cuts the lists it reads only the start of, wherever they are;
# hides the verification hash at the end of download links.
function Limit-Long($Node, [string] $Name) {
    if ($Node -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($key in $LeftOut) {
            if ($Node.ContainsKey($key)) {
                [void]$Node.Remove($key)
            }
        }

        if ($Name -eq 'download' -and $null -ne $Node['binary_url']) {
            $Node['binary_url'] = [System.Text.Json.Nodes.JsonValue]::Create((Hide-DownloadHash (Get-Text $Node['binary_url'])))
        }

        foreach ($property in @($Node)) {
            if ($Kept.ContainsKey($property.Key)) {
                Limit-Array $property.Value $Kept[$property.Key]
            }

            Limit-Long $property.Value $property.Key
        }
    }
    elseif ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        foreach ($item in $Node) {
            Limit-Long $item $null
        }
    }
}

# A download link without its verification hash: /files/<file>/download/<hash> keeps /files/<file>/download.
function Hide-DownloadHash([string] $Link) {
    return [regex]::Replace($Link, '(?<=/files/\d+/download/)[^/?#]+', $RedactedHash)
}

# Replaces you wherever you are: a user object that is yours, and your ID in a user_id.
function Hide-User($Node) {
    if ($Node -is [System.Text.Json.Nodes.JsonObject]) {
        $isMe = (Get-Text $Node['id']) -eq $script:Me.UserId -and ($Node.ContainsKey('username') -or $Node.ContainsKey('name_id'))
        if ($isMe) {
            $Node['id'] = [System.Text.Json.Nodes.JsonValue]::Create([long]$Stand.UserId)
            foreach ($text in @{ username = $Stand.Name; name_id = $Stand.NameId; profile_url = "https://mod.io/u/$($Stand.NameId)" }.GetEnumerator()) {
                if ($Node.ContainsKey($text.Key)) {
                    $Node[$text.Key] = [System.Text.Json.Nodes.JsonValue]::Create($text.Value)
                }
            }

            if ($Node.ContainsKey('display_name_portal')) {
                $Node['display_name_portal'] = $null
            }

            if ($Node['avatar'] -is [System.Text.Json.Nodes.JsonObject]) {
                foreach ($property in @($Node['avatar'])) {
                    $Node['avatar'][$property.Key] = [System.Text.Json.Nodes.JsonValue]::Create($(if ($property.Key -eq 'filename') { 'avatar.png' } else { 'https://example.com/avatar.png' }))
                }
            }
        }

        foreach ($property in @($Node)) {
            if ($property.Key -eq 'user_id' -and (Get-Text $property.Value) -eq $script:Me.UserId) {
                $Node['user_id'] = [System.Text.Json.Nodes.JsonValue]::Create([long]$Stand.UserId)
            }
            else {
                Hide-User $property.Value
            }
        }
    }
    elseif ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        foreach ($item in $Node) {
            Hide-User $item
        }
    }
}

# Fails if anything of yours is left: any 12 characters of the token or the key, your user ID in an address or a
# field, your name, your profile's address, this computer's name or your home folder.
function Assert-Clean([string] $Json, [string] $File) {
    $problems = @()
    foreach ($secret in $script:Secrets) {
        for ($start = 0; $start -le $secret.Value.Length - 12; $start++) {
            if ($Json.Contains($secret.Value.Substring($start, 12), [StringComparison]::Ordinal)) {
                $problems += "part of $($secret.Label)"
                break
            }
        }
    }

    $id = [regex]::Escape($script:Me.UserId)
    if ([regex]::IsMatch($Json, "u-$id(?!\d)|(?<=/)$id(?=[/""]|$)|""(id|user_id|submitted_by)"":\s*$id(?!\d)")) {
        $problems += 'your user ID'
    }

    foreach ($place in $script:Private) {
        if ([regex]::IsMatch($Json, $place.Pattern) -and $place.Label -notin $problems) {
            $problems += $place.Label
        }
    }

    if ($problems) {
        throw "$File would hold $($problems -join ', '). Nothing was written for it; tell the person who wrote this script."
    }
}

function Save-Fixture([string] $Name, $Exchange) {
    $body = ConvertTo-Node $Exchange.Text
    Limit-Long $body $null
    Hide-User $body

    $headers = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($header in $Exchange.Headers.GetEnumerator()) {
        $value = [string]$header.Value
        if ($header.Key -eq 'Location') {
            $value = Hide-DownloadHash $value
        }

        $headers[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create($value)
    }

    $request = [System.Text.Json.Nodes.JsonObject]::new()
    $request['method'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Method)
    $request['host'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Server)
    $request['auth'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Auth)
    $path = [regex]::Replace((Hide-DownloadHash $Exchange.Path), "(?<=/)$([regex]::Escape($script:Me.UserId))(?=/|$)", $Stand.UserId)
    $request['path'] = [System.Text.Json.Nodes.JsonValue]::Create($path)
    $request['query'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Query)

    $response = [System.Text.Json.Nodes.JsonObject]::new()
    $response['status'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Status)
    $response['headers'] = $headers
    $response['body'] = $body

    $record = [System.Text.Json.Nodes.JsonObject]::new()
    $record['request'] = $request
    $record['response'] = $response

    $json = $record.ToJsonString($JsonOptions)
    Assert-Clean $json "$Name.json"
    [System.IO.File]::WriteAllText((Join-Path $script:OutFolder "$Name.json"), $json.ReplaceLineEndings("`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    $script:Saved++
    $ref = if ($Exchange.Headers.Contains('x-modio-error-ref')) { " (error $($Exchange.Headers['x-modio-error-ref']))" } else { '' }
    Write-Host ("  {0,-40} {1}{2}" -f "$Name.json", $Exchange.Status, $ref)
}

# A list of the games, 1 a page, for its count.
function Get-GameCount([string] $What, [System.Collections.Specialized.OrderedDictionary] $Filter) {
    $parameters = [ordered]@{ '_limit' = 1 }
    if ($Filter) {
        foreach ($entry in $Filter.GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }
    }

    $answer = Invoke-ModIo $script:ListHost 'games' $parameters
    $count = [int](Get-Text (Read-Answer $answer $What)['result_total'])
    return [pscustomobject]@{ Answer = $answer; Count = $count }
}

# A page of the game's mods, as the source asks for one.
function Invoke-Mods([string] $Sort = '-date_live', [int] $Page = 1, [System.Collections.Specialized.OrderedDictionary] $Filter) {
    $parameters = [ordered]@{ '_sort' = $Sort; '_limit' = $PageSize; '_offset' = ($Page - 1) * $PageSize }
    if ($Filter) {
        foreach ($entry in $Filter.GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }
    }

    return Invoke-ModIo $script:GameHost "games/$Game/mods" $parameters
}

# Whether a game's api_access_options has a flag.
function Test-Access($GameNode, [int] $Flag) {
    return ([int](Get-Text $GameNode['api_access_options']) -band $Flag) -ne 0
}

# The first word of 3 letters or more, lowercase.
function Get-FirstWord([string] $Name) {
    return @($Name.ToLowerInvariant() -split '[^\p{L}\p{N}]+' | Where-Object { $_.Length -ge 3 }) | Select-Object -First 1
}

$Token = Get-Secret 'MODIO_TOKEN' 'Your personal access token, with the read scope'
$Key = Get-Secret 'MODIO_API_KEY' 'Your API key'
$userPath = if ($UserApi) { [pscustomobject]@{ Api = $UserApi; UserId = $null } } else { Get-UserPath }
$UserApi = $userPath.Api

$Secrets = [System.Collections.Generic.List[object]]::new()
$Secrets.Add([pscustomobject]@{ Label = 'your token'; Value = $Token })
$Secrets.Add([pscustomobject]@{ Label = 'your API key'; Value = $Key })

# What a file must never hold besides the secrets: filled in once mod.io says who you are.
$Private = [System.Collections.Generic.List[object]]::new()
$homeFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ($homeFolder.Length -ge 4) {
    $Private.Add([pscustomobject]@{ Label = 'your home folder'; Pattern = [regex]::Escape($homeFolder) })
    if ($homeFolder.Contains('\')) {
        $Private.Add([pscustomobject]@{ Label = 'your home folder'; Pattern = [regex]::Escape($homeFolder.Replace('\', '\\')) })
    }
}

if ([Environment]::MachineName.Length -ge 4) {
    $Private.Add([pscustomobject]@{ Label = 'this computer''s name'; Pattern = "(?i)(?<![\w.-])$([regex]::Escape([Environment]::MachineName))(?![\w.-])" })
}

# The recordings are written apart, and take the earlier ones' place once all are written.
$Recorded = Join-Path $PSScriptRoot 'Recorded'
$OutFolder = Join-Path ([System.IO.Path]::GetTempPath()) "modio-fixtures-$([guid]::NewGuid().ToString('N'))"
$Saved = 0

$Handler = [System.Net.Http.SocketsHttpHandler]::new()
$Handler.AllowAutoRedirect = $false
$Handler.UseCookies = $false
$Handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$Http = [System.Net.Http.HttpClient]::new($Handler)
$Http.Timeout = [TimeSpan]::FromSeconds(30)
$Http.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

try {
    New-Item -ItemType Directory -Path $OutFolder | Out-Null

    # Who the token belongs to, asked of your API path, then of api.mod.io.
    $ListHost = 'user'
    $meAnswer = Invoke-ModIo 'user' 'me'
    if ($meAnswer.Status -ne 200) {
        $ListHost = 'default'
        $meDefault = Invoke-ModIo 'default' 'me'
        if ($meDefault.Status -ne 200) {
            throw "mod.io refused the token on your API path (HTTP $($meAnswer.Status)) and on api.mod.io (HTTP $($meDefault.Status)). Check it has the read scope and hasn't expired, then try again."
        }

        $meAnswer = $meDefault
    }

    $meBody = Read-Answer $meAnswer 'who the token belongs to'
    $Me = @{ UserId = Get-Text $meBody['id']; Name = Get-Text $meBody['username']; Profile = Get-Text $meBody['profile_url'] }
    if (-not $Me.UserId -or -not $Me.Name) {
        throw 'mod.io answered without a user ID or name, so the script cannot tell what to replace.'
    }

    if ($userPath.UserId -and $userPath.UserId -ne $Me.UserId) {
        throw 'The token and the API path are of two different accounts. Copy both from the same API Access page.'
    }

    # Your name as a whole word, in its own case, as a mod's name could have it in another; and your profile's address.
    foreach ($place in @{ 'your name' = $Me.Name; 'your profile''s address' = $Me.Profile }.GetEnumerator()) {
        if ($place.Value) {
            $Private.Add([pscustomobject]@{ Label = $place.Key; Pattern = "(?<![\w.-])$([regex]::Escape($place.Value))(?![\w.-])" })
        }
    }

    Write-Host "Recording into $Recorded"
    Write-Host "  The token answers on the $ListHost host; the key is $($Key.Length) characters, $(if ($Key -match '^[0-9a-fA-F]+$') { 'hexadecimal' } else { 'not all hexadecimal' }); the token $($Token.Length)."
    Save-Fixture 'me' $meAnswer

    # Which hosts answer which credential.
    Save-Fixture 'probe-me-user' (Invoke-ModIo 'user' 'me')
    Save-Fixture 'probe-me-default' (Invoke-ModIo 'default' 'me')
    Save-Fixture 'probe-games-user-token' (Invoke-ModIo 'user' 'games' ([ordered]@{ '_limit' = 1 }))
    Save-Fixture 'probe-games-default-token' (Invoke-ModIo 'default' 'games' ([ordered]@{ '_limit' = 1 }))
    Save-Fixture 'probe-games-user-key' (Invoke-ModIo 'user' 'games' ([ordered]@{ '_limit' = 1 }) -Auth key)
    Save-Fixture 'probe-games-default-key' (Invoke-ModIo 'default' 'games' ([ordered]@{ '_limit' = 1 }) -Auth key)
    Save-Fixture 'user' (Invoke-ModIo 'user' "users/$($Me.UserId)" -Auth key)

    # How many games there are, and how many let third parties in, download directly, or only with a token.
    $counts = [ordered]@{}
    $all = Get-GameCount 'the games'
    Save-Fixture 'games-count' $all.Answer
    $counts['all'] = $all.Count
    foreach ($flag in ([ordered]@{ 'open' = $Open; 'direct' = $DirectDownloads; 'token' = $TokenDownloads }).GetEnumerator()) {
        $counted = Get-GameCount "the games with flag $($flag.Value)" ([ordered]@{ 'api_access_options-bitwise-and' = $flag.Value })
        Save-Fixture "games-count-$($flag.Key)" $counted.Answer
        $counts[$flag.Key] = $counted.Count
    }

    Write-Host ("  Games: {0} in all, {1} open to third parties, {2} with direct downloads, {3} with downloads only with a token." -f $counts['all'], $counts['open'], $counts['direct'], $counts['token'])

    # The games open to third parties, by name, as the source lists them; and the most popular of all, open or not.
    $openPage = Invoke-ModIo $ListHost 'games' ([ordered]@{ 'api_access_options-bitwise-and' = $Open; '_sort' = 'name'; '_limit' = 100; '_offset' = 0 })
    Save-Fixture 'games-open-1' $openPage
    if ($counts['open'] -gt 100) {
        Save-Fixture 'games-open-2' (Invoke-ModIo $ListHost 'games' ([ordered]@{ 'api_access_options-bitwise-and' = $Open; '_sort' = 'name'; '_limit' = 100; '_offset' = 100 }))
    }

    $popular = Invoke-ModIo $ListHost 'games' ([ordered]@{ '_sort' = '-downloads_total'; '_limit' = 30 })
    Save-Fixture 'games-popular' $popular
    $popularGames = Get-Records (Read-Answer $popular 'the most popular games')

    # The game: given, or the open one with direct downloads whose mods were downloaded most.
    if (-not $Game) {
        $openPopular = Invoke-ModIo $ListHost 'games' ([ordered]@{ 'api_access_options-bitwise-and' = $Open; '_sort' = '-downloads_total'; '_limit' = 30 })
        $chosen = @(Get-Records (Read-Answer $openPopular 'the most popular open games') | Where-Object { Test-Access $_ $DirectDownloads }) | Select-Object -First 1
        if (-not $chosen) {
            throw 'No popular game open to third parties downloads directly. Give one with -Game.'
        }

        $Game = [int](Get-Text $chosen['id'])
    }

    $GameHost = 'game'
    $gameAnswer = Invoke-ModIo 'game' "games/$Game"
    if ($gameAnswer.Status -ne 200) {
        $GameHost = $ListHost
        $gameAnswer = Invoke-ModIo $ListHost "games/$Game"
    }

    $gameBody = Read-Answer $gameAnswer "game $Game"
    $gameName = Get-Text $gameBody['name']
    $gameNameId = Get-Text $gameBody['name_id']
    if (-not (Test-Access $gameBody $Open)) {
        throw "$gameName ($Game) doesn't let third parties use its API. Give another game with -Game."
    }

    Write-Host "  The game: $gameName ($Game), asked of the $GameHost host."
    Save-Fixture 'game' $gameAnswer
    Save-Fixture 'game-key' (Invoke-ModIo $GameHost "games/$Game" -Auth key)
    Save-Fixture 'game-by-name-id' (Invoke-ModIo $ListHost 'games' ([ordered]@{ 'name_id' = $gameNameId }))
    $word = Get-FirstWord $gameName
    if ($word) {
        Save-Fixture 'games-find' (Invoke-ModIo $ListHost 'games' ([ordered]@{ '_q' = $word; 'api_access_options-bitwise-and' = $Open; '_limit' = 100 }))
    }

    # Which hosts answer which credential for the game's own endpoints.
    Save-Fixture 'probe-mods-game-token' (Invoke-ModIo 'game' "games/$Game/mods" ([ordered]@{ '_limit' = 1 }))
    Save-Fixture 'probe-mods-game-key' (Invoke-ModIo 'game' "games/$Game/mods" ([ordered]@{ '_limit' = 1 }) -Auth key)
    Save-Fixture 'probe-mods-user-token' (Invoke-ModIo 'user' "games/$Game/mods" ([ordered]@{ '_limit' = 1 }))
    Save-Fixture 'probe-mods-user-key' (Invoke-ModIo 'user' "games/$Game/mods" ([ordered]@{ '_limit' = 1 }) -Auth key)
    Save-Fixture 'probe-me-game' (Invoke-ModIo 'game' 'me')

    # A popular game closed to third parties, as a user's key and token are refused there.
    $closed = @($popularGames | Where-Object { -not (Test-Access $_ $Open) }) | Select-Object -First 1
    if ($closed) {
        $closedId = [int](Get-Text $closed['id'])
        Save-Fixture 'game-closed-mods' (Invoke-ModIo 'game' "games/$closedId/mods" ([ordered]@{ '_limit' = 1 }) -ForGame $closedId)
        Save-Fixture 'game-closed-mods-key' (Invoke-ModIo 'game' "games/$closedId/mods" ([ordered]@{ '_limit' = 1 }) -ForGame $closedId -Auth key)
    }
    else {
        Write-Host '  The 30 most popular games are all open to third parties, so no closed game was recorded.'
    }

    Save-Fixture 'game-not-found' (Invoke-ModIo $ListHost "games/$MissingId")

    # The game's mods in each order; the newest and the latest updated two pages deep.
    $newest = $null
    foreach ($sort in $Sorts.GetEnumerator()) {
        $first = Invoke-Mods $sort.Value
        Save-Fixture "mods-$($sort.Key)-1" $first
        if ($sort.Key -eq 'newest') {
            $newest = Read-Answer $first 'the newest mods'
        }

        if ($sort.Key -in 'newest', 'updated') {
            Save-Fixture "mods-$($sort.Key)-2" (Invoke-Mods $sort.Value 2)
        }
    }

    # Narrowed by the author and the first tag of the first newest mod with them, by update date and explicit content.
    $records = Get-Records $newest
    $withAuthor = @($records | Where-Object { $null -ne $_['submitted_by'] -and (Get-Text $_['submitted_by']['id']) -ne $Me.UserId }) | Select-Object -First 1
    if ($withAuthor) {
        Save-Fixture 'mods-author' (Invoke-Mods -Filter ([ordered]@{ 'submitted_by' = (Get-Text $withAuthor['submitted_by']['id']) }))
        Save-Fixture 'mods-author-name' (Invoke-Mods -Filter ([ordered]@{ 'submitted_by_display_name' = (Get-Text $withAuthor['submitted_by']['username']) }))
    }

    $withTag = @($records | Where-Object { $_['tags'] -is [System.Text.Json.Nodes.JsonArray] -and $_['tags'].Count -gt 0 }) | Select-Object -First 1
    if ($withTag) {
        Save-Fixture 'mods-tag' (Invoke-Mods -Filter ([ordered]@{ 'tags' = (Get-Text $withTag['tags'][0]['name']) }))
    }

    $week = [DateTimeOffset]::UtcNow.AddDays(-7)
    $hour = [DateTimeOffset]::new($week.Year, $week.Month, $week.Day, $week.Hour, 0, 0, [TimeSpan]::Zero)
    Save-Fixture 'mods-updated-week' (Invoke-Mods '-date_updated' -Filter ([ordered]@{ 'date_updated-min' = $hour.ToUnixTimeSeconds() }))
    Save-Fixture 'mods-explicit' (Invoke-Mods -Filter ([ordered]@{ 'maturity_option-bitwise-and' = $Explicit }))

    # The mod: given, or of the 20 most subscribed, the first with 3 files or more, one with dependencies first.
    if (-not $ModId) {
        $top = Get-Records (Read-Answer (Invoke-Mods '-subscribers_total' -Filter ([ordered]@{ '_limit' = 20 })) 'the most subscribed mods')
        $candidates = @($top | Where-Object { (Get-Text $_['dependencies']) -eq 'true' }) + @($top | Where-Object { (Get-Text $_['dependencies']) -ne 'true' })
        foreach ($candidate in $candidates) {
            $candidateId = Get-Text $candidate['id']
            $fileCount = [int](Get-Text (Read-Answer (Invoke-ModIo $GameHost "games/$Game/mods/$candidateId/files" ([ordered]@{ '_limit' = 1 })) "mod $candidateId's files")['result_total'])
            if ($fileCount -ge 3) {
                $ModId = [int]$candidateId
                break
            }
        }

        if (-not $ModId) {
            throw "None of $gameName's 20 most subscribed mods has 3 files or more. Give one with -ModId."
        }
    }

    $modAnswer = Invoke-ModIo $GameHost "games/$Game/mods/$ModId"
    $modBody = Read-Answer $modAnswer "mod $ModId"
    $modName = Get-Text $modBody['name']
    Write-Host "  The mod: $modName ($ModId)."
    Save-Fixture 'mod' $modAnswer
    Save-Fixture 'mod-by-name-id' (Invoke-ModIo $GameHost "games/$Game/mods" ([ordered]@{ 'name_id' = (Get-Text $modBody['name_id']) }))
    Save-Fixture 'mod-files' (Invoke-ModIo $GameHost "games/$Game/mods/$ModId/files" ([ordered]@{ '_sort' = '-date_added'; '_limit' = 100 }))
    $liveFile = $modBody['modfile']
    if ($liveFile -is [System.Text.Json.Nodes.JsonObject]) {
        $liveId = Get-Text $liveFile['id']
        $fileAnswer = Invoke-ModIo $GameHost "games/$Game/mods/$ModId/files/$liveId"
        Save-Fixture 'mod-file' $fileAnswer
        $binary = Get-Text (Read-Answer $fileAnswer "file $liveId")['download']['binary_url']
        if ($binary) {
            Save-Fixture 'download-token' (Invoke-ModIo -Absolute ([uri]$binary) -Auth token)
            Save-Fixture 'download-none' (Invoke-ModIo -Absolute ([uri]$binary) -Auth none)
        }
    }
    else {
        Write-Host "  $modName has no live file, so none was recorded, nor a download."
    }

    Save-Fixture 'mod-dependencies' (Invoke-ModIo $GameHost "games/$Game/mods/$ModId/dependencies")
    Save-Fixture 'mod-dependencies-recursive' (Invoke-ModIo $GameHost "games/$Game/mods/$ModId/dependencies" ([ordered]@{ 'recursive' = 'true' }))
    Save-Fixture 'mod-not-found' (Invoke-ModIo $GameHost "games/$Game/mods/$MissingId")
    Save-Fixture 'file-not-found' (Invoke-ModIo $GameHost "games/$Game/mods/$ModId/files/$MissingId")

    # Searches by words, two pages when there are more than one.
    $words = if ($SearchWords) { $SearchWords } else { Get-FirstWord $modName }
    if ($words) {
        $found = Invoke-Mods -Filter ([ordered]@{ '_q' = $words })
        Save-Fixture 'search-1' $found
        if ([int](Get-Text (Read-Answer $found 'a search')['result_total']) -gt $PageSize) {
            Save-Fixture 'search-2' (Invoke-Mods -Page 2 -Filter ([ordered]@{ '_q' = $words }))
        }
    }

    Save-Fixture 'search-nothing' (Invoke-Mods -Filter ([ordered]@{ '_q' = $NoMatch }))

    # The game's file events: since 28 days ago, the latest of any kind, and the oldest, for how far back they reach.
    $since = [DateTimeOffset]::UtcNow.AddDays(-28).ToUnixTimeSeconds()
    Save-Fixture 'events-files' (Invoke-ModIo $GameHost "games/$Game/mods/events" ([ordered]@{ 'event_type' = 'MODFILE_CHANGED'; 'date_added-min' = $since; '_limit' = 100 }))
    Save-Fixture 'events-latest' (Invoke-ModIo $GameHost "games/$Game/mods/events" ([ordered]@{ '_limit' = 20 }))
    Save-Fixture 'events-oldest' (Invoke-ModIo $GameHost "games/$Game/mods/events" ([ordered]@{ '_sort' = 'date_added'; '_limit' = 1 }))

    # Your subscriptions, with the token and with the key.
    Save-Fixture 'subscribed' (Invoke-ModIo $ListHost 'me/subscribed' ([ordered]@{ '_limit' = 100 }))
    Save-Fixture 'subscribed-key' (Invoke-ModIo $ListHost 'me/subscribed' ([ordered]@{ '_limit' = 100 }) -Auth key)

    # A token and a key mod.io refuses.
    Save-Fixture 'me-refused' (Invoke-ModIo $ListHost 'me' -Token 'not-a-real-token')
    Save-Fixture 'games-refused-key' (Invoke-ModIo $ListHost 'games' ([ordered]@{ '_limit' = 1 }) -Auth key -Key ('0' * 32))

    New-Item -ItemType Directory -Force -Path $Recorded | Out-Null
    Get-ChildItem -Path $Recorded -Filter '*.json' -File | Remove-Item
    Get-ChildItem -Path $OutFolder -Filter '*.json' -File | Move-Item -Destination $Recorded
    Write-Host "Done: $Saved files. Revoke the token on mod.io's API Access page, review the files, then tell Claude they are there."
}
finally {
    $Http.Dispose()
    if (Test-Path -LiteralPath $OutFolder) {
        Remove-Item -LiteralPath $OutFolder -Recurse -Force
    }
}
