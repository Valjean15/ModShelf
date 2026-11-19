<#
.SYNOPSIS
    Records the CurseForge answers that the CurseForge source's tests replay.

.DESCRIPTION
    Calls CurseForge's Core API as the source does, with your own API key, and saves each answer as a JSON file under
    Recorded, next to this script. The tests serve these files with WireMock.Net, so CI never calls CurseForge.

    The key is read from the CURSEFORGE_API_KEY environment variable, or asked for without being shown. It's sent only
    as the x-api-key header, and never written: requests are saved without their headers, and every file is checked for
    the key, this computer's name and your home folder, and the script stops before writing a file that has one. Review
    the files before committing them anyway. A request sent without your key says so in its "key": null when it had
    none, or the made-up key it had in its place.

    It records, for one game and one of its mods:
    - every page of CurseForge's games, and the game's classes, such as Minecraft's Mods and Resource Packs;
    - the mod, its files a page at a time (up to 10 pages, as the source reads them), and its main file;
    - the mods its main file needs, asked for together, as the source asks;
    - two pages of a search of the mod's class, 50 mods each, which the tests search again in each order, and the mod
      found by its slug, as a link to it is read;
    - a mod that doesn't exist; the game asked for without a key, and its main file's changelog with a key CurseForge
      doesn't take. CurseForge once answered a game's classes without a key, so these ask for what it's known to
      refuse without one.

    The files keep only what's left once each file's "modules" and "sortableGameVersions", and each mod's
    "latestFilesIndexes", "latestEarlyAccessFilesIndexes" and "screenshots", which the source doesn't read, are taken
    out, so they stay small.

    A run that completes replaces the earlier recordings; one that stops leaves them as they were. It makes about 17
    requests, one at a time with a pause between them, and stops if CurseForge asks it to slow down.

.PARAMETER Game
    The game's slug, as in curseforge.com/<game>/. Minecraft by default.

.PARAMETER Mod
    The mod's number on CurseForge: one with more than 50 files, whose main file needs another mod, is best. Mod Menu
    (308702) by default, which needs Fabric API.

.PARAMETER Search
    The words searched for in the mod's class.

.EXAMPLE
    ./Record-CurseForgeFixtures.ps1

.EXAMPLE
    ./Record-CurseForgeFixtures.ps1 -Game minecraft -Mod 238222 -Search map
#>
#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')]
    [string] $Game = 'minecraft',

    [ValidateRange(1, [long]::MaxValue)]
    [long] $Mod = 308702,

    [ValidatePattern('^[A-Za-z0-9 ]+$')]
    [string] $Search = 'map',

    # Only for testing this script against a local fake of CurseForge.
    [uri] $ApiBase = 'https://api.curseforge.com/'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The pause between requests.
$PauseMilliseconds = 400

# A page of games, files and searched mods, as the source asks for them.
$PageSize = 50
$SearchPageSize = 50
$SearchPages = 2

# The most pages of a mod's files the source reads.
$MaxFilePages = 10

# A mod no one has: CurseForge's numbers start far above it.
$MissingMod = 1

# A key of the shape CurseForge's have, that no one has.
$WrongKey = '$2a$10$' + ('0' * 53)

$UserAgent = 'ModShelf-fixtures/1.0'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

