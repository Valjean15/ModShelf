<#
.SYNOPSIS
    Records the Thunderstore answers that the Thunderstore source's tests replay.

.DESCRIPTION
    Calls Thunderstore's API and its CDN, as the source does, and saves each answer as a JSON file under Recorded, next
    to this script. The tests serve these files with WireMock.Net, so CI never calls Thunderstore.

    No account or key is needed, and none is sent: the requests carry no cookies. Every file is checked for this
    computer's name and your home folder, and the script stops before writing a file that has one. Review the files
    before committing them anyway.

    It records, for one community and one of its packages:
    - the communities list, cut to the community and the first four others it lists;
    - the community's categories;
    - its listing index, as Thunderstore redirects it to the CDN, and asked again with If-Modified-Since, as the source
      asks once it has read the listing; the index's blob, the list of the chunks' links; and each chunk;
    - the package as Thunderstore describes it wherever it's listed, its newest version's changelog, and a package that
      doesn't exist;
    - a package listed in several communities, as Thunderstore describes it;
    - a community that doesn't exist, as its listing index answers.

    The CDN's blobs are gzip-compressed JSON: each is saved unpacked, marked "gzip", and the tests compress it again
    to serve it. The chunks are cut so the files stay small: each package keeps its newest 3 versions, except the
    package recorded and the packages it depends on, which keep them all. A package listed in several communities keeps
    the first 5 of them.

    A run that completes replaces the earlier recordings; one that stops leaves them as they were. It makes about 15
    requests, one at a time with a pause between them, and stops if Thunderstore asks it to slow down.

.PARAMETER Community
    The community, as in thunderstore.io/c/<community>/: one with more than 100 packages that aren't deprecated, read in
    one chunk. Hades 2 by default: about 240 packages, some deprecated, some with adult content.

.PARAMETER Package
    A package of the community, Team-Name, as its dependency strings name it: one with many versions that depends on
    packages of the same community is best. Jowday's DamageMeter by default.

.PARAMETER ListedInSeveral
    A package listed in several communities, Team-Name. BepInEx's pack by default.

.EXAMPLE
    ./Record-ThunderstoreFixtures.ps1

.EXAMPLE
    ./Record-ThunderstoreFixtures.ps1 -Community balatro -Package Steamodded-Steamodded
#>
#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')]
    [string] $Community = 'hades-ii',

    [ValidatePattern('^[A-Za-z0-9_-]+-[A-Za-z0-9_]+$')]
    [string] $Package = 'Jowday-DamageMeter',

    [ValidatePattern('^[A-Za-z0-9_-]+-[A-Za-z0-9_]+$')]
    [string] $ListedInSeveral = 'BepInEx-BepInExPack',

    # Only for testing this script against a local fake of Thunderstore.
    [uri] $ApiBase = 'https://thunderstore.io/'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The pause between requests.
$PauseMilliseconds = 400

# How many versions each package of a chunk keeps, but the one recorded and those it depends on.
$VersionsKept = 3

# How many other communities the communities list keeps, and how many a package listed in several keeps.
$OtherCommunitiesKept = 4
$ListingsKept = 5

# A name no community or package has, new each run.
$Missing = "modshelf-missing-$(Get-Random -Minimum 100000 -Maximum 999999)"

$UserAgent = 'ModShelf-fixtures/1.0'

$JsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$JsonOptions.WriteIndented = $true
$JsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

# The team and the name of a package's full name, Team-Name: the name is what follows the last hyphen.
function Split-PackageName([string] $FullName) {
    $dash = $FullName.LastIndexOf('-')
    return [pscustomobject]@{ Team = $FullName.Substring(0, $dash); Name = $FullName.Substring($dash + 1) }
}

