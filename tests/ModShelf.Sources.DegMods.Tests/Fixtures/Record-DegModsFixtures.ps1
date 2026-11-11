<#
.SYNOPSIS
    Records the answers of DEG Mods' relays and file servers that the DEG Mods source's tests replay.

.DESCRIPTION
    DEG Mods keeps its mods as Nostr events (NIP-01) on its own two relays, read over a WebSocket, and their files on
    Blossom servers, named by their SHA-256. This script asks both relays what the source asks them, and the file
    servers for a few files, and saves each answer as a JSON file under Recorded, next to this script. The tests replay
    these files with a fake relay and WireMock.Net, so CI never calls DEG Mods.

    No account or key is needed, and none is sent. Every file is checked for this computer's name and your home folder,
    and the script stops before writing a file that has one. A Blossom server's download gate answers with a challenge
    that holds your network address, so its challenges are written as REDACTED. Review the files before committing them
    anyway.

    It records, from each relay (wss://brs.degmods.com serves only mods, mod jams and moderation tags, and refuses the
    rest; wss://relay.degmods.com serves everything):
    - its NIP-11 document;
    - each mod given to -Mods, both kinds of event (31142 since Aug 1, 2026; 30402 before), as the source asks for a mod
      from its link;
    - a mod that doesn't exist;
    - the admin's moderation: blocked mods and hidden tags (kind 30078), blocked authors (kind 10000) and moderation tags
      (kind 30985);
    - the mods of the author given to -Author, a few at a time, each page asked with `until` the oldest of the page
      before, as the source pages through every mod; their old mods before Aug 1, 2026; and those since a time.

    And from the file servers:
    - a file of the first mod that DEG Mods' server and another Blossom server both have: the gate's answer from DEG
      Mods' (428), and the other's answer to HEAD and to the file's first kilobyte;
    - a file of the second mod that only DEG Mods' server has: its 428, and each other server's 404;
    - a catbox.moe file's HEAD and first kilobyte, and one catbox.moe no longer has; where an archive.org link leads
      (HEAD).

    A file's first kilobyte is asked with a Range header, and a redirect followed, each answer saved.

    The source reads every mod of a relay at once (`{"kinds":[31142]}`, 500 a request), which a recording can't hold:
    the tests answer such a request from the events recorded here, as a relay would.

    Only the mods given are recorded, so the repo holds no other mod of DEG Mods' catalog. The script stops when one of
    them is marked NSFW, carries a tag DEG Mods' admin hides, or isn't a mod at all: choose another. Mods that are
    hidden, blocked, adult or deleted are made in the tests instead, signed with a test key.

    Each event's ID is checked to be the SHA-256 of its content, as NIP-01 says, before it's written.

    A run that completes replaces the earlier recordings; one that stops leaves them as they were. It makes about 60
    requests, one at a time with a pause between them.

.PARAMETER Mods
    The mods to record, each as its author's public key (64 hexadecimal digits) and its d tag, as `<author>:<d>`. The
    first must have a file that another Blossom server has too, the second one only DEG Mods has, and the eighth a
    catbox.moe file. By default, mods of Fallout: New Vegas, Street Fighter 6, Mafia and Starfield, two test posts, one
    of them in the admin's blocked mods and the other given a moderation tag.

.PARAMETER Author
    An author whose mods are paged through, a few at a time: one with more than a few mods, all ordinary ones. By
    default the author of Starfield's music replacers (nine mods, one an old event moved to the new kind).

.EXAMPLE
    ./Record-DegModsFixtures.ps1
#>

#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidatePattern('^[0-9a-f]{64}:.+$')]
    [string[]] $Mods = @(
        # Fallout: New Vegas, Better Newspapers for New Vegas Bounties II: three files, on DEG Mods' server and another.
        '16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:7cce3021-6514-47f7-a054-a28dc1269ff4'
        # Fallout: New Vegas, Nipton Rebuilt Remade: files only DEG Mods has, and on archive.org; an old event too.
        '16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:4e09a170-8393-4368-b59c-6c54db890857'
        # Fallout: New Vegas, Smook's Police Gear Pack: DEG Mods' old server, another, and catbox.moe; an old event too.
        '16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:7162b89c-da8a-4889-b4df-db905d8ecf03'
        # Fallout: New Vegas, Enclave General Outfit and Smook's Equipable Pipes: new and old events.
        '16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:cae0605a-cf81-47df-8cfc-c06eb6c458dd'
        '16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1:3f74d3ac-5bcd-4585-89e9-56621a4fda36'
        # Street Fighter 6, Reject Wifi: two versions, one on another server too.
        'c3712e02c22d4afba641ba8a2f9da724c1e4cf67ed97c4965eb762d603822eb9:1d5868f2-8fa3-4b38-b135-64843c076f9b'
        # Mafia, No Talking: four versions in one event; an old event too.
        'a15550047ac16022e0b11381cbe0ad5dd22e2d0555bb50827aee57e3c379b71a:1ff99bb8-1543-491e-9e7a-dfbfc57c7b8b'
        # Starfield, VGB Fed and Hydration Duration Increase, and VGB Well Rested…: old events, files on catbox.moe.
        '1b939fa858ed76e8daec4f31f1c65343c0be9467cecb50c73d5bcf2c1ebdc679:49930431-b762-4d6b-b22b-6488f94e5d2b'
        '1b939fa858ed76e8daec4f31f1c65343c0be9467cecb50c73d5bcf2c1ebdc679:9ad17069-382d-42f0-9b0b-97ce92649d25'
        # Test posts: one in the admin's blocked mods, one given a moderation tag.
        '0001c40591acd6a4e6c524f1b3ac77b2e8399002b38c9734ba610b1d985ae2c2:03d5d7e1-5338-488d-bb8c-da251e62ce27'
        '0001c40591acd6a4e6c524f1b3ac77b2e8399002b38c9734ba610b1d985ae2c2:b0c9ee94-f19d-4b2c-b8de-10e51cd93e4d'
    ),

    [ValidatePattern('^[0-9a-f]{64}$')]
    [string] $Author = 'b20828742c7a62a1c4547508c5d141eae619c27674bba56846b7cc7ac848020a',

    # Only for testing this script against local fakes of DEG Mods.
    [uri[]] $Relays = @('wss://brs.degmods.com', 'wss://relay.degmods.com'),
    [uri[]] $BlossomServers = @(
        'https://blossom.primal.net', 'https://blossom.data.haus', 'https://nostr.download', 'https://blossom.jumble.social',
        'https://blossom-01.uid.ovh', 'https://blossom-02.uid.ovh', 'https://blossom.ditto.pub'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# DEG Mods' own account: its games database, its moderation.
$Admin = 'f4bf1fb5ba8be839f70c7331733e309f780822b311f63e01f9dc8abbb428f8d5'

# Mods: since Aug 1, 2026, and before it, tagged t=GameMod. The site shows an old one only when it's from before then.
$ModKind = 31142
$OldModKind = 30402
$OldModsEnd = 1785542400

# DEG Mods' file servers.
$DegModsBlossom = @('brs.degmods.com', 'bs.degmods.com')

# The tags DEG Mods' admin hides when its list can't be read, as its site does.
$HiddenTags = @('politics', 'religion', 'loli', 'shota', 'gore')

# How many of the author's mods a page asks for.
$PageSize = 3

# The pause between requests, and how long an answer may take.
$PauseMilliseconds = 300
$AnswerSeconds = 30

# A mod that doesn't exist.
$MissingD = '00000000-0000-0000-0000-000000000000'

# How much of a file is downloaded.
$FirstBytes = 1024

$UserAgent = 'ModShelf-fixtures/1.0'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

$Utf8 = [System.Text.UTF8Encoding]::new($false)

# A string as NIP-01 writes it into what an event's ID is the hash of: JSON, with only what must be escaped escaped.
function ConvertTo-NostrString([string] $Text) {
    $builder = [System.Text.StringBuilder]::new($Text.Length + 2)
    [void]$builder.Append('"')
    foreach ($c in $Text.ToCharArray()) {
        switch ([int]$c) {
            0x22 { [void]$builder.Append('\"') }
            0x5C { [void]$builder.Append('\\') }
            0x08 { [void]$builder.Append('\b') }
            0x09 { [void]$builder.Append('\t') }
            0x0A { [void]$builder.Append('\n') }
            0x0C { [void]$builder.Append('\f') }
            0x0D { [void]$builder.Append('\r') }
            default {
                if ([int]$c -lt 0x20) {
                    [void]$builder.Append(('\u{0:x4}' -f [int]$c))
                }
                else {
                    [void]$builder.Append($c)
                }
            }
        }
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

# Whether an event's ID is the SHA-256 of [0, pubkey, created_at, kind, tags, content], as NIP-01 says.
function Test-EventId([System.Text.Json.Nodes.JsonObject] $Event) {
    $tags = foreach ($tag in $Event['tags'].AsArray()) {
        '[' + ((@($tag.AsArray()) | ForEach-Object { ConvertTo-NostrString ($_.GetValue[string]()) }) -join ',') + ']'
    }

    $serialized = '[0,' + (ConvertTo-NostrString ($Event['pubkey'].GetValue[string]())) + ',' +
        $Event['created_at'].ToJsonString() + ',' + $Event['kind'].ToJsonString() + ',[' + ($tags -join ',') + '],' +
        (ConvertTo-NostrString ($Event['content'].GetValue[string]())) + ']'
    $hash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Utf8.GetBytes($serialized))).ToLowerInvariant()
    return $hash -eq $Event['id'].GetValue[string]()
}

function Get-TagValue([System.Text.Json.Nodes.JsonObject] $Event, [string] $Name) {
    foreach ($tag in $Event['tags'].AsArray()) {
        if ($tag.Count -ge 2 -and $tag[0].GetValue[string]() -eq $Name) {
            return $tag[1].GetValue[string]()
        }
    }

    return $null
}

function Get-TagValues([System.Text.Json.Nodes.JsonObject] $Event, [string] $Name) {
    foreach ($tag in $Event['tags'].AsArray()) {
        if ($tag.Count -ge 2 -and $tag[0].GetValue[string]() -eq $Name) {
            for ($i = 1; $i -lt $tag.Count; $i++) {
                $tag[$i].GetValue[string]()
            }
        }
    }
}

# A mod's files, as its event lists them: each JSON, in `download` tags (new) or `downloadUrls` (old).
function Get-Downloads([System.Text.Json.Nodes.JsonObject] $Event) {
    $texts = @(Get-TagValues $Event 'download') + @(Get-TagValues $Event 'downloadUrls')
    foreach ($text in $texts) {
        $download = [System.Text.Json.Nodes.JsonNode]::Parse($text)
        $link = if ($download['file']) { $download['file'].GetValue[string]() } else { $download['url'].GetValue[string]() }
        $hash = if ($download['hash']) { $download['hash'].GetValue[string]() } else { '' }
        [pscustomobject]@{ Url = [uri]$link; Hash = $hash.ToLowerInvariant() }
    }
}

# Stops the script when an event recorded for a mod isn't one, or is one the tests mustn't hold.
function Assert-Ordinary([System.Text.Json.Nodes.JsonObject] $Event) {
    $kind = [int]$Event['kind'].GetValue[long]()
    $address = "$kind`:$($Event['pubkey'].GetValue[string]()):$(Get-TagValue $Event 'd')"
    if ($kind -ne $ModKind -and $kind -ne $OldModKind) {
        throw "$address isn't a mod. Choose another."
    }

    $tags = @(Get-TagValues $Event 't') + @(Get-TagValues $Event 'tags') | ForEach-Object { $_.ToLowerInvariant() }
    $hidden = @($tags | Where-Object { $_ -in $HiddenTags })
    if ($hidden -or (Get-TagValue $Event 'content-warning') -or (Get-TagValue $Event 'nsfw') -eq 'true') {
        throw "$address is marked NSFW or carries a tag DEG Mods' admin hides. Choose another: the tests make such mods themselves."
    }
}

# Sends a REQ to a relay and reads its answer until the end of the stored events (EOSE) or a refusal (CLOSED), then
# closes the subscription.
function Invoke-Relay([System.Net.WebSockets.ClientWebSocket] $Socket, [string] $Name, [System.Text.Json.Nodes.JsonObject] $Filter) {
    Start-Sleep -Milliseconds $PauseMilliseconds
    $script:Subscription++
    $id = "s$($script:Subscription)"
    $request = [System.Text.Json.Nodes.JsonArray]::new()
    $request.Add([System.Text.Json.Nodes.JsonValue]::Create('REQ'))
    $request.Add([System.Text.Json.Nodes.JsonValue]::Create($id))
    $request.Add($Filter)
    Send-Text $Socket $request.ToJsonString()

    $messages = [System.Text.Json.Nodes.JsonArray]::new()
    while ($true) {
        $message = [System.Text.Json.Nodes.JsonNode]::Parse((Receive-Text $Socket)).AsArray()
        $type = $message[0].GetValue[string]()
        if ($type -eq 'NOTICE' -or $message.Count -lt 2 -or $message[1].GetValue[string]() -ne $id) {
            $messages.Add($message)
            continue
        }

        $messages.Add($message)
        if ($type -eq 'EVENT' -and -not (Test-EventId $message[2].AsObject())) {
            throw "An event $Name answered has an ID that isn't the hash of its content: $($message[2]['id'])."
        }

        if ($type -eq 'EOSE' -or $type -eq 'CLOSED') {
            break
        }
    }

    if ($messages[$messages.Count - 1][0].GetValue[string]() -eq 'EOSE') {
        Send-Text $Socket ('["CLOSE","' + $id + '"]')
    }

    $exchange = [System.Text.Json.Nodes.JsonObject]::new()
    $exchange['name'] = [System.Text.Json.Nodes.JsonValue]::Create($Name)
    $exchange['request'] = $request
    $exchange['messages'] = $messages
    $script:Exchanges.Add($exchange)

    $events = @($messages | Where-Object { $_[0].GetValue[string]() -eq 'EVENT' } | ForEach-Object { , $_[2].AsObject() })
    Write-Host ("  {0,-48} {1} event(s), {2}" -f $Name, $events.Count, $messages[$messages.Count - 1][0].GetValue[string]())
    return , $events
}

function Send-Text([System.Net.WebSockets.ClientWebSocket] $Socket, [string] $Text) {
    $bytes = $Utf8.GetBytes($Text)
    $timeout = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($AnswerSeconds))
    try {
        $Socket.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult() | Out-Null
    }
    finally {
        $timeout.Dispose()
    }
}

function Receive-Text([System.Net.WebSockets.ClientWebSocket] $Socket) {
    $buffer = [byte[]]::new(65536)
    $stream = [System.IO.MemoryStream]::new()
    $timeout = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($AnswerSeconds))
    try {
        do {
            $result = $Socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                throw "The relay closed the connection: $($result.CloseStatusDescription)"
            }

            $stream.Write($buffer, 0, $result.Count)
        } while (-not $result.EndOfMessage)

        return $Utf8.GetString($stream.ToArray())
    }
    finally {
        $timeout.Dispose()
        $stream.Dispose()
    }
}

