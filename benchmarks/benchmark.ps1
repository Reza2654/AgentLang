#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reproducible AgentLang binary footprint & latency benchmark suite.
.DESCRIPTION
    Publishes standalone self-contained CLI binaries and measures exact file sizes,
    CLI startup latency, 24-file static analysis + JSON emission time, and .agtc bytecode size.
#>

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$Rid = if ($IsWindows -or $env:OS -eq "Windows_NT") { "win-x64" } elseif ($IsMacOS) { "osx-arm64" } else { "linux-x64" }
$ExeExt = if ($Rid -eq "win-x64") { ".exe" } else { "" }

$TrimDir = Join-Path $RepoRoot "artifacts/bench-trimmed"
$R2RDir  = Join-Path $RepoRoot "artifacts/bench-r2r"

Write-Host "=== AgentLang Benchmark Suite ($Rid) ===" -ForegroundColor Cyan

Write-Host "`n[1/4] Publishing Trimmed + Compressed Self-Contained Single-File..." -ForegroundColor Yellow
dotnet publish src/AgentLang.Cli/AgentLang.Cli.csproj -c Release -r $Rid `
    --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true `
    -p:EnableCompressionInSingleFile=true -o $TrimDir --nologo | Out-Null

$TrimExe = Join-Path $TrimDir "AgentLang.Cli$ExeExt"
$TrimSizeMB = [math]::Round((Get-Item $TrimExe).Length / 1MB, 2)

Write-Host "[2/4] Publishing ReadyToRun Uncompressed Self-Contained Single-File..." -ForegroundColor Yellow
dotnet publish src/AgentLang.Cli/AgentLang.Cli.csproj -c Release -r $Rid `
    --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=false -o $R2RDir --nologo | Out-Null

$R2RExe = Join-Path $R2RDir "AgentLang.Cli$ExeExt"
$R2RSizeMB = [math]::Round((Get-Item $R2RExe).Length / 1MB, 2)

function Measure-AvgMs {
    param([scriptblock]$Action, [int]$Runs = 5)
    # Warm-up run
    & $Action | Out-Null
    $times = 1..$Runs | ForEach-Object { (Measure-Command $Action).TotalMilliseconds }
    return [math]::Round(($times | Measure-Object -Average).Average, 1)
}

Write-Host "[3/4] Measuring CLI Startup & 24-File Check Latency (5 runs each)..." -ForegroundColor Yellow
$TrimVersionMs = Measure-AvgMs { & $TrimExe --version }
$TrimCheckMs   = Measure-AvgMs { & $TrimExe check examples/ --json }

$R2RVersionMs  = Measure-AvgMs { & $R2RExe --version }
$R2RCheckMs    = Measure-AvgMs { & $R2RExe check examples/ --json }

Write-Host "[4/4] Measuring Bytecode Compilation (.agtc)..." -ForegroundColor Yellow
$AgctPath = Join-Path $RepoRoot "artifacts/hello.agtc"
$CompileMs = Measure-AvgMs { & $TrimExe compile examples/hello-agent/main.agent -o $AgctPath }
$AgctBytes = (Get-Item $AgctPath).Length

$RuntimeVer = (& $TrimExe --version)
Write-Host "`n=== Verified Benchmark Results ($Rid — $RuntimeVer) ===" -ForegroundColor Green
Write-Host "| Configuration | Binary Size | ``--version`` (avg) | ``check examples/ --json`` (24 files) | ``hello.agtc`` |"
Write-Host "| :--- | :---: | :---: | :---: | :---: |"
Write-Host "| Self-Contained + Trimmed + Compressed | **$TrimSizeMB MB** | ${TrimVersionMs} ms | ${TrimCheckMs} ms | ${AgctBytes} bytes ($CompileMs ms) |"
Write-Host "| Self-Contained + ReadyToRun (Uncompressed) | **$R2RSizeMB MB** | ${R2RVersionMs} ms | ${R2RCheckMs} ms | ${AgctBytes} bytes |"
