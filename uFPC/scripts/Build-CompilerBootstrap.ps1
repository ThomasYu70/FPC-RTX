param(
  [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$FpcRoot = 'C:\FPC\3.2.2',
  [string]$CompilerName = 'ufpc.exe'
)

$ErrorActionPreference = 'Stop'

$hostBin = Join-Path $FpcRoot 'bin\i386-win32'
$makeExe = Join-Path $hostBin 'make.exe'
$hostCompiler = Join-Path $hostBin 'ppcrossx64.exe'
$sourceRoot = Join-Path $RepoRoot 'fpcsrc'
$compilerRoot = Join-Path $sourceRoot 'compiler'
$buildBin = Join-Path $RepoRoot 'build\bin'
$builtCompiler = Join-Path $compilerRoot $CompilerName
$stagedCompiler = Join-Path $buildBin $CompilerName

if (-not (Test-Path $makeExe)) {
  throw "make.exe not found at $makeExe"
}

if (-not (Test-Path $hostCompiler)) {
  throw "ppcrossx64.exe not found at $hostCompiler"
}

New-Item -ItemType Directory -Force -Path $buildBin | Out-Null
$env:PATH = "$hostBin;$env:PATH"

Push-Location $compilerRoot
try {
  & $makeExe compiler `
    FPC=$hostCompiler `
    PP=$hostCompiler `
    CPU_TARGET=x86_64 `
    OS_TARGET=win64 `
    EXENAME=$CompilerName `
    COMPILEREXENAME=$CompilerName

  if ($LASTEXITCODE -ne 0) {
    throw "Compiler bootstrap failed with exit code $LASTEXITCODE"
  }
}
finally {
  Pop-Location
}

if (-not (Test-Path $builtCompiler)) {
  throw "Expected compiler output not found: $builtCompiler"
}

Copy-Item $builtCompiler $stagedCompiler -Force
Write-Host "Staged compiler: $stagedCompiler"