function New-Filter([hashtable] $Fields) {
    $filter = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($name in @('ids', 'kinds', 'authors', '#d', '#t', '#L', 'since', 'until', 'limit')) {
        if (-not $Fields.ContainsKey($name)) {
            continue
        }

        $value = $Fields[$name]
        if ($value -is [array]) {
            $array = [System.Text.Json.Nodes.JsonArray]::new()
            foreach ($item in $value) {
                $array.Add([System.Text.Json.Nodes.JsonValue]::Create($item))
            }

            $filter[$name] = $array
        }
        else {
            $filter[$name] = [System.Text.Json.Nodes.JsonValue]::Create($value)
        }
    }

    # A JsonObject is a collection, which PowerShell would otherwise unroll.
    return , $filter
}

# A relay's NIP-11 document.
function Get-RelayInfo([uri] $Relay) {
    $address = [uri]($Relay.ToString() -replace '^wss://', 'https://' -replace '^ws://', 'http://')
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $address)
    $request.Headers.Add('Accept', 'application/nostr+json')
    $response = $script:Http.Send($request)
    try {
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "$Relay answered $([int]$response.StatusCode) for its NIP-11 document."
        }

        return , [System.Text.Json.Nodes.JsonNode]::Parse($text)
    }
    finally {
        $response.Dispose()
        $request.Dispose()
    }
}

