<#
.SYNOPSIS
    Records the Nexus Mods API responses that the Nexus source's tests replay, with personal data replaced.

.DESCRIPTION
    Calls the Nexus Mods API with your own API key and saves each response as a JSON file under
    Recorded/<account>, next to this script. The tests serve these files with WireMock.Net, so CI never calls
    the live API.

    Run it once with a Premium account's key and once with a free account's key.

    Before a file is written, the key, your user ID, name and email, nxm:// keys and the tokens in download
    links are replaced with placeholders. Every file is then checked for them, and the script stops if one is
    left. Review the files before committing them anyway.

    The key comes from the NEXUS_API_KEY environment variable, or is asked for without being shown. It's
    never written to a file or the screen.

.PARAMETER Account
    Premium or Free: the kind of account the key belongs to. Premium records the game, mod, file, changelog,
    update and tracked-mod responses too; Free records only what differs for a free account.

.PARAMETER Game
    The game's domain name on Nexus Mods, as in nexusmods.com/<game>/mods/<id>.

.PARAMETER ModId
    The mod to record. Its newest main file is used for the file and download-link responses.

.PARAMETER NxmLink
    Free only, optional: a fresh nxm:// link from the "Mod Manager Download" button of a file. These links
    expire, so use one you just got. Records the download link Nexus Mods gives for its key, and how it
    refuses a key it never handed over. The script says when the link expires.

.PARAMETER ExpiredNxmLink
    Free only, optional: an nxm:// link that has expired, such as the one given to -NxmLink earlier, once the
    time the script gave has passed. Records how Nexus Mods refuses an expired key.

.EXAMPLE
    ./Record-NexusFixtures.ps1 -Account Premium

.EXAMPLE
    ./Record-NexusFixtures.ps1 -Account Free -NxmLink 'nxm://stardewvalley/mods/2400/files/...'

.EXAMPLE
    ./Record-NexusFixtures.ps1 -Account Free -ExpiredNxmLink 'nxm://stardewvalley/mods/2400/files/...'
#>

#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Premium', 'Free')]
    [string] $Account,

    [ValidatePattern('^[a-z0-9]+$')]
    [string] $Game = 'stardewvalley',

    [ValidateRange(1, [int]::MaxValue)]
    [int] $ModId = 2400,

    [string] $NxmLink,

    [string] $ExpiredNxmLink,

    # Only for testing this script against a local fake of the API.
    [uri] $ApiBase = 'https://api.nexusmods.com/'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Account -eq 'Premium' -and ($NxmLink -or $ExpiredNxmLink)) {
    throw 'nxm:// links are only recorded for a free account. Run with -Account Free.'
}

$ApplicationName = 'ModShelf'
$ApplicationVersion = '0.1.0-dev'

# What replaces your own details in the recordings.
$Stand = if ($Account -eq 'Premium') {
    @{ UserId = '1000001'; Name = 'ModShelfPremiumTester'; Email = 'premium.tester@example.com' }
}
else {
    @{ UserId = '1000002'; Name = 'ModShelfFreeTester'; Email = 'free.tester@example.com' }
}

$Redacted = 'REDACTED'
$RedactedKey = 'REDACTED-API-KEY'
$RedactedNxmKey = 'REDACTED-NXM-KEY'
$MadeUpNxmKey = 'not-a-real-nxm-key'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function Get-ApiKey {
    $key = $env:NEXUS_API_KEY
    if ([string]::IsNullOrWhiteSpace($key)) {
        $secure = Read-Host -AsSecureString -Prompt "API key of your $Account account (not shown)"
        $key = [System.Net.NetworkCredential]::new('', $secure).Password
    }

    $key = $key.Trim()
    if (-not $key) {
        throw 'No API key was entered.'
    }

    return $key
}

function Invoke-Nexus {
    param([string] $Path, [string] $Query = '', [string] $Key = $script:ApiKey)

    $target = if ($Query) { "${Path}?$Query" } else { $Path }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $target.TrimStart('/'))
    $request.Headers.Add('apikey', $Key)
    $request.Headers.Add('Application-Name', $ApplicationName)
    $request.Headers.Add('Application-Version', $ApplicationVersion)
    $request.Headers.Add('Accept', 'application/json')

    $response = $script:Http.Send($request)
    try {
        $headers = [ordered]@{}
        if ($response.Content.Headers.ContentType) {
            $headers['Content-Type'] = $response.Content.Headers.ContentType.ToString()
        }

        foreach ($header in $response.Headers) {
            if ($header.Key -like 'X-RL-*') {
                $headers[$header.Key] = $header.Value -join ', '
            }
        }

        return [pscustomobject]@{
            Path    = $Path
            Query   = $Query
            Status  = [int]$response.StatusCode
            Headers = $headers
            Text    = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
    }
    finally {
        $response.Dispose()
        $request.Dispose()
    }
}

