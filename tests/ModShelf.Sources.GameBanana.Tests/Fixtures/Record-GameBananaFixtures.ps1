<#
.SYNOPSIS
    Records the GameBanana API answers that the GameBanana source's tests replay.

.DESCRIPTION
    Calls GameBanana's API (apiv11, the JSON API its own pages use) and saves each answer as a JSON file under
    Recorded, next to this script. The tests serve these files with WireMock.Net, so CI never calls GameBanana.

    No account or key is needed, and none is sent: the requests carry no cookies, so GameBanana answers as it would
    anyone. Every file is checked for that (each flag about who's asking is false) and for this computer's name and
    your home folder, and the script stops before writing a file that has one. Review the files before committing them
    anyway.

    It records, for one game and one of its mods:
    - the games list, a page at a time: the first two, the last, and the one that holds the game; the games whose
      names match the game's; and those whose names have its longest word, 50 a page, as the source finds games by
      name;
    - the game, its mod categories, its trending mods, and a game that doesn't exist (see below);
    - the game's mods, 50 a page, in each order a search can ask for, and narrowed by a category and by an author (the
      first mod of the newest page's, as the source's contract tests pick them), with the member search that finds the
      author;
    - searches by words, 15 a page (all GameBanana gives), in each order, by names only, and one that finds nothing;
    - the mod's profile, its download page, its updates, its game alone (what a link to the mod doesn't say), and a mod
      that doesn't exist, both ways (see below);
    - a mod of the game with content ratings, when it has one;
    - the mod's author as a member, as linking their profile reads it: the member search that finds them by name, their
      profile, their subscriptions, a member that doesn't exist, and subscriptions asked for without a member's ID (as
      when no profile is linked);
    - where the download links of the mod's newest file lead (asked with HEAD, so no file is downloaded).

    A run that completes replaces the earlier recordings; one that stops leaves them as they were. It makes about 80
    requests, one at a time with a pause between them, and stops if
    GameBanana asks it to slow down.

    GameBanana answers 404 for a game or mod that doesn't exist, until it has been asked a few times: then it answers
    200, with the same error in a page it keeps (Content-Type text/html). Each is asked for with an ID new to it, until
    it does, and both answers are saved: *-not-found and *-not-found-kept.

    Long lists that the source never reads in full are cut short, so the files stay small: a mod's pictures to the
    first 2, the mod managers' game lists to 3, a member's medals to 2, and a file's analysis warnings to 3 a kind. A
    member's buddies, donation links and profile modules are left out, and their whereabouts on the site (_sLocation,
    _sSavedLastLocation) left empty.

.PARAMETER Game
    The game's ID on GameBanana, as in gamebanana.com/games/<id>. Persona 5 Royal (PC) by default: over 900 mods, most
    with files and many with updates.

.PARAMETER ModId
    A mod of the game, as in gamebanana.com/mods/<id>: one with several files, archived files and updates is best.
    P5R Female Protagonist by default.

.PARAMETER SearchWords
    Words to search the game's mods for, matching the mod given to -ModId among others.

.EXAMPLE
    ./Record-GameBananaFixtures.ps1

.EXAMPLE
    ./Record-GameBananaFixtures.ps1 -Game 8552 -ModId 123456 -SearchWords 'Raiden'
#>

#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateRange(1, [int]::MaxValue)]
    [int] $Game = 16951,

    [ValidateRange(1, [int]::MaxValue)]
    [int] $ModId = 440088,

    [ValidateLength(3, 100)]
    [string] $SearchWords = 'Protagonist',

    # Only for testing this script against a local fake of GameBanana.
    [uri] $ApiBase = 'https://gamebanana.com/'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# How many mods a page of a game's mods holds: the most GameBanana gives.
$PageSize = 50

# The pause between requests.
$PauseMilliseconds = 400

# An ID no game or mod has, new to GameBanana each run, so its first answers are 404 (see Save-Missing).
$MissingId = 900000000 + (Get-Random -Maximum 90000000)

# How many times a missing game or mod is asked for, at most, before GameBanana keeps its answer.
$MissingAsks = 6

# A search that finds nothing.
$NoMatch = 'qqzxjvnomatchqqzxjv'

