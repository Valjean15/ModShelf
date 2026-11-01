<#
.SYNOPSIS
Packs ModShelf's installers and update packages with Velopack (vpk) into artifacts\releases, from a self-contained
publish of the desktop app for each runtime.

.DESCRIPTION
Runtimes: win-x64 (Setup.exe, per user, with a Start-menu shortcut), linux-x64 (an AppImage), osx-arm64 and osx-x64
(an .app in a .pkg). Without one, it packs for this system. vpk packs for another system where it can, such as
Windows packages on Linux; macOS packages need a Mac. The version comes from the git tag through MinVer, unless
-Version gives one. Each runtime is a channel of its own, named after it: an installed ModShelf looks for updates in its own.

Packing again with the previous release's full package in the output folder adds a delta package to the update.
An AppImage needs mksquashfs (the squashfs-tools package), and a delta zstd. Bash: build/pack.sh does the same.

.EXAMPLE
.\build\pack.ps1
.\build\pack.ps1 -Version 0.9.1 -Notes .\notes.md win-x64
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Version,
    [string] $Notes,
    [string] $Output,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Runtimes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = [IO.Path]::Combine($root, 'src', 'ModShelf.Desktop', 'ModShelf.Desktop.csproj')

# Velopack installs the Windows app into %LocalAppData%\<pack ID>, and its uninstaller deletes that whole folder.
# ModShelf keeps its library database, settings, logs and secrets in %LocalAppData%\ModShelf, so the pack ID must never
# be ModShelf (ADR 0038).
$packId = 'ModShelf.App'
# Paths given are taken from PowerShell's current folder, which .NET's own doesn't follow.
$Output = if ($Output) { $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Output) } else { [IO.Path]::Combine($root, 'artifacts', 'releases') }
$Notes = if ($Notes) { $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Notes) } else { $null }

# Works in Windows PowerShell 5.1 too, which has no $IsLinux or $IsMacOS.
$platform = [Runtime.InteropServices.RuntimeInformation]
$hostSystem = if ($platform::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) { 'linux' }
    elseif ($platform::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { 'osx' }
    else { 'win' }
if (-not $Runtimes) {
    $Runtimes = switch ($hostSystem) {
        'linux' { @('linux-x64') }
        'osx' { if ($platform::OSArchitecture -eq 'Arm64') { @('osx-arm64') } else { @('osx-x64') } }
        default { @('win-x64') }
    }
}

# Runs a command, and stops when it fails.
function Invoke-Checked([string] $command, [string[]] $arguments) {
    & $command @arguments
    if ($LASTEXITCODE -ne 0) { throw "$command $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    Invoke-Checked dotnet @('tool', 'restore') | Out-Null

    if (-not $Version) {
        $Version = (& dotnet msbuild -restore $project -nologo -t:MinVer -getProperty:MinVerVersion -p:Configuration=Release).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'MinVer could not tell the version.' }
    }
    $shortVersion = ($Version -split '[-+]')[0]
    Write-Host "Packing ModShelf $Version for $($Runtimes -join ' ') into $Output"

    foreach ($runtime in $Runtimes) {
        $system = ($runtime -split '-')[0]
        if ($system -notin @('win', 'linux', 'osx')) { throw "Unknown runtime: $runtime" }
        if ($system -eq 'osx' -and $hostSystem -ne 'osx') {
            Write-Warning "macOS packages need a Mac: skipping $runtime."
            continue
        }

        # vpk packs for another system when told which, as its first argument.
        $directive = if ($system -ne $hostSystem) { @("[$system]") } else { @() }

        $publish = [IO.Path]::Combine($root, 'artifacts', 'publish', $runtime)
        if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
        Invoke-Checked dotnet @('publish', $project, '-nologo', '-c', 'Release', '-r', $runtime, '--self-contained', '-o', $publish, "-p:MinVerVersionOverride=$Version")

        $pack = @('--packId', $packId, '--packVersion', $Version, '--packDir', $publish, '--packTitle', 'ModShelf', '--packAuthors', 'ModShelf',
            '--runtime', $runtime, '--channel', $runtime, '--outputDir', $Output)
        if ($Notes) { $pack += @('--releaseNotes', $Notes) }

        switch ($system) {
            'win' {
                $pack += @('--mainExe', 'ModShelf.Desktop.exe', '--icon', [IO.Path]::Combine($root, 'src', 'ModShelf.Desktop', 'Assets', 'modshelf.ico'),
                    '--aumid', 'ModShelf.App', '--shortcuts', 'StartMenuRoot')
            }
            'linux' {
                $pack += @('--mainExe', 'ModShelf.Desktop', '--icon', [IO.Path]::Combine($root, 'src', 'ModShelf.Desktop', 'Assets', 'modshelf.png'), '--categories', 'Utility')
            }
            'osx' {
                $plist = [IO.Path]::Combine($root, 'artifacts', 'publish', "Info-$runtime.plist")
                (Get-Content ([IO.Path]::Combine($root, 'build', 'macos', 'Info.plist')) -Raw).Replace('@VERSION@', $Version).Replace('@SHORT_VERSION@', $shortVersion) |
                    Set-Content $plist -NoNewline
                $pack += @('--mainExe', 'ModShelf.Desktop', '--icon', [IO.Path]::Combine($root, 'build', 'macos', 'modshelf.icns'), '--plist', $plist)
            }
        }

        Invoke-Checked dotnet (@('vpk') + $directive + @('pack') + $pack)
    }

    Write-Host "Done: $Output"
}
finally {
    Pop-Location
}
