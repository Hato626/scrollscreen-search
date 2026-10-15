function Copy-RuntimeNotices {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AssetsPath,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    $downloads = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        # Installed runtimes and restored packs can differ when several SDKs coexist.
        # The restore result is the source of truth for both version and cache location.
        $versions = @($downloads | Where-Object { $_.name -ieq $package } | ForEach-Object {
            ([string]$_.version).Trim('[', ']').Split(',')[0].Trim()
        } | Select-Object -Unique)
        if ($versions.Count -ne 1) { throw "Expected one restored version of $package in $AssetsPath." }
        $version = $versions[0]
        $packagePath = $packageFolders | ForEach-Object { Join-Path (Join-Path $_ $package) $version } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (-not $packagePath) { throw "Restored package $package/$version was not found in the asset file's package folders." }
        $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' })
        if ($notices.Count -eq 0) { throw "No license notices found for restored package $package/$version." }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $Destination ($package + '-' + $notice.Name)) -Force
        }
        Write-Host "Included notices from $package/$version"
    }
}