# Every exchange with one relay.
function Save-Relay([uri] $Relay) {
    $script:Exchanges = [System.Collections.Generic.List[System.Text.Json.Nodes.JsonObject]]::new()
    $script:Subscription = 0
    Write-Host "$Relay"
    $info = Get-RelayInfo $Relay
    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    $socket.Options.SetRequestHeader('User-Agent', $UserAgent)
    $connecting = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($AnswerSeconds))
    try {
        $socket.ConnectAsync($Relay, $connecting.Token).GetAwaiter().GetResult() | Out-Null

        foreach ($mod in $Mods) {
            $modAuthor, $modD = $mod -split ':', 2
            $events = Invoke-Relay $socket "mod $mod" (New-Filter @{ kinds = @($ModKind, $OldModKind); authors = @($modAuthor); '#d' = @($modD) })
            foreach ($event in $events) {
                Assert-Ordinary $event
                $script:Found.Add($event)
            }
        }

        $first = ($Mods[0] -split ':', 2)[0]
        [void](Invoke-Relay $socket 'a mod that does not exist' (New-Filter @{ kinds = @($ModKind, $OldModKind); authors = @($first); '#d' = @($MissingD) }))

        [void](Invoke-Relay $socket 'blocked mods and hidden tags' (New-Filter @{ kinds = @(30078); authors = @($Admin); '#d' = @('moderation-excluded-tags', 'blocked-mods') }))
        [void](Invoke-Relay $socket 'blocked authors' (New-Filter @{ kinds = @(10000); authors = @($Admin) }))
        [void](Invoke-Relay $socket 'moderation tags' (New-Filter @{ kinds = @(30985); authors = @($Admin); '#L' = @('moderation') }))

        # The author's mods, a page at a time, as the source pages through every mod: each page until the oldest of the
        # one before, which it holds too, until a page brings nothing new.
        $seen = [System.Collections.Generic.HashSet[string]]::new()
        $until = $null
        $times = [System.Collections.Generic.List[long]]::new()
        for ($page = 1; $page -le 10; $page++) {
            $fields = @{ kinds = @($ModKind); authors = @($Author); limit = $PageSize }
            if ($null -ne $until) {
                $fields['until'] = $until
            }

            $events = Invoke-Relay $socket "the author's mods, page $page" (New-Filter $fields)
            $new = @($events | Where-Object { $seen.Add($_['id'].GetValue[string]()) })
            foreach ($event in $events) {
                Assert-Ordinary $event
                $times.Add($event['created_at'].GetValue[long]())
            }

            if ($new.Count -eq 0) {
                break
            }

            $until = [long]($events | ForEach-Object { $_['created_at'].GetValue[long]() } | Measure-Object -Minimum).Minimum
        }

        foreach ($event in (Invoke-Relay $socket "the author's old mods" (New-Filter @{ kinds = @($OldModKind); authors = @($Author); '#t' = @('GameMod'); until = $OldModsEnd - 1; limit = 500 }))) {
            Assert-Ordinary $event
        }

        if ($times.Count -gt 1) {
            $sorted = @($times | Sort-Object)
            $since = $sorted[[int][Math]::Floor($sorted.Count / 2)]
            foreach ($event in (Invoke-Relay $socket "the author's mods since $since" (New-Filter @{ kinds = @($ModKind, $OldModKind); authors = @($Author); since = $since }))) {
                Assert-Ordinary $event
            }
        }

        $closing = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($AnswerSeconds))
        try {
            $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, '', $closing.Token).GetAwaiter().GetResult() | Out-Null
        }
        finally {
            $closing.Dispose()
        }
    }
    finally {
        $connecting.Dispose()
        $socket.Dispose()
    }

    $record = [System.Text.Json.Nodes.JsonObject]::new()
    $record['relay'] = [System.Text.Json.Nodes.JsonValue]::Create($Relay.ToString())
    $record['info'] = $info
    $exchanges = [System.Text.Json.Nodes.JsonArray]::new()
    foreach ($exchange in $script:Exchanges) {
        $exchanges.Add($exchange)
    }

    $record['exchanges'] = $exchanges
    Write-Recording "relay-$($Relay.Host).json" $record.ToJsonString($JsonOptions)
}

