$ErrorActionPreference = 'Stop'

$repository = 'kewalker/ez-pocket'
$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'ez-pocket-download-count'
}

$releases = @()
$page = 1
do {
    $uri = "https://api.github.com/repos/$repository/releases?per_page=100&page=$page"
    try {
        # Invoke-RestMethod can return the JSON array as one nested object.
        $batch = @(Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 20 | ForEach-Object { $_ })
    } catch {
        throw "Could not read GitHub releases: $($_.Exception.Message)"
    }
    $releases += $batch
    $page++
} while ($batch.Count -eq 100)

if ($releases.Count -eq 0) {
    throw 'No published releases found.'
}

# Count app archives, not checksum files or release instructions.
$packages = @($releases | ForEach-Object { $_.assets } | Where-Object {
    $_.name -match '^ez-pocket-.*(\.zip|\.tar\.gz)$'
})
$latest = $releases | Sort-Object published_at -Descending | Select-Object -First 1
$latestPackages = @($latest.assets | Where-Object {
    $_.name -match '^ez-pocket-.*(\.zip|\.tar\.gz)$'
})

function Get-DownloadSum($assets) {
    [long](($assets | Measure-Object -Property download_count -Sum).Sum)
}

[pscustomobject]@{
    Total = Get-DownloadSum $packages
    Windows = Get-DownloadSum @($packages | Where-Object { $_.name -match '-win-' })
    MacOS = Get-DownloadSum @($packages | Where-Object { $_.name -match '-macos-' })
    Linux = Get-DownloadSum @($packages | Where-Object { $_.name -match '-linux-' })
    LatestRelease = $latest.tag_name
    LatestDownloads = Get-DownloadSum $latestPackages
}
