param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$GameVersion,
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = "-p:SolutionDir=$repo/"
$plugin = Join-Path $repo 'ScavPrototypeMP/CasualtiesTogether.csproj'
$patcher = Join-Path $repo 'ScavPrototypeMP-ForkUpdaterPatcher/CoopFixUpdatePatcher.csproj'
$version = dotnet msbuild $plugin -nologo -getProperty:FullModVersion $solution -p:Configuration=ReleaseFullgame
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+-coopfix\.[1-9]\d*$') {
    throw "Could not determine the fork build version: $version"
}

$build = Join-Path $repo ('build/fork-package-' + [Guid]::NewGuid().ToString('N'))
$staging = Join-Path $build 'staging'
$package = Join-Path $build 'package'
$pluginDirectory = Join-Path $package 'BepInEx/plugins/Together'
$patcherDirectory = Join-Path $package 'BepInEx/patchers'
New-Item -ItemType Directory -Path $pluginDirectory,$patcherDirectory -Force | Out-Null

dotnet build $plugin -c ReleaseFullgame $solution "-p:ModPluginOutputDir=$staging"
if ($LASTEXITCODE -ne 0) { throw 'The mod build failed.' }
dotnet build $patcher -c ReleaseFullgame $solution
if ($LASTEXITCODE -ne 0) { throw 'The updater patcher build failed.' }

Copy-Item -LiteralPath (Join-Path $staging 'CasualtiesMP.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $repo 'build/artifacts/CoopFixUpdatePatcher/bin/ReleaseFullgame/CoopFixUpdatePatcher.dll') -Destination $patcherDirectory
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'INSTALLATION-COOP-FIX.md') -Destination $package
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory "Casualties-Together-Coop-Fix-$version-Windows-$GameVersion.zip"
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $package).FullName -DestinationPath $zip
Get-FileHash -LiteralPath $zip