# A request to a file server; the answer's status, the headers the source reads, and its body: text, or base64 for the
# first kilobyte of a file.
function Invoke-File([uri] $Url, [switch] $Head, [switch] $FirstKilobyte) {
    Start-Sleep -Milliseconds $PauseMilliseconds
    $method = if ($Head) { [System.Net.Http.HttpMethod]::Head } else { [System.Net.Http.HttpMethod]::Get }
    $request = [System.Net.Http.HttpRequestMessage]::new($method, $Url)
    if ($FirstKilobyte) {
        $request.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new(0, $FirstBytes - 1)
    }

    $response = $script:Http.Send($request)
    try {
        # The headers kept, named as here whatever case the server wrote them in.
        $Kept = @('Content-Type', 'Content-Length', 'Content-Range', 'Accept-Ranges', 'Content-Disposition', 'ETag', 'Last-Modified', 'Location', 'X-Reason', 'X-Blossom-Gate-Pow', 'X-Blossom-Gate-Ad')
        $headers = [System.Text.Json.Nodes.JsonObject]::new()
        $all = @($response.Headers) + @($response.Content.Headers)
        foreach ($header in $all) {
            if ($header.Key -notin $Kept) {
                continue
            }

            $name = $Kept | Where-Object { $_ -eq $header.Key } | Select-Object -First 1
            $value = ($header.Value -join ', ')
            if ($name -like 'X-Blossom-Gate-*') {
                # The challenge holds the client's network address.
                $value = $value -replace '(^|;\s*)c=[^;]*', '$1c=REDACTED'
            }

            $headers[$name] = [System.Text.Json.Nodes.JsonValue]::Create($value)
        }

        $requestNode = [System.Text.Json.Nodes.JsonObject]::new()
        $requestNode['method'] = [System.Text.Json.Nodes.JsonValue]::Create($method.Method)
        $requestNode['url'] = [System.Text.Json.Nodes.JsonValue]::Create($Url.ToString())
        if ($FirstKilobyte) {
            $requestNode['range'] = [System.Text.Json.Nodes.JsonValue]::Create("bytes=0-$($FirstBytes - 1)")
        }

        $responseNode = [System.Text.Json.Nodes.JsonObject]::new()
        $responseNode['status'] = [System.Text.Json.Nodes.JsonValue]::Create([int]$response.StatusCode)
        $responseNode['headers'] = $headers
        if (-not $Head) {
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            if ($response.IsSuccessStatusCode) {
                $responseNode['bodyBase64'] = [System.Text.Json.Nodes.JsonValue]::Create([System.Convert]::ToBase64String($bytes, 0, [Math]::Min($bytes.Length, $FirstBytes)))
            }
            else {
                $responseNode['body'] = [System.Text.Json.Nodes.JsonValue]::Create($Utf8.GetString($bytes))
            }
        }

        $record = [System.Text.Json.Nodes.JsonObject]::new()
        $record['request'] = $requestNode
        $record['response'] = $responseNode
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Record = $record }
    }
    finally {
        $response.Dispose()
        $request.Dispose()
    }
}

