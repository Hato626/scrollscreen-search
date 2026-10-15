$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/runtime-notices.ps1"
$allowedTestRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../work'))
$testRoot = Join-Path $allowedTestRoot ('notice-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
try {
    $cache = Join-Path $testRoot 'non-default-nuget-cache'
    $unusedCache = Join-Path $testRoot 'empty-first-cache'
    $destination = Join-Path $testRoot 'licenses'
    $dependencies = @()
    foreach ($package in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $version = if ($package -like 'Microsoft.NETCore*') { '8.0.17' } else { '8.0.18' }
        $path = Join-Path (Join-Path $cache $package.ToLowerInvariant()) $version
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        Set-Content -LiteralPath (Join-Path $path 'LICENSE') -Value "$package $version"
        $dependencies += @{ name = $package; version = "[$version, $version]" }
    }
    $assets = @{
        packageFolders = @{ $unusedCache = @{}; $cache = @{} }
        project = @{ frameworks = @{ 'net8.0-windows10.0.19041' = @{ downloadDependencies = $dependencies } } }
    }
    $assetsPath = Join-Path $testRoot 'project.assets.json'
    $assets | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $assetsPath
    Copy-RuntimeNotices -AssetsPath $assetsPath -Destination $destination
    foreach ($dependency in $dependencies) {
        $file = Join-Path $destination ($dependency.name.ToLowerInvariant() + '-LICENSE')
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing copied notice: $file" }
        if ((Get-Content -LiteralPath $file -Raw) -notmatch [regex]::Escape($dependency.version.Split(',')[0].Trim('['))) {
            throw 'A notice from the wrong package version was copied.'
        }
    }
    Write-Host 'PASS: per-package restored versions and non-default NuGet caches'
    $assets.project.frameworks.'net8.0-windows10.0.19041'.downloadDependencies[0].version = '[8.0.99, 8.0.99]'
    $assets | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $assetsPath
    $failedAsExpected = $false
    try { Copy-RuntimeNotices -AssetsPath $assetsPath -Destination $destination }
    catch { $failedAsExpected = $_.Exception.Message -like '*8.0.99*not found*' }
    if (-not $failedAsExpected) { throw 'Missing restored pack must fail with its actual requested version.' }
    Write-Host 'PASS: missing restored pack gives a specific error'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($allowedTestRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a test path outside the project work directory.'
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
