# Build Zdenko Zachráň ma do jedneho spustitelneho suboru.
# Vysledok: .\dist\ZachranaDisku.exe
param(
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $out = Join-Path $PSScriptRoot "dist"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    $publishArgs = @(
        "publish", (Join-Path $PSScriptRoot "NtfsRescue.csproj"),
        "-c", "Release",
        "-r", "win-x64",
        "-o", $out,
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )
    if ($FrameworkDependent) {
        $publishArgs += "-p:SelfContained=false"
    } else {
        $publishArgs += "-p:SelfContained=true"
    }

    Write-Host "Publikujem..." -ForegroundColor Cyan
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish zlyhal (exit $LASTEXITCODE)" }

    $exe = Join-Path $out "ZachranaDisku.exe"
    if (-not (Test-Path $exe)) { throw "ZachranaDisku.exe sa nevytvoril" }
    Write-Host ""
    Write-Host "Hotovo: $exe" -ForegroundColor Green
    Write-Host ("Velkost: {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
}
finally {
    Pop-Location
}