# The orders a game's mods can be listed in, by the file each is saved to.
$Sorts = [ordered]@{
    'newest'                = 'Generic_Newest'
    'oldest'                = 'Generic_Oldest'
    'latest-modified'       = 'Generic_LatestModified'
    'latest-updated'        = 'Generic_LatestUpdated'
    'most-liked'            = 'Generic_MostLiked'
    'most-downloaded'       = 'Generic_MostDownloaded'
    'alphabetically'        = 'Generic_Alphabetically'
    'reverse-alphabetically' = 'Generic_ReverseAlphabetically'
}

# The orders a search by words can ask for.
$SearchOrders = @('best_match', 'popularity', 'date', 'udate')

# The content ratings that mark a mod as one for adults, looked for to find a rated mod.
$AdultRatings = @('st', 'sc', 'nu', 'pn', 'sa')

# How many of each long list are kept.
$Kept = @{
    '_aImages' = 2; '_aGameRowIds' = 3; '_aNormalMedals' = 2; '_aRareMedals' = 2; '_aLegendaryMedals' = 2
    '_aBuddies' = 0; '_aDonationMethods' = 0; '_aProfileModules' = 0
}
$WarningsKept = 3

$UserAgent = 'ModShelf-fixtures/1.0'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

# A query string from names and values, each escaped, in the order given.
function Join-Query([System.Collections.Specialized.OrderedDictionary] $Parameters) {
    $pairs = foreach ($parameter in $Parameters.GetEnumerator()) {
        [uri]::EscapeDataString([string]$parameter.Key) + '=' + [uri]::EscapeDataString([string]$parameter.Value)
    }

    return $pairs -join '&'
}

