param(
  [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$FpcRoot = 'C:\FPC\3.2.2'
)

$ErrorActionPreference = 'Stop'

$hostBin = Join-Path $FpcRoot 'bin\i386-win32'
$hostCompiler = Join-Path $hostBin 'ppcrossx64.exe'
$sourceFile = Join-Path $RepoRoot 'sdk\native\ufpcbridge.lpr'
$outputDir = Join-Path $RepoRoot 'build\sdk'
$unitDir = Join-Path $RepoRoot 'build\sdk\units'
$searchPaths = @(
  (Join-Path $FpcRoot 'units\x86_64-win64\rtl'),
  (Join-Path $FpcRoot 'units\x86_64-win64\rtl-objpas'),
  (Join-Path $FpcRoot 'units\x86_64-win64\fcl-base'),
  (Join-Path $FpcRoot 'units\x86_64-win64\fcl-process')
)

if (-not (Test-Path $hostCompiler)) {
  throw "ppcrossx64.exe not found at $hostCompiler"
}

if (-not (Test-Path $sourceFile)) {
  throw "Bridge source not found at $sourceFile"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $unitDir | Out-Null

$args = @(
  '-Twin64',
  '-Px86_64',
  '-Mobjfpc',
  '-Scghi',
  '-O1',
  "-FE$outputDir",
  "-FU$unitDir"
)

foreach ($path in $searchPaths) {
  $args += "-Fu$path"
}

$args += $sourceFile

Push-Location (Split-Path -Parent $sourceFile)
try {
  & $hostCompiler @args
  if ($LASTEXITCODE -ne 0) {
    throw "Host bridge build failed with exit code $LASTEXITCODE"
  }
}
finally {
  Pop-Location
}

Write-Host "Built bridge DLL: $(Join-Path $outputDir 'ufpcbridge.dll')"