# A request to Thunderstore, or to its CDN by a whole link. -Blob unpacks the answer when it's gzip-compressed, as the
# CDN's blobs are, and marks it so; -Since asks with If-Modified-Since.
function Invoke-Store {
    param([string] $Target, [switch] $Blob, [string] $Since)

    Start-Sleep -Milliseconds $PauseMilliseconds
    $absolute = [uri]::IsWellFormedUriString($Target, [UriKind]::Absolute)
    $uri = if ($absolute) { [uri]$Target } else { [uri]::new($ApiBase, $Target.TrimStart('/')) }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $uri)
    if ($Since) {
        $request.Headers.TryAddWithoutValidation('If-Modified-Since', $Since) | Out-Null
    }

    $response = $script:Http.Send($request)
    try {
        if ([int]$response.StatusCode -eq 429) {
            throw 'Thunderstore asked to slow down (HTTP 429). Nothing more was recorded; wait a while, then run the script again.'
        }

        $headers = [ordered]@{}
        if ($response.Content.Headers.ContentType) {
            $headers['Content-Type'] = $response.Content.Headers.ContentType.ToString()
        }

        if ($response.Headers.Location) {
            $headers['Location'] = $response.Headers.Location.ToString()
        }

        if ($response.Content.Headers.LastModified) {
            $headers['Last-Modified'] = ([DateTimeOffset]$response.Content.Headers.LastModified).ToString('R')
        }

        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $gzip = $Blob -and $bytes.Length -ge 2 -and $bytes[0] -eq 0x1F -and $bytes[1] -eq 0x8B
        if ($gzip) {
            $packed = [System.IO.MemoryStream]::new($bytes)
            $unpacking = [System.IO.Compression.GZipStream]::new($packed, [System.IO.Compression.CompressionMode]::Decompress)
            $unpacked = [System.IO.MemoryStream]::new()
            $unpacking.CopyTo($unpacked)
            $unpacking.Dispose()
            $bytes = $unpacked.ToArray()
        }

        $requestHeaders = [ordered]@{}
        if ($Since) {
            $requestHeaders['If-Modified-Since'] = $Since
        }

        return [pscustomobject]@{
            Host           = if ($uri.Host -ne $ApiBase.Host) { $uri.Host } else { $null }
            Path           = $uri.AbsolutePath
            Query          = $uri.Query.TrimStart('?')
            RequestHeaders = $requestHeaders
            Status         = [int]$response.StatusCode
            Headers        = $headers
            Gzip           = [bool]$gzip
            Text           = [System.Text.Encoding]::UTF8.GetString($bytes)
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

function Limit-Array($Array, [int] $Count) {
    if ($Array -is [System.Text.Json.Nodes.JsonArray]) {
        while ($Array.Count -gt $Count) {
            $Array.RemoveAt($Array.Count - 1)
        }
    }
}

# Fails if the file would hold anything of this computer's.
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
    $request['method'] = [System.Text.Json.Nodes.JsonValue]::Create('GET')
    if ($Exchange.Host) {
        $request['host'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Host)
    }

    $request['path'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Path)
    $request['query'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Query)
    if ($Exchange.RequestHeaders.Count -gt 0) {
        $asked = [System.Text.Json.Nodes.JsonObject]::new()
        foreach ($header in $Exchange.RequestHeaders.GetEnumerator()) {
            $asked[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$header.Value)
        }

        $request['headers'] = $asked
    }

    $headers = [System.Text.Json.Nodes.JsonObject]::new()
    foreach ($header in $Exchange.Headers.GetEnumerator()) {
        $headers[$header.Key] = [System.Text.Json.Nodes.JsonValue]::Create([string]$header.Value)
    }

    $response = [System.Text.Json.Nodes.JsonObject]::new()
    $response['status'] = [System.Text.Json.Nodes.JsonValue]::Create($Exchange.Status)
    $response['headers'] = $headers
    if ($Exchange.Gzip) {
        $response['gzip'] = [System.Text.Json.Nodes.JsonValue]::Create($true)
    }

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
    if ($Exchange.Status -ne 200) {
        throw "Thunderstore didn't give $What (HTTP $($Exchange.Status)). Check the parameters, then try again."
    }

    $body = ConvertTo-Node $Exchange.Text
    if ($body -isnot [System.Text.Json.Nodes.JsonObject] -and $body -isnot [System.Text.Json.Nodes.JsonArray]) {
        throw "Thunderstore gave $What as something other than JSON."
    }

    return , $body
}

# The full names, Team-Name, a version's dependency strings, Team-Name-1.2.3, name.
function Get-DependencyNames($Version) {
    foreach ($dependency in @($Version['dependencies'])) {
        $text = Get-Text $dependency
        if ($text) {
            $text.Substring(0, $text.LastIndexOf('-'))
        }
    }
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
$OutFolder = Join-Path ([System.IO.Path]::GetTempPath()) "thunderstore-fixtures-$([guid]::NewGuid().ToString('N'))"
$Saved = 0

$Handler = [System.Net.Http.SocketsHttpHandler]::new()
$Handler.AllowAutoRedirect = $false
$Handler.UseCookies = $false
$Handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$Http = [System.Net.Http.HttpClient]::new($Handler)
$Http.Timeout = [TimeSpan]::FromSeconds(60)
$Http.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

try {
    $recordedPackage = Split-PackageName $Package
    $several = Split-PackageName $ListedInSeveral

    # The listing first: the community and the package must be in it.
    $index = Invoke-Store "c/$Community/api/v1/package-listing-index/"
    if ($index.Status -ne 302 -or -not $index.Headers.Contains('Location')) {
        throw "Thunderstore didn't redirect the listing index of $Community (HTTP $($index.Status)). Check -Community, then try again."
    }

    $indexBlob = Invoke-Store $index.Headers['Location'] -Blob
    $indexBody = Read-Answer $indexBlob 'the listing index''s blob'
    $chunkLinks = @(foreach ($link in $indexBody) { Get-Text $link })
    if ($chunkLinks.Count -ne 1) {
        throw "The listing of $Community is in $($chunkLinks.Count) chunks, not 1. Choose a smaller community."
    }

    # Kept in lists, never written out from a script block: PowerShell would take each package's JSON object apart.
    $chunks = [System.Collections.Generic.List[object]]::new()
    $packages = [System.Collections.Generic.List[object]]::new()
    $listedPackage = $null
    $live = 0
    foreach ($link in $chunkLinks) {
        $chunk = Invoke-Store $link -Blob
        $list = Read-Answer $chunk 'a chunk of the listing'
        $chunks.Add($chunk)
        $packages.Add($list)
        foreach ($listed in $list) {
            if ((Get-Text $listed['full_name']) -eq $Package) {
                $listedPackage = $listed
            }

            if ((Get-Text $listed['is_deprecated']) -ne 'true') {
                $live++
            }
        }
    }

    if ($null -eq $listedPackage) {
        throw "$Community doesn't list $Package. Give one of its packages to -Package."
    }

    if ($live -le 100) {
        throw "$Community has $live packages that aren't deprecated, not more than 100. Choose a bigger community."
    }

    New-Item -ItemType Directory -Path $OutFolder | Out-Null
    Write-Host "Recording $Community and its package $Package into $Recorded"

    # The communities, cut to this one and a few others.
    $communities = Invoke-Store 'api/cyberstorm/community/'
    $communitiesBody = Read-Answer $communities 'the communities list'
    $results = $communitiesBody['results']
    for ($at = $results.Count - 1; $at -ge 0; $at--) {
        $identifier = Get-Text $results[$at]['identifier']
        if ($identifier -ne $Community -and ($at -ge $OtherCommunitiesKept + 1)) {
            $results.RemoveAt($at)
        }
    }

    $listedCommunity = $false
    foreach ($result in $results) {
        if ((Get-Text $result['identifier']) -eq $Community) {
            $listedCommunity = $true
        }
    }

    if (-not $listedCommunity) {
        throw "Thunderstore's communities list doesn't hold $Community."
    }

    $communitiesBody['count'] = [System.Text.Json.Nodes.JsonValue]::Create($results.Count)
    $communitiesBody['next'] = $null
    Save-Fixture 'communities' $communities $communitiesBody
    Save-Fixture 'community-filters' (Invoke-Store "api/cyberstorm/community/$Community/filters/")

    # The listing: the index, again unchanged, its blob, and its chunk, cut.
    Save-Fixture 'listing-index' $index
    if ($index.Headers.Contains('Last-Modified')) {
        Save-Fixture 'listing-index-unchanged' (Invoke-Store "c/$Community/api/v1/package-listing-index/" -Since $index.Headers['Last-Modified'])
    }
    else {
        Write-Host '  The listing index gave no Last-Modified, so it wasn''t asked again with If-Modified-Since.'
    }

    Save-Fixture 'listing-index-blob' $indexBlob
    $keepAll = @($Package) + @(Get-DependencyNames $listedPackage['versions'][0])
    for ($chunk = 0; $chunk -lt $chunks.Count; $chunk++) {
        foreach ($listed in $packages[$chunk]) {
            if ((Get-Text $listed['full_name']) -notin $keepAll) {
                Limit-Array $listed['versions'] $VersionsKept
            }
        }

        Save-Fixture "listing-chunk-$($chunk + 1)" $chunks[$chunk] $packages[$chunk]
    }

    # The package wherever it's listed, its newest version's changelog, and one that doesn't exist.
    Save-Fixture 'package' (Invoke-Store "api/experimental/package/$($recordedPackage.Team)/$($recordedPackage.Name)/")
    $newest = Get-Text $listedPackage['versions'][0]['version_number']
    Save-Fixture 'changelog' (Invoke-Store "api/experimental/package/$($recordedPackage.Team)/$($recordedPackage.Name)/$newest/changelog/")
    Save-Fixture 'package-not-found' (Invoke-Store "api/experimental/package/ModShelf/$($Missing.Replace('-', '_'))/")

    # A package listed in several communities, cut to a few of them.
    $severalAnswer = Invoke-Store "api/experimental/package/$($several.Team)/$($several.Name)/"
    $severalBody = Read-Answer $severalAnswer "the package $ListedInSeveral"
    Limit-Array $severalBody['community_listings'] $ListingsKept
    if ($severalBody['community_listings'].Count -lt 2) {
        throw "$ListedInSeveral isn't listed in several communities. Give another to -ListedInSeveral."
    }

    Save-Fixture 'package-several' $severalAnswer $severalBody

    # A community that doesn't exist.
    Save-Fixture 'community-not-found' (Invoke-Store "c/$Missing/api/v1/package-listing-index/")

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
