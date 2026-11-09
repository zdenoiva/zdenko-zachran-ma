# Vytvori instalator Zdenko, zachráň ma! (Inno Setup).
# Vysledok: .\dist\ZachranaDisku-Setup-2.14.exe
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "build.ps1")
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 zlyhal" }
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) {
        throw "Inno Setup (ISCC.exe) sa nenasiel. Nainstalujte ho: winget install JRSoftware.InnoSetup"
    }

    Write-Host "Kompilujem instalator..." -ForegroundColor Cyan
    & $iscc (Join-Path $PSScriptRoot "installer.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC zlyhal (exit $LASTEXITCODE)" }

    $setup = Join-Path $PSScriptRoot "dist\ZachranaDisku-Setup-2.14.exe"
    if (-not (Test-Path $setup)) { throw "Instalator sa nevytvoril" }
    Write-Host ""
    Write-Host "Hotovo: $setup" -ForegroundColor Green
    Write-Host ("Velkost: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
}
finally {
    Pop-Location
}