function Save-File([string] $Name, $Answer) {
    Write-Recording "$Name.json" $Answer.Record.ToJsonString($JsonOptions)
    Write-Host ("  {0,-48} {1}" -f "$Name.json", $Answer.Status)
}

# A file's first kilobyte, and where it was sent, as the source's file transfer follows a server's redirects: each
# answer saved, the first as the name given, then -redirected, -redirected-2 and so on.
function Save-FirstKilobyte([string] $Name, [uri] $Url) {
    $answer = Invoke-File $Url -FirstKilobyte
    Save-File $Name $answer
    for ($hop = 1; $answer.Status -ge 300 -and $answer.Status -lt 400 -and $hop -le 5; $hop++) {
        $next = [uri]::new($Url, $answer.Record['response']['headers']['Location'].GetValue[string]())
        $answer = Invoke-File $next -FirstKilobyte
        $suffix = if ($hop -eq 1) { '-redirected' } else { "-redirected-$hop" }
        Save-File "$Name$suffix" $answer
        $Url = $next
    }

    return $answer
}

# The other Blossom servers that have a file, by its hash, in the order given.
function Find-Mirror([string] $Hash) {
    foreach ($server in $BlossomServers) {
        $answer = Invoke-File ([uri]"$($server.ToString().TrimEnd('/'))/$Hash") -Head
        if ($answer.Status -eq 200) {
            return [pscustomobject]@{ Server = $server; Answer = $answer }
        }
    }

    return $null
}