# The comma keeps PowerShell from unrolling a JSON object or array into its items on the way out.
function ConvertTo-Node([string] $Text) {
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

function Limit-Array($Array, [int] $Count) {
    if ($Array -is [System.Text.Json.Nodes.JsonArray]) {
        while ($Array.Count -gt $Count) {
            $Array.RemoveAt($Array.Count - 1)
        }
    }
}

# Download links carry a token and your user ID in their query: keep the parameter names, not the values.
function Hide-LinkTokens($Body) {
    if ($Body -isnot [System.Text.Json.Nodes.JsonArray]) { return }
    foreach ($link in $Body) {
        if ($link -isnot [System.Text.Json.Nodes.JsonObject] -or $null -eq $link['URI']) { continue }
        $uri = [uri](Get-Text $link['URI'])
        if (-not $uri.Query) { continue }
        $pairs = foreach ($pair in $uri.Query.TrimStart('?').Split('&')) {
            $name, $value = $pair.Split('=', 2)
            $value = if ($name -eq 'expires') { $value } elseif ($name -eq 'user_id') { $Stand.UserId } else { $Redacted }
            "$name=$value"
        }
        $link['URI'] = [System.Text.Json.Nodes.JsonValue]::Create($uri.GetLeftPart([UriPartial]::Path) + '?' + ($pairs -join '&'))
    }
}

# An nxm:// file link's parts. Its key joins the secrets, so it's replaced wherever it shows up.
function Read-NxmLink([string] $Text, [string] $Parameter) {
    $nxm = [uri]$Text.Trim()
    $segments = $nxm.AbsolutePath.Trim('/').Split('/')
    if ($nxm.Scheme -ne 'nxm' -or $segments.Count -ne 4 -or $segments[0] -ne 'mods' -or $segments[2] -ne 'files') {
        throw "-$Parameter is not an nxm:// file link. It should look like nxm://<game>/mods/<mod>/files/<file>?key=...&expires=...&user_id=..."
    }

    $query = [System.Web.HttpUtility]::ParseQueryString($nxm.Query)
    if (-not $query['key'] -or $query['expires'] -notmatch '^\d+$') {
        throw "The -$Parameter link has no key or expiry."
    }

    $script:Secrets.Add([pscustomobject]@{ Kind = 'key'; Label = "the -$Parameter key"; Value = $query['key']; Stand = $RedactedNxmKey })
    return [pscustomobject]@{
        Game         = $nxm.Host
        ModId        = $segments[1]
        FileId       = $segments[3]
        Key          = $query['key']
        Expires      = $query['expires']
        ExpiresAt    = [DateTimeOffset]::FromUnixTimeSeconds([long]$query['expires']).ToLocalTime()
        DownloadPath = "v1/games/$($nxm.Host)/mods/$($segments[1])/files/$($segments[3])/download_link.json"
    }
}

function Hide-Personal([string] $Text) {
    foreach ($secret in $script:Secrets) {
        $Text = $Text.Replace($secret.Value, $secret.Stand)
    }

    $id = [regex]::Escape($script:Me.UserId)
    $Text = [regex]::Replace($Text, "(?<=""(user_id|member_id|uid)"":\s*""?)$id(?!\d)", $Stand.UserId)
    $Text = [regex]::Replace($Text, "(?<=[=/])$id(?=[/&""?\s]|$)", $Stand.UserId)
    $Text = [regex]::Replace($Text, "(?<![\w.-])$([regex]::Escape($script:Me.Name))(?![\w.-])", $Stand.Name)
    return $Text
}

# Fails if anything personal is left: the key or any 12 characters of it, an nxm key, the email, user ID or name.
function Assert-Clean([string] $Text, [string] $File) {
    $problems = @()
    foreach ($secret in $script:Secrets | Where-Object { $_.Kind -eq 'key' }) {
        for ($start = 0; $start -le $secret.Value.Length - 12; $start++) {
            if ($Text.Contains($secret.Value.Substring($start, 12))) {
                $problems += "part of $($secret.Label)"
                break
            }
        }
    }

    foreach ($secret in $script:Secrets | Where-Object { $_.Kind -ne 'key' }) {
        if ($Text.Contains($secret.Value)) {
            $problems += $secret.Label
        }
    }

    if ([regex]::IsMatch($Text, "(?<!\d)$([regex]::Escape($script:Me.UserId))(?!\d)")) {
        $problems += 'your user ID'
    }

    if ([regex]::IsMatch($Text, "(?<![\w.-])$([regex]::Escape($script:Me.Name))(?![\w.-])")) {
        $problems += 'your user name'
    }

    if ($problems) {
        throw "$File would still hold $($problems -join ', '). Nothing was written for it; tell the person who wrote this script."
    }
}

function Save-Fixture([string] $Name, $Exchange, [scriptblock] $Shape) {
    $body = ConvertTo-Node $Exchange.Text
    if ($Shape) {
        & $Shape $body
    }

    $headers = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($header in $Exchange.Headers.GetEnumerator()) {
        $headers[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$header.Value)
    }

    $request = [System.Text.Json.Nodes.JsonObject]::new()
    $request['method'] = [System.Text.Json.Nodes.JsonValue]::Create('GET')
    $request['path'] = [System.Text.Json.Nodes.JsonValue]::Create('/' + $Exchange.Path.TrimStart('/'))
    $request['query'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Query)

    $response = [System.Text.Json.Nodes.JsonObject]::new()
    $response['status'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Status)
    $response['headers'] = $headers
    $response['body'] = $body

    $record = [System.Text.Json.Nodes.JsonObject]::new()
    $record['request'] = $request
    $record['response'] = $response

    $file = Join-Path $script:OutFolder "$Name.json"
    $json = Hide-Personal ($record.ToJsonString($JsonOptions))
    Assert-Clean $json $file
    [System.IO.File]::WriteAllText($file, $json.ReplaceLineEndings("`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host ("  {0,-44} {1}" -f "$Name.json", $Exchange.Status)
}

$ApiKey = Get-ApiKey
$Secrets = [System.Collections.Generic.List[object]]::new()
$Secrets.Add([pscustomobject]@{ Kind = 'key'; Label = 'your API key'; Value = $ApiKey; Stand = $RedactedKey })

$OutFolder = Join-Path $PSScriptRoot 'Recorded' $Account.ToLowerInvariant()
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null

$Http = [System.Net.Http.HttpClient]::new()
$Http.BaseAddress = $ApiBase
$Http.Timeout = [TimeSpan]::FromSeconds(30)

try {
    # Who the key belongs to, so their details can be replaced everywhere.
    $validate = Invoke-Nexus 'v1/users/validate.json'
    if ($validate.Status -ne 200) {
        throw "Nexus Mods refused the key (HTTP $($validate.Status)). Check it, then try again."
    }

    $owner = ConvertTo-Node $validate.Text
    $Me = @{ UserId = Get-Text $owner['user_id']; Name = Get-Text $owner['name']; Email = Get-Text $owner['email'] }
    if (-not $Me.UserId -or -not $Me.Name) {
        throw 'Nexus Mods answered without a user ID or name, so the script cannot tell what to replace.'
    }

    if ($Me.Email) {
        $Secrets.Add([pscustomobject]@{ Kind = 'email'; Label = 'your email'; Value = $Me.Email; Stand = $Stand.Email })
    }

    $isPremium = (Get-Text $owner['is_premium']) -eq 'true'
    if ($isPremium -ne ($Account -eq 'Premium')) {
        $actual = if ($isPremium) { 'Premium' } else { 'free' }
        throw "That key belongs to a $actual account. Run the script with -Account $(if ($isPremium) { 'Premium' } else { 'Free' })."
    }

    Write-Host "Recording as a $Account account into $OutFolder"
    Save-Fixture 'validate' $validate

    $modPath = "v1/games/$Game/mods/$ModId"
    if ($Account -eq 'Premium') {
        Save-Fixture 'game' (Invoke-Nexus "v1/games/$Game.json")
        Save-Fixture 'mod' (Invoke-Nexus "$modPath.json")

        $files = Invoke-Nexus "$modPath/files.json"
        if ($files.Status -ne 200) {
            throw "Nexus Mods has no files for $Game mod $ModId (HTTP $($files.Status))."
        }

        $list = @((ConvertTo-Node $files.Text)['files'])
        $main = @($list | Where-Object { (Get-Text $_['category_name']) -eq 'MAIN' })
        # @() keeps a single result a list, so [0] picks the file rather than indexing into it.
        $pick = @(@(if ($main) { $main } else { $list }) | Sort-Object { [long](Get-Text $_['uploaded_timestamp']) } -Descending)[0]
        $fileId = Get-Text $pick['file_id']

        Save-Fixture 'files' $files
        Save-Fixture 'file' (Invoke-Nexus "$modPath/files/$fileId.json")
        Save-Fixture 'changelogs' (Invoke-Nexus "$modPath/changelogs.json")
        Save-Fixture 'download-link' (Invoke-Nexus "$modPath/files/$fileId/download_link.json") { param($body) Hide-LinkTokens $body }
        Save-Fixture 'updated-1d' (Invoke-Nexus "v1/games/$Game/mods/updated.json" 'period=1d') { param($body) Limit-Array $body 25 }
        Save-Fixture 'tracked-mods' (Invoke-Nexus 'v1/user/tracked_mods.json') { param($body) Limit-Array $body 5 }
        Save-Fixture 'mod-not-found' (Invoke-Nexus "v1/games/$Game/mods/999999999.json")
        Save-Fixture 'game-not-found' (Invoke-Nexus 'v1/games/notagame.json')
        Save-Fixture 'validate-invalid-key' (Invoke-Nexus 'v1/users/validate.json' -Key 'not-a-real-key')
    }
    else {
        $files = Invoke-Nexus "$modPath/files.json"
        if ($files.Status -ne 200) {
            throw "Nexus Mods has no files for $Game mod $ModId (HTTP $($files.Status))."
        }

        $newest = @((ConvertTo-Node $files.Text)['files']) | Sort-Object { [long](Get-Text $_['uploaded_timestamp']) } -Descending | Select-Object -First 1
        $fileId = Get-Text $newest['file_id']
        Save-Fixture 'download-link-without-key' (Invoke-Nexus "$modPath/files/$fileId/download_link.json")

        if ($NxmLink) {
            $nxm = Read-NxmLink $NxmLink 'NxmLink'
            $exchange = Invoke-Nexus $nxm.DownloadPath "key=$([uri]::EscapeDataString($nxm.Key))&expires=$($nxm.Expires)"
            $exchange.Query = "key=$RedactedNxmKey&expires=$($nxm.Expires)"
            Save-Fixture 'download-link-with-nxm-key' $exchange { param($body) Hide-LinkTokens $body }

            # How Nexus Mods refuses a key it never handed over, with the link's own expiry.
            Save-Fixture 'download-link-with-wrong-nxm-key' (Invoke-Nexus $nxm.DownloadPath "key=$MadeUpNxmKey&expires=$($nxm.Expires)")

            # The link itself, as a browser hands it over, for the link parser's tests.
            $link = "nxm://$($nxm.Game)/mods/$($nxm.ModId)/files/$($nxm.FileId)?key=$RedactedNxmKey&expires=$($nxm.Expires)&user_id=$($Stand.UserId)"
            Assert-Clean $link 'nxm-link.txt'
            [System.IO.File]::WriteAllText((Join-Path $OutFolder 'nxm-link.txt'), "$link`n", [System.Text.UTF8Encoding]::new($false))
            Write-Host ("  {0,-44} {1}" -f 'nxm-link.txt', 'saved')
            Write-Host ("The link expires at {0:yyyy-MM-dd HH:mm:ss}. After then, run this again with -ExpiredNxmLink and the same link." -f $nxm.ExpiresAt)
        }

        if ($ExpiredNxmLink) {
            $old = Read-NxmLink $ExpiredNxmLink 'ExpiredNxmLink'
            if ($old.ExpiresAt -gt [DateTimeOffset]::Now) {
                throw ("The -ExpiredNxmLink link works until {0:yyyy-MM-dd HH:mm:ss}. Run this again after then." -f $old.ExpiresAt)
            }

            $exchange = Invoke-Nexus $old.DownloadPath "key=$([uri]::EscapeDataString($old.Key))&expires=$($old.Expires)"
            $exchange.Query = "key=$RedactedNxmKey&expires=$($old.Expires)"
            Save-Fixture 'download-link-with-expired-nxm-key' $exchange { param($body) Hide-LinkTokens $body }
        }
    }

    Write-Host 'Done. Review the files, then tell Claude they are there.'
}
finally {
    $Http.Dispose()
    $ApiKey = $null
}
