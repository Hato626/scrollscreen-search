param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & "$PSScriptRoot/test-build.ps1"
    & $Dotnet run --project tests/ScrollSearch.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core acceptance failed.' }
    & $Dotnet publish src/ScrollSearch.App/ScrollSearch.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item LICENSE,README.md,THIRD_PARTY_NOTICES.md,TEST_REPORT.md -Destination dist/win-x64
    Copy-Item docs -Destination dist/win-x64 -Recurse -Force
    . "$PSScriptRoot/runtime-notices.ps1"
    Copy-RuntimeNotices -AssetsPath 'src/ScrollSearch.App/obj/project.assets.json' -Destination 'dist/win-x64/licenses'
    Copy-Item licenses/* -Destination dist/win-x64/licenses
    Compress-Archive -Path dist/win-x64/* -DestinationPath dist/ScrollScreenSearch-win-x64.zip -Force
    Get-FileHash dist/ScrollScreenSearch-win-x64.zip -Algorithm SHA256 | Format-List
}
finally { Pop-Location }