# A request to the API, or with -Head to the site itself, as a download link is asked where it leads.
function Invoke-Banana {
    param([string] $Path, [string] $Query = '', [switch] $Head)

    Start-Sleep -Milliseconds $PauseMilliseconds
    $target = if ($Query) { "${Path}?$Query" } else { $Path }
    $method = if ($Head) { [System.Net.Http.HttpMethod]::Head } else { [System.Net.Http.HttpMethod]::Get }
    $request = [System.Net.Http.HttpRequestMessage]::new($method, $target.TrimStart('/'))
    $request.Headers.Add('Accept', 'application/json')
    $response = $script:Http.Send($request)
    try {
        if ([int]$response.StatusCode -eq 429) {
            $wait = if ($response.Headers.RetryAfter) { " It asked to wait $($response.Headers.RetryAfter)." } else { '' }
            throw "GameBanana asked to slow down (HTTP 429).$wait Nothing more was recorded; wait a while, then run the script again."
        }

        $headers = [ordered]@{}
        if ($response.Content.Headers.ContentType) {
            $headers['Content-Type'] = $response.Content.Headers.ContentType.ToString()
        }

        if ($response.Headers.Location) {
            $headers['Location'] = $response.Headers.Location.ToString()
        }

        return [pscustomobject]@{
            Method  = $method.Method
            Path    = '/' + $Path.TrimStart('/')
            Query   = $Query
            Status  = [int]$response.StatusCode
            Headers = $headers
            Text    = if ($Head) { '' } else { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        }
    }
    finally {
        $response.Dispose()
        $request.Dispose()
    }
}

function Invoke-Api([string] $Path, [System.Collections.Specialized.OrderedDictionary] $Parameters) {
    $query = if ($Parameters) { Join-Query $Parameters } else { '' }
    return Invoke-Banana "apiv11/$Path" $query
}

# A page of the games list, 50 games in alphabetical order, as the source asks for one.
function Invoke-GamesPage([int] $Page) {
    return Invoke-Api 'Game/Index' ([ordered]@{ '_nPage' = $Page; '_nPerpage' = $PageSize; '_sSort' = 'Generic_Alphabetically' })
}

# The page of the games list that holds the game, and its answer: found by halving, as the list is in alphabetical
# order, then among the pages around that one, then one by one, should GameBanana order names unlike this script.
function Find-GamePage([string] $Name, [int] $Last) {
    $asked = @{}
    function Get-Page([int] $Page) {
        if (-not $asked.ContainsKey($Page)) {
            $asked[$Page] = Invoke-GamesPage $Page
        }

        return $asked[$Page]
    }

    $low = 1
    $high = $Last
    while ($low -lt $high) {
        $middle = [int][Math]::Floor(($low + $high + 1) / 2)
        $first = @((Read-Answer (Get-Page $middle) 'the games list')['_aRecords']) | Select-Object -First 1
        if ($first -and [string]::Compare((Get-Text $first['_sName']), $Name, [StringComparison]::OrdinalIgnoreCase) -le 0) {
            $low = $middle
        }
        else {
            $high = $middle - 1
        }
    }

    $nearby = foreach ($step in 1..3) { $low - $step; $low + $step }
    foreach ($page in @($low) + $nearby + (1..$Last)) {
        if ($page -lt 1 -or $page -gt $Last) {
            continue
        }

        $answer = Get-Page $page
        $holds = @((Read-Answer $answer 'the games list')['_aRecords']) | Where-Object { (Get-Text $_['_idRow']) -eq [string]$Game }
        if ($holds) {
            return [pscustomobject]@{ Page = $page; Answer = $answer }
        }
    }

    return $null
}

# A page of the game's mods, as the source asks for one.
function Invoke-ModIndex([string] $Sort, [int] $Page = 1, [string] $Filter, [string] $Value) {
    $parameters = [ordered]@{ '_nPage' = $Page; '_nPerpage' = $PageSize; '_sSort' = $Sort; '_aFilters[Generic_Game]' = $Game }
    if ($Filter) {
        $parameters["_aFilters[$Filter]"] = $Value
    }

    return Invoke-Api 'Mod/Index' $parameters
}

# A page of a search by words among the game's mods.
function Invoke-Search([string] $Words, [string] $Order = 'best_match', [int] $Page = 1, [string] $Fields) {
    $parameters = [ordered]@{ '_sModelName' = 'Mod'; '_sOrder' = $Order; '_idGameRow' = $Game; '_sSearchString' = $Words; '_nPage' = $Page }
    if ($Fields) {
        $parameters['_csvFields'] = $Fields
    }

    return Invoke-Api 'Util/Search/Results' $parameters
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

function Limit-Array($Array, [int] $Count) {
    if ($Array -is [System.Text.Json.Nodes.JsonArray]) {
        while ($Array.Count -gt $Count) {
            $Array.RemoveAt($Array.Count - 1)
        }
    }
}

# Cuts the long lists the source never reads in full, wherever they are, and empties members' whereabouts.
function Limit-Long($Node, [string] $Name) {
    if ($Node -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($whereabouts in '_sLocation', '_sSavedLastLocation') {
            if ($null -ne $Node[$whereabouts]) {
                $Node[$whereabouts] = [System.Text.Json.Nodes.JsonValue]::Create('')
            }
        }

        foreach ($property in $Node) {
            if ($Name -eq '_aAnalysisWarnings') {
                Limit-Array $property.Value $WarningsKept
            }

            Limit-Long $property.Value $property.Key
        }
    }
    elseif ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        if ($Kept.ContainsKey($Name)) {
            Limit-Array $Node $Kept[$Name]
        }

        foreach ($item in $Node) {
            Limit-Long $item $null
        }
    }
}

# The flags about who's asking, such as _bAccessorIsSubmitter and _bIsOwnedByAccessor, that are true.
function Find-Accessor($Node, [string] $Name) {
    if ($Node -is [System.Text.Json.Nodes.JsonObject]) {
        foreach ($property in $Node) {
            Find-Accessor $property.Value $property.Key
        }
    }
    elseif ($Node -is [System.Text.Json.Nodes.JsonArray]) {
        foreach ($item in $Node) {
            Find-Accessor $item $null
        }
    }
    elseif ($Name -and $Name -like '*Accessor*' -and (Get-Text $Node) -eq 'true') {
        $Name
    }
}

# Fails if the file would hold anything of this computer's, or an answer given to a signed-in member.
function Assert-Clean([string] $Json, $Body, [string] $File) {
    $problems = @()
    foreach ($place in $script:Private) {
        if ($Json.Contains($place.Value, [StringComparison]::Ordinal) -and $place.Label -notin $problems) {
            $problems += $place.Label
        }
    }

    $signedIn = @(Find-Accessor $Body $null | Select-Object -Unique)
    if ($signedIn) {
        $problems += "an answer to a signed-in member ($($signedIn -join ', '))"
    }

    if ($problems) {
        throw "$File would hold $($problems -join ', '). Nothing was written for it; tell the person who wrote this script."
    }
}

function Save-Fixture([string] $Name, $Exchange) {
    $body = ConvertTo-Node $Exchange.Text
    Limit-Long $body $null

    $headers = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($header in $Exchange.Headers.GetEnumerator()) {
        $headers[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$header.Value)
    }

    $request = [System.Text.Json.Nodes.JsonObject]::new()
    $request['method'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Method)
    $request['path'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Path)
    $request['query'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Query)

    $response = [System.Text.Json.Nodes.JsonObject]::new()
    $response['status'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Status)
    $response['headers'] = $headers
    $response['body'] = $body

    $record = [System.Text.Json.Nodes.JsonObject]::new()
    $record['request'] = $request
    $record['response'] = $response

    $json = $record.ToJsonString($JsonOptions)
    Assert-Clean $json $body "$Name.json"
    [System.IO.File]::WriteAllText((Join-Path $script:OutFolder "$Name.json"), $json.ReplaceLineEndings("`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    $script:Saved++
    Write-Host ("  {0,-40} {1}" -f "$Name.json", $Exchange.Status)
}

# The body of an answer that had to succeed; stops the script, saying what failed, when it didn't.
function Read-Answer($Exchange, [string] $What) {
    if ($Exchange.Status -ne 200) {
        throw "GameBanana didn't give $What (HTTP $($Exchange.Status)). Check the parameters, then try again."
    }

    $body = ConvertTo-Node $Exchange.Text
    if ($body -isnot [System.Text.Json.Nodes.JsonObject] -and $body -isnot [System.Text.Json.Nodes.JsonArray]) {
        throw "GameBanana gave $What as something other than JSON."
    }

    return , $body
}

# Asks for something that doesn't exist until GameBanana answers 200 with its error, saving its first 404 as $Name and
# that answer as $Name-kept.
function Save-Missing([string] $Name, [string] $Path, [System.Collections.Specialized.OrderedDictionary] $Parameters) {
    $saved = $false
    for ($ask = 1; $ask -le $MissingAsks; $ask++) {
        $answer = Invoke-Api $Path $Parameters
        if ($answer.Status -eq 404 -and -not $saved) {
            Save-Fixture $Name $answer
            $saved = $true
        }
        elseif ($answer.Status -eq 200) {
            Save-Fixture "$Name-kept" $answer
            return
        }
    }

    Write-Host "  GameBanana answered $Path $MissingAsks times without keeping its answer, so $Name-kept wasn't recorded."
}

# The ID at the end of a link such as https://gamebanana.com/mods/cats/18444.
function Get-LinkId([string] $Url) {
    if ($Url -match '/(\d+)/?$') {
        return $Matches[1]
    }

    return $null
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
$OutFolder = Join-Path ([System.IO.Path]::GetTempPath()) "gamebanana-fixtures-$([guid]::NewGuid().ToString('N'))"
$Saved = 0

$Handler = [System.Net.Http.SocketsHttpHandler]::new()
$Handler.AllowAutoRedirect = $false
$Handler.UseCookies = $false
$Handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$Http = [System.Net.Http.HttpClient]::new($Handler)
$Http.BaseAddress = $ApiBase
$Http.Timeout = [TimeSpan]::FromSeconds(30)
$Http.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

try {
    $gameAnswer = Invoke-Api "Game/$Game/ProfilePage"
    $gameBody = Read-Answer $gameAnswer "game $Game"
    $gameName = Get-Text $gameBody['_sName']

    $modAnswer = Invoke-Api "Mod/$ModId/ProfilePage"
    $modBody = Read-Answer $modAnswer "mod $ModId"
    $modGame = if ($modBody['_aGame']) { Get-Text $modBody['_aGame']['_idRow'] } else { $null }
    if ($modGame -ne [string]$Game) {
        throw "Mod $ModId is a mod of game $modGame, not of game $Game. Give a mod of the game to -ModId."
    }

    New-Item -ItemType Directory -Path $OutFolder | Out-Null
    Write-Host "Recording $gameName ($Game) and its mod $(Get-Text $modBody['_sName']) ($ModId) into $Recorded"

    # The games list, a page at a time, and the games named like this one.
    $games = Invoke-GamesPage 1
    $gameCount = [int](Get-Text (Read-Answer $games 'the games list')['_aMetadata']['_nRecordCount'])
    $lastPage = [Math]::Max(1, [int][Math]::Ceiling($gameCount / $PageSize))
    Save-Fixture 'games-1' $games
    if ($lastPage -ge 2) {
        Save-Fixture 'games-2' (Invoke-GamesPage 2)
    }

    if ($lastPage -ge 3) {
        Save-Fixture 'games-last' (Invoke-GamesPage $lastPage)
    }

    $holding = Find-GamePage $gameName $lastPage
    if (-not $holding) {
        Write-Host "  No page of the games list holds $gameName, so none was recorded for it."
    }
    elseif ($holding.Page -notin 1, 2, $lastPage) {
        Save-Fixture 'games-with-game' $holding.Answer
    }

    Save-Fixture 'games-name-match' (Invoke-Api 'Util/Game/NameMatch' ([ordered]@{ '_sName' = $gameName }))

    # The games whose names have the game's longest word, lowercase, as the source asks when that's typed.
    $longest = @($gameName.ToLowerInvariant() -split '[^\p{L}\p{N}]+' | Where-Object { $_.Length -ge 3 } | Sort-Object Length -Descending -Stable) |
        Select-Object -First 1
    if ($longest) {
        Save-Fixture 'games-find' (Invoke-Api 'Util/Game/NameMatch' ([ordered]@{ '_sName' = $longest; '_nPerpage' = $PageSize; '_nPage' = 1 }))
    }
    else {
        Write-Host "  $gameName has no word of 3 letters or more, so no search of games by name was recorded."
    }

    # The game.
    Save-Fixture 'game' $gameAnswer
    $categories = Invoke-Api 'Mod/Categories' ([ordered]@{ '_idGameRow' = $Game; '_sSort' = 'a_to_z'; '_bShowEmpty' = 'true' })
    Save-Fixture 'game-categories' $categories
    Save-Fixture 'game-trending' (Invoke-Api "Game/$Game/TopSubs")
    Save-Missing 'game-not-found' "Game/$MissingId/ProfilePage"

    # The game's mods in each order; the newest and the latest modified two pages deep.
    foreach ($sort in $Sorts.GetEnumerator()) {
        $first = Invoke-ModIndex $sort.Value
        Save-Fixture "mods-$($sort.Key)-1" $first
        if ($sort.Key -eq 'newest') {
            $newest = Read-Answer $first 'the newest mods'
        }

        if ($sort.Key -in 'newest', 'latest-modified') {
            Save-Fixture "mods-$($sort.Key)-2" (Invoke-ModIndex $sort.Value 2)
        }
    }

    # Narrowed by the category and the author of the first newest mod that has one, as the contract tests pick them.
    $records = @($newest['_aRecords'])
    $withCategory = @($records | Where-Object { $null -ne $_['_aRootCategory'] }) | Select-Object -First 1
    if ($withCategory) {
        $categoryName = Get-Text $withCategory['_aRootCategory']['_sName']
        $categoryId = @((Read-Answer $categories 'the game''s categories') | Where-Object { (Get-Text $_['_sName']) -eq $categoryName } |
            ForEach-Object { Get-Text $_['_idRow'] }) | Select-Object -First 1
        if (-not $categoryId) {
            $categoryId = Get-LinkId (Get-Text $withCategory['_aRootCategory']['_sProfileUrl'])
        }

        Save-Fixture 'mods-category' (Invoke-ModIndex 'Generic_Newest' 1 'Generic_Category' $categoryId)
    }

    $withAuthor = @($records | Where-Object { $null -ne $_['_aSubmitter'] }) | Select-Object -First 1
    if ($withAuthor) {
        Save-Fixture 'members' (Invoke-Api 'Util/Search/Results' ([ordered]@{ '_sModelName' = 'Member'; '_sSearchString' = (Get-Text $withAuthor['_aSubmitter']['_sName']) }))
        Save-Fixture 'mods-author' (Invoke-ModIndex 'Generic_Newest' 1 'Generic_Submitter' (Get-Text $withAuthor['_aSubmitter']['_idRow']))
    }

    # Searches by words, 15 a page: four pages make a page of 50.
    for ($page = 1; $page -le 4; $page++) {
        $found = Invoke-Search $SearchWords 'best_match' $page
        Save-Fixture "search-$page" $found
        if ((Get-Text (Read-Answer $found 'a search')['_aMetadata']['_bIsComplete']) -eq 'true') {
            break
        }
    }

    foreach ($order in $SearchOrders | Where-Object { $_ -ne 'best_match' }) {
        Save-Fixture "search-$order" (Invoke-Search $SearchWords $order)
    }

    Save-Fixture 'search-names' (Invoke-Search $SearchWords 'best_match' 1 'name')
    Save-Fixture 'search-nothing' (Invoke-Search $NoMatch)

    # The first word of the newest mod's name, as the contract tests search for it.
    if ($records) {
        $word = (Get-Text $records[0]['_sName']).Split(' ')[0]
        $wordFound = Invoke-Search $word
        Save-Fixture 'search-first-word' $wordFound
        if ((Get-Text (Read-Answer $wordFound 'a search')['_aMetadata']['_nRecordCount']) -eq '0') {
            Write-Host "  A search for '$word', the first word of the newest mod's name, found nothing."
        }
    }

    # The mod.
    Save-Fixture 'mod' $modAnswer
    Save-Fixture 'mod-download-page' (Invoke-Api "Mod/$ModId/DownloadPage")
    Save-Fixture 'mod-updates' (Invoke-Api "Mod/$ModId/Updates" ([ordered]@{ '_nPage' = 1; '_nPerpage' = $PageSize }))
    Save-Fixture 'mod-game' (Invoke-Api "Mod/$ModId" ([ordered]@{ '_csvProperties' = '_idRow,_sName,_aGame' }))
    Save-Missing 'mod-not-found' "Mod/$MissingId/ProfilePage"
    Save-Missing 'mod-game-not-found' "Mod/$MissingId" ([ordered]@{ '_csvProperties' = '_idRow,_sName,_aGame' })

    # A mod of the game with content ratings for adults, found by GameBanana's filter for them.
    $rated = $null
    foreach ($rating in $AdultRatings) {
        $lookup = Invoke-Api 'Mod/Index' ([ordered]@{ '_nPage' = 1; '_nPerpage' = 1; '_aFilters[Generic_Game]' = $Game; '_aFilters[Generic_ContentRatings]' = $rating })
        $hit = @((Read-Answer $lookup 'the rated mods')['_aRecords']) | Select-Object -First 1
        if ($hit) {
            $rated = Get-Text $hit['_idRow']
            break
        }
    }

    if ($rated) {
        Save-Fixture 'mod-rated' (Invoke-Api "Mod/$rated/ProfilePage")
    }
    else {
        Write-Host "  The game has no mods rated for adults, so none was recorded."
    }

    # The mod's author as a member: found by name, their profile and their subscriptions, as linking their profile reads
    # them; one that doesn't exist; and subscriptions asked for without a member's ID, as when no profile is linked.
    $submitter = $modBody['_aSubmitter']
    if ($submitter) {
        $memberId = Get-Text $submitter['_idRow']
        Save-Fixture 'member-search' (Invoke-Api 'Util/Search/Results' ([ordered]@{ '_sModelName' = 'Member'; '_sSearchString' = (Get-Text $submitter['_sName']) }))
        Save-Fixture 'member' (Invoke-Api "Member/$memberId/ProfilePage")
        Save-Fixture 'member-subscriptions' (Invoke-Api "Member/$memberId/Subscriptions" ([ordered]@{ '_nPage' = 1 }))
    }
    else {
        Write-Host "  Mod $ModId names no author, so no member was recorded."
    }

    Save-Missing 'member-not-found' "Member/$MissingId/ProfilePage"
    Save-Fixture 'member-subscriptions-unlinked' (Invoke-Api 'Member/linked/Subscriptions' ([ordered]@{ '_nPage' = 1 }))

    # Where the download links of the mod's newest file lead.
    $files = @($modBody['_aFiles']) | Where-Object { $null -ne $_ }
    $newestFile = @($files | Sort-Object { [long](Get-Text $_['_tsDateAdded']) } -Descending) | Select-Object -First 1
    if ($newestFile) {
        $fileId = Get-Text $newestFile['_idRow']
        Save-Fixture 'download' (Invoke-Banana "dl/$fileId" -Head)
        Save-Fixture 'download-mmdl' (Invoke-Banana "mmdl/$fileId" -Head)
    }
    else {
        Write-Host "  Mod $ModId has no files, so no download link was recorded."
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