function Write-Recording([string] $File, [string] $Json) {
    foreach ($place in $script:Private) {
        if ($Json.Contains($place.Value, [StringComparison]::Ordinal)) {
            throw "$File would hold $($place.Label). Nothing was written for it; tell the person who wrote this script."
        }
    }

    [System.IO.File]::WriteAllText((Join-Path $script:OutFolder $File), $Json.ReplaceLineEndings("`n") + "`n", $Utf8)
    $script:Saved++
}

# The newest event of a mod, either kind, from what the relays answered.
function Get-Newest([string] $Mod) {
    $modAuthor, $modD = $Mod -split ':', 2
    return $script:Found |
        Where-Object { $_['pubkey'].GetValue[string]() -eq $modAuthor -and (Get-TagValue $_ 'd') -eq $modD } |
        Sort-Object { $_['created_at'].GetValue[long]() } -Descending |
        Select-Object -First 1
}

# What a file must never hold: this computer's name and your home folder, as written in JSON too.
$Private = [System.Collections.Generic.List[object]]::new()
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
$OutFolder = Join-Path ([System.IO.Path]::GetTempPath()) "degmods-fixtures-$([guid]::NewGuid().ToString('N'))"
$Saved = 0
$Found = [System.Collections.Generic.List[System.Text.Json.Nodes.JsonObject]]::new()

