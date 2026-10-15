param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet run --project tests/ScrollSearch.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core acceptance failed.' }
    $runtimeVersion = (& $Dotnet --list-runtimes | Select-String '^Microsoft.NETCore.App (8\.\d+\.\d+) ' | Select-Object -Last 1).Matches.Groups[1].Value
    if (-not $runtimeVersion) { throw 'A .NET 8 SDK/runtime is required for this build.' }
    & $Dotnet publish src/ScrollSearch.App/ScrollSearch.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item LICENSE,README.md,THIRD_PARTY_NOTICES.md,TEST_REPORT.md -Destination dist/win-x64
    Copy-Item docs -Destination dist/win-x64 -Recurse -Force
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    New-Item -ItemType Directory -Force dist/win-x64/licenses | Out-Null
    foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $packagePath = Join-Path (Join-Path $packageRoot $package) $runtimeVersion
        foreach ($notice in (Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' })) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path 'dist/win-x64/licenses' ($package + '-' + $notice.Name))
        }
    }
    Copy-Item licenses/* -Destination dist/win-x64/licenses
    Compress-Archive -Path dist/win-x64/* -DestinationPath dist/ScrollScreenSearch-win-x64.zip -Force
    Get-FileHash dist/ScrollScreenSearch-win-x64.zip -Algorithm SHA256 | Format-List
}
finally { Pop-Location }