# A request to CurseForge's API, with the key unless -NoKey, or with -WrongKey in its place; -Body posts it as JSON.
function Invoke-CurseForge {
    param([string] $Target, [string] $Body, [switch] $NoKey, [string] $WrongKey)

    Start-Sleep -Milliseconds $PauseMilliseconds
    $uri = [uri]::new($ApiBase, $Target)
    $method = if ($Body) { [System.Net.Http.HttpMethod]::Post } else { [System.Net.Http.HttpMethod]::Get }
    $request = [System.Net.Http.HttpRequestMessage]::new($method, $uri)
    $request.Headers.Accept.ParseAdd('application/json')
    if ($WrongKey) {
        $request.Headers.TryAddWithoutValidation('x-api-key', $WrongKey) | Out-Null
    }
    elseif (-not $NoKey) {
        $request.Headers.TryAddWithoutValidation('x-api-key', $script:Key) | Out-Null
    }

    if ($Body) {
        $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, 'application/json')
    }

    $response = $script:Http.Send($request)
    try {
        if ([int]$response.StatusCode -eq 429) {
            throw 'CurseForge asked to slow down (HTTP 429). Nothing more was recorded; wait a while, then run the script again.'
        }

        $headers = [ordered]@{}
        if ($response.Content.Headers.ContentType) {
            $headers['Content-Type'] = $response.Content.Headers.ContentType.ToString()
        }

        return [pscustomobject]@{
            Key    = if ($WrongKey) { $WrongKey } elseif ($NoKey) { 'none' } else { 'yours' }
            Method = $method.Method
            Path   = $uri.AbsolutePath
            Query  = $uri.Query.TrimStart('?')
            Body   = $Body
            Status = [int]$response.StatusCode
            Headers = $headers
            Text   = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
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

# Fails if the file would hold the key or anything of this computer's.
function Assert-Clean([string] $Json, [string] $File) {
    $problems = @()
    foreach ($place in $script:Private) {
        if ($Json.Contains($place.Value, [StringComparison]::Ordinal) -and $place.Label -notin $problems) {
            $problems += $place.Label
        }
    }

    if ($problems) {
        throw "$File would hold $($problems -join ', '). Nothing was written for it; tell the person who wrote this script."
    }
}

# Saves an exchange; its body as JSON when it is, given as $Body when it was cut.
function Save-Fixture([string] $Name, $Exchange, $Body) {
    if ($null -eq $Body) {
        $Body = ConvertTo-Node $Exchange.Text
    }

    $request = [System.Text.Json.Nodes.JsonObject]::new()
    $request['method'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Method)
    $request['path'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Path)
    $request['query'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Query)
    if ($Exchange.Body) {
        $request['body'] = ConvertTo-Node $Exchange.Body
    }

    # Your key isn't written; the absence of one, or the made-up key in its place, is.
    if ($Exchange.Key -eq 'none') {
        $request.Add('key', $null)
    }
    elseif ($Exchange.Key -ne 'yours') {
        $request['key'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Key)
    }

    $headers = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($header in $Exchange.Headers.GetEnumerator()) {
        $headers[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$header.Value)
    }

    $response = [System.Text.Json.Nodes.JsonObject]::new()
    $response['status'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Status)
    $response['headers'] = $headers
    $response['body'] = $Body

    $record = [System.Text.Json.Nodes.JsonObject]::new()
    $record['request'] = $request
    $record['response'] = $response

    $json = $record.ToJsonString($JsonOptions)
    Assert-Clean $json "$Name.json"
    [System.IO.File]::WriteAllText((Join-Path $script:OutFolder "$Name.json"), $json.ReplaceLineEndings("`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    $script:Saved++
    Write-Host ("  {0,-40} {1}" -f "$Name.json", $Exchange.Status)
}

# The body of an answer that had to succeed; stops the script, saying what failed, when it didn't.
function Read-Answer($Exchange, [string] $What) {
    if ($Exchange.Status -eq 403) {
        throw "CurseForge refused the API key while asking for $What (HTTP 403). Check the key on the CurseForge for Studios console, then try again."
    }

    if ($Exchange.Status -ne 200) {
        throw "CurseForge didn't give $What (HTTP $($Exchange.Status)). Check the parameters, then try again."
    }

    $body = ConvertTo-Node $Exchange.Text
    if ($body -isnot [System.Text.Json.Nodes.JsonObject]) {
        throw "CurseForge gave $What as something other than JSON."
    }

    return , $body
}

# The objects in $Node: itself when it's one, each in it when it's a list. (A JSON object given to @() or foreach
# would be taken apart into its properties.)
function Get-Objects([System.Text.Json.Nodes.JsonNode] $Node) {
    if ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        return , @($Node | Where-Object { $_ -is [System.Text.Json.Nodes.JsonObject] })
    }

    return , @(if ($Node -is [System.Text.Json.Nodes.JsonObject]) { , $Node })
}

# Takes out what the source doesn't read of a file, or of each in a list, so the recordings stay small.
function Compress-Files([System.Text.Json.Nodes.JsonNode] $Files) {
    foreach ($file in (Get-Objects $Files)) {
        $file.Remove('modules') | Out-Null
        $file.Remove('sortableGameVersions') | Out-Null
    }
}

# Takes out what the source doesn't read of a mod, or of each in a list, its files' too, so the recordings stay small.
function Compress-Mods([System.Text.Json.Nodes.JsonNode] $Mods) {
    foreach ($listed in (Get-Objects $Mods)) {
        $listed.Remove('latestFilesIndexes') | Out-Null
        $listed.Remove('latestEarlyAccessFilesIndexes') | Out-Null
        $listed.Remove('screenshots') | Out-Null
        Compress-Files $listed['latestFiles']
    }
}

$Key = $env:CURSEFORGE_API_KEY
if (-not $Key) {
    $secure = Read-Host 'CurseForge API key (from console.curseforge.com, not shown)' -AsSecureString
    $Key = [System.Net.NetworkCredential]::new('', $secure).Password
}

$Key = $Key.Trim()
if ($Key.Length -lt 30) {
    throw 'That isn''t a CurseForge API key. Make one on the API keys page of the CurseForge for Studios console.'
}

# What a file must never hold: the key, this computer's name and your home folder, as written in JSON too.
$Private = [System.Collections.Generic.List[object]]::new()
$Private.Add([pscustomobject]@{ Label = 'your API key'; Value = $Key })
$Private.Add([pscustomobject]@{ Label = 'your API key'; Value = $Key.Replace('$', '$') })
$homeFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ($homeFolder.Length -ge 4) {
    $Private.Add([pscustomobject]@{ Label = 'your home folder'; Value = $homeFolder })
    if ($homeFolder.Contains('\')) {
        $Private.Add([pscustomobject]@{ Label = 'your home folder'; Value = $homeFolder.Replace('\', '\\') })
    }
}

if ([Environment]::MachineName.Length -ge 4) {
    $Private.Add([pscustomobject]@{ Label = 'this computer''s name'; Value = [Environment]::MachineName })
}

# The recordings are written apart, and take the earlier ones' place once all are written.
$Recorded = Join-Path $PSScriptRoot 'Recorded'
$OutFolder = Join-Path ([System.IO.Path]::GetTempPath()) "curseforge-fixtures-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null
$Saved = 0

$Handler = [System.Net.Http.SocketsHttpHandler]::new()
$Handler.AllowAutoRedirect = $false
$Handler.UseCookies = $false
$Handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$Http = [System.Net.Http.HttpClient]::new($Handler)
$Http.Timeout = [TimeSpan]::FromSeconds(60)
$Http.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

try {
    Write-Host "Recording $Game and its mod $Mod into $Recorded"

    # Every page of games, and the game's ID.
    $gameId = $null
    for ($page = 0; ; $page++) {
        $answer = Invoke-CurseForge "v1/games?index=$($page * $PageSize)&pageSize=$PageSize"
        $body = Read-Answer $answer 'the games'
        Save-Fixture "games-$($page + 1)" $answer $body
        foreach ($listed in $body['data'].AsArray()) {
            if ($listed['slug'].ToString() -eq $Game) {
                $gameId = [long]$listed['id'].ToString()
            }
        }

        $count = $body['data'].AsArray().Count
        $total = [long]$body['pagination']['totalCount'].ToString()
        if ($count -lt $PageSize -or ($page * $PageSize) + $count -ge $total) {
            break
        }
    }

    if (-not $gameId) {
        throw "CurseForge has no game called $Game. Give another to -Game."
    }

    # The game's classes.
    Save-Fixture 'classes' (Invoke-CurseForge "v1/categories?gameId=$gameId&classesOnly=true")

    # The mod, and its class and slug.
    $modAnswer = Invoke-CurseForge "v1/mods/$Mod"
    $modBody = Read-Answer $modAnswer "the mod $Mod"
    if ([long]$modBody['data']['gameId'].ToString() -ne $gameId) {
        throw "The mod $Mod isn't one of $Game's. Give another to -Mod."
    }

    Compress-Mods $modBody['data']
    Save-Fixture 'mod' $modAnswer $modBody
    $classId = $modBody['data']['classId'].ToString()
    $slug = $modBody['data']['slug'].ToString()
    $mainFileId = $modBody['data']['mainFileId'].ToString()

    # Its files, a page at a time, as the source reads them.
    for ($page = 0; $page -lt $MaxFilePages; $page++) {
        $answer = Invoke-CurseForge "v1/mods/$Mod/files?index=$($page * $PageSize)&pageSize=$PageSize"
        $body = Read-Answer $answer "the mod's files"
        Compress-Files $body['data']
        Save-Fixture "files-$($page + 1)" $answer $body
        $count = $body['data'].AsArray().Count
        $total = [long]$body['pagination']['totalCount'].ToString()
        if ($count -lt $PageSize -or ($page * $PageSize) + $count -ge $total) {
            break
        }
    }

    # Its main file, and the mods that file needs, asked for together.
    $fileAnswer = Invoke-CurseForge "v1/mods/$Mod/files/$mainFileId"
    $fileBody = Read-Answer $fileAnswer "the mod's main file"
    Compress-Files $fileBody['data']
    Save-Fixture 'file' $fileAnswer $fileBody
    $needed = @(
        foreach ($dependency in $fileBody['data']['dependencies'].AsArray()) {
            if ([int]$dependency['relationType'].ToString() -eq 3 -and [long]$dependency['modId'].ToString() -ne $Mod) {
                [long]$dependency['modId'].ToString()
            }
        }
    ) | Select-Object -Unique
    if ($needed) {
        $modsAnswer = Invoke-CurseForge 'v1/mods' -Body (@{ modIds = @($needed) } | ConvertTo-Json -Compress)
        $modsBody = Read-Answer $modsAnswer 'the mods the main file needs'
        Compress-Mods $modsBody['data']
        Save-Fixture 'mods' $modsAnswer $modsBody
    }
    else {
        Write-Host '  The main file needs no other mod: no mods.json. Give -Mod one whose main file does.'
    }

    # Pages of a search of the mod's class, and the mod by its slug.
    for ($page = 0; $page -lt $SearchPages; $page++) {
        $searchAnswer = Invoke-CurseForge "v1/mods/search?gameId=$gameId&classId=$classId&searchFilter=$([uri]::EscapeDataString($Search))&sortField=2&sortOrder=desc&index=$($page * $SearchPageSize)&pageSize=$SearchPageSize"
        $searchBody = Read-Answer $searchAnswer "a search for $Search"
        Compress-Mods $searchBody['data']
        Save-Fixture "search-$($page + 1)" $searchAnswer $searchBody
    }
    $slugAnswer = Invoke-CurseForge "v1/mods/search?gameId=$gameId&classId=$classId&slug=$slug"
    $slugBody = Read-Answer $slugAnswer "the mod by its slug, $slug"
    Compress-Mods $slugBody['data']
    Save-Fixture 'slug' $slugAnswer $slugBody

    # A mod that doesn't exist; and, for what CurseForge needs a key for, a request without the key and one with a key
    # it doesn't take.
    Save-Fixture 'mod-not-found' (Invoke-CurseForge "v1/mods/$MissingMod")
    Save-Fixture 'no-key' (Invoke-CurseForge "v1/games/$gameId" -NoKey)
    Save-Fixture 'wrong-key' (Invoke-CurseForge "v1/mods/$Mod/files/$mainFileId/changelog" -WrongKey $WrongKey)

    New-Item -ItemType Directory -Force -Path $Recorded | Out-Null
    Get-ChildItem -Path $Recorded -Filter '*.json' -File | Remove-Item
    Get-ChildItem -Path $OutFolder -Filter '*.json' -File | Move-Item -Destination $Recorded
    Write-Host "Done: $Saved files. Review them before committing them."
}
finally {
    $Http.Dispose()
    if (Test-Path -LiteralPath $OutFolder) {
        Remove-Item -LiteralPath $OutFolder -Recurse -Force
    }
}