$Handler = [System.Net.Http.SocketsHttpHandler]::new()
$Handler.AllowAutoRedirect = $false
$Handler.UseCookies = $false
$Http = [System.Net.Http.HttpClient]::new($Handler)
$Http.Timeout = [TimeSpan]::FromSeconds($AnswerSeconds)
$Http.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

try {
    New-Item -ItemType Directory -Path $OutFolder | Out-Null
    Write-Host "Recording $($Mods.Count) mods of DEG Mods into $Recorded"
    foreach ($relay in $Relays) {
        Save-Relay $relay
    }

    foreach ($mod in $Mods) {
        if (-not (Get-Newest $mod)) {
            throw "Neither relay has mod $mod. Choose another."
        }
    }

    Write-Host 'File servers'

    # A file DEG Mods' server and another both have.
    $shared = Get-Downloads (Get-Newest $Mods[0]) |
        Where-Object { $_.Url.Host -in $DegModsBlossom -and $_.Hash } |
        ForEach-Object { $mirror = Find-Mirror $_.Hash; if ($mirror) { [pscustomobject]@{ Download = $_; Mirror = $mirror } } } |
        Select-Object -First 1
    if (-not $shared) {
        throw "No file of mod $($Mods[0]) is on DEG Mods' server and another. Give -Mods another first mod."
    }

    $gated = [uri]"https://$($DegModsBlossom[0])/$($shared.Download.Url.Segments[-1])"
    Save-File 'blossom-gated-head' (Invoke-File $gated -Head)
    Save-File 'blossom-gated-get' (Invoke-File $gated -FirstKilobyte)
    $mirrored = [uri]"$($shared.Mirror.Server.ToString().TrimEnd('/'))/$($shared.Download.Hash)"
    Save-File 'blossom-mirror-head' $shared.Mirror.Answer
    [void](Save-FirstKilobyte 'blossom-mirror-get' $mirrored)

    # A file only DEG Mods' server has.
    $only = Get-Downloads (Get-Newest $Mods[1]) |
        Where-Object { $_.Url.Host -eq $DegModsBlossom[0] -and $_.Hash } |
        Where-Object { -not (Find-Mirror $_.Hash) } |
        Select-Object -First 1
    if (-not $only) {
        throw "Every file of mod $($Mods[1]) on DEG Mods' server is on another too. Give -Mods another second mod."
    }

    Save-File 'blossom-only-head' (Invoke-File $only.Url -Head)
    foreach ($server in $BlossomServers) {
        Save-File "blossom-missing-$($server.Host)" (Invoke-File ([uri]"$($server.ToString().TrimEnd('/'))/$($only.Hash)") -Head)
    }

    # Elsewhere: catbox.moe, a file it has and one it no longer has, and archive.org.
    $catboxFiles = @($Mods | ForEach-Object { Get-Downloads (Get-Newest $_) } | Where-Object { $_.Url.Host -eq 'files.catbox.moe' })
    $catboxHas = $false
    $catboxLacks = $false
    foreach ($catbox in $catboxFiles) {
        $answer = Invoke-File $catbox.Url -FirstKilobyte
        if (-not $catboxHas -and $answer.Status -eq 206) {
            Save-File 'catbox-head' (Invoke-File $catbox.Url -Head)
            Save-File 'catbox-get' $answer
            $catboxHas = $true
        }
        elseif (-not $catboxLacks -and $answer.Status -eq 404) {
            Save-File 'catbox-missing-get' $answer
            $catboxLacks = $true
        }
    }

    $archive = $Mods | ForEach-Object { Get-Downloads (Get-Newest $_) } | Where-Object { $_.Url.Host -eq 'archive.org' } | Select-Object -First 1
    if ($archive) {
        Save-File 'archive-head' (Invoke-File $archive.Url -Head)
    }

    New-Item -ItemType Directory -Force -Path $Recorded | Out-Null
    Get-ChildItem -Path $Recorded -Filter '*.json' -File | Remove-Item
    Get-ChildItem -Path $OutFolder -Filter '*.json' -File | Move-Item -Destination $Recorded
    Write-Host "Done: $Saved files. Review them, then tell Claude they are there."
}
finally {
    $Http.Dispose()
    if (Test-Path -LiteralPath $OutFolder) {
        Remove-Item -LiteralPath $OutFolder -Recurse -Force
    }
}
