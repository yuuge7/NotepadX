# Measures how long the published executable takes to put a window on screen, with and
# without single-file compression, so the size/startup trade-off is a decision based on
# numbers rather than a guess.
#
#   .\packaging\measure-startup.ps1
#
# WPF supports neither trimming nor NativeAOT, so compression is the only size lever
# that exists for this app.

param([int]$Runs = 6)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Publish-Variant([string]$name, [bool]$compress) {
    $out = Join-Path $root "dist\measure\$name"
    Write-Host "publishing $name (compression: $compress)" -ForegroundColor Cyan

    dotnet publish NotepadX.csproj -c Release -r win-x64 -o $out `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=$($compress.ToString().ToLower()) `
        -p:DebugType=none --nologo | Out-Null

    if ($LASTEXITCODE -ne 0) { throw "publish failed for $name" }
    return Join-Path $out 'NotepadX.exe'
}

function Clear-ExtractionCache {
    # A compressed single-file app unpacks native libraries here on first run and reuses
    # them afterwards, which is exactly the difference worth measuring.
    $cache = Join-Path $env:TEMP '.net\NotepadX'
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
}

function Measure-Launch([string]$exe) {
    Get-Process NotepadX -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $exe -ArgumentList '-n' -PassThru

    while (-not $p.HasExited -and $p.MainWindowHandle -eq 0 -and $sw.ElapsedMilliseconds -lt 30000) {
        Start-Sleep -Milliseconds 5
        $p.Refresh()
    }
    $sw.Stop()

    $elapsed = $sw.Elapsed.TotalMilliseconds
    if (-not $p.HasExited) { $p.Kill() }
    Start-Sleep -Milliseconds 300
    return [Math]::Round($elapsed)
}

$results = @()

foreach ($variant in @(@{ Name = 'compressed'; Compress = $true }, @{ Name = 'uncompressed'; Compress = $false })) {
    $exe = Publish-Variant $variant.Name $variant.Compress
    $sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)

    Clear-ExtractionCache
    $first = Measure-Launch $exe

    $warm = @()
    for ($i = 0; $i -lt $Runs; $i++) { $warm += Measure-Launch $exe }
    $warmSorted = $warm | Sort-Object

    $results += [pscustomobject]@{
        Variant      = $variant.Name
        SizeMB       = $sizeMb
        FirstRunMs   = $first
        WarmMedianMs = $warmSorted[[int]($warmSorted.Count / 2)]
        WarmMinMs    = $warmSorted[0]
        WarmMaxMs    = $warmSorted[-1]
    }
}

Write-Host ''
$results | Format-Table -AutoSize
