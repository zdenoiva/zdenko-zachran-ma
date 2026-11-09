# Vydanie novej verzie: build + latest.json + GitHub Release.
# Pouzitie:
#   .\release.ps1                 # build, aktualizuje latest.json, vytvori release
#   .\release.ps1 -SkipBuild      # pouzije uz hotove subory v dist
#   .\release.ps1 -NoRelease      # len build a latest.json
param(
    [switch]$SkipBuild,
    [switch]$NoRelease
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $appInfo = Get-Content (Join-Path $PSScriptRoot "Util\AppInfo.cs") -Raw
    $match = [regex]::Match($appInfo, 'VersionDisplay\s*=\s*"([^"]+)"')
    if (-not $match.Success) { throw "Nepodarilo sa precitat verziu z Util\AppInfo.cs" }
    $version = $match.Groups[1].Value
    $tag = "v$version"

    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "build.ps1")
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 zlyhal" }
        & (Join-Path $PSScriptRoot "make-installer.ps1") -SkipBuild
        if ($LASTEXITCODE -ne 0) { throw "make-installer.ps1 zlyhal" }
    }

    $exe = Join-Path $PSScriptRoot "dist\ZachranaDisku.exe"
    $setup = Join-Path $PSScriptRoot "dist\ZachranaDisku-Setup-$version.exe"
    if (-not (Test-Path $exe)) { throw "Chyba $exe" }
    if (-not (Test-Path $setup)) { throw "Chyba $setup" }

    $sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $setupSha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item $exe).Length
    $setupSize = (Get-Item $setup).Length

    $base = "https://github.com/zdenoiva/zdenko-zachran-ma/releases/download/$tag"

    $changelogText = Get-Content (Join-Path $PSScriptRoot "CHANGELOG.md") -Raw
    $pattern = "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)"
    $section = [regex]::Match($changelogText, $pattern).Groups[1].Value
    $items = @($section -split "\r?\n" |
        Where-Object { $_ -match '^\s*-\s+' } |
        ForEach-Object { ($_ -replace '^\s*-\s+', '').Trim() } |
        Where-Object { $_ })

    $latest = [ordered]@{
        version         = $version
        tag             = $tag
        publishedAt     = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        pageUrl         = "https://zdenoiva.github.io/zdenko-zachran-ma/"
        portableUrl     = "$base/ZachranaDisku.exe"
        installerUrl    = "$base/ZachranaDisku-Setup-$version.exe"
        sha256          = $sha
        installerSha256 = $setupSha
        size            = $size
        installerSize   = $setupSize
        changelog       = $items
    }

    $latestPath = Join-Path $PSScriptRoot "latest.json"
    $latest | ConvertTo-Json -Depth 6 | Set-Content $latestPath -Encoding UTF8
    Copy-Item $latestPath (Join-Path $PSScriptRoot "docs\latest.json") -Force
    Write-Host "latest.json aktualizovany na verziu $version" -ForegroundColor Green
    Write-Host "Portable: $([math]::Round($size / 1MB, 1)) MB  SHA256 $sha"
    Write-Host "Setup:    $([math]::Round($setupSize / 1MB, 1)) MB  SHA256 $setupSha"

    if (-not $NoRelease) {
        $gh = Get-Command gh -ErrorAction SilentlyContinue
        if (-not $gh) {
            throw "GitHub CLI (gh) nie je dostupne. Nainstalujte ho alebo pouzite -NoRelease."
        }

        $notes = if ($items.Count -gt 0) {
            ($items | ForEach-Object { "- $_" }) -join "`n"
        } else {
            "Vydanie $version"
        }
        $notesFile = Join-Path $env:TEMP "zdenko-zachran-ma-$version-notes.md"
        Set-Content $notesFile -Value $notes -Encoding UTF8

        & $gh.Source release view $tag *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Release $tag existuje, aktualizujem assety..." -ForegroundColor Cyan
            & $gh.Source release upload $tag $exe $setup --clobber
        } else {
            Write-Host "Vytvaram release $tag..." -ForegroundColor Cyan
            & $gh.Source release create $tag $exe $setup `
                --title "Zdenko, zachráň ma! $version" `
                --notes-file $notesFile
        }
        if ($LASTEXITCODE -ne 0) { throw "gh release zlyhal (exit $LASTEXITCODE)" }
        Write-Host "Release $tag je hotovy." -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
