param(
  [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$OrgRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Org')
)

$ErrorActionPreference = 'Stop'

$localSourceRoot = Join-Path $RepoRoot 'fpcsrc'
$compilerRoot = Join-Path $localSourceRoot 'compiler'
$rtlRoot = Join-Path $localSourceRoot 'rtl'
$utilsRoot = Join-Path $localSourceRoot 'utils'

New-Item -ItemType Directory -Force -Path $localSourceRoot, $compilerRoot, $rtlRoot, $utilsRoot | Out-Null

Copy-Item @(
  (Join-Path $OrgRoot 'LICENSE'),
  (Join-Path $OrgRoot 'README.md'),
  (Join-Path $OrgRoot 'Makefile'),
  (Join-Path $OrgRoot 'Makefile.fpc'),
  (Join-Path $OrgRoot 'fpmake.pp'),
  (Join-Path $OrgRoot 'fpmake_add1.inc'),
  (Join-Path $OrgRoot 'fpmake_proc1.inc')
) -Destination $localSourceRoot -Force

Get-ChildItem (Join-Path $OrgRoot 'compiler') -File |
  Where-Object {
    ($_.Name -notlike 'ppc*.lpi') -or ($_.Name -in @('ppcx64.lpi', 'ppcx64llvm.lpi'))
  } |
  Copy-Item -Destination $compilerRoot -Force
Get-ChildItem (Join-Path $OrgRoot 'rtl') -File | Copy-Item -Destination $rtlRoot -Force
Get-ChildItem (Join-Path $OrgRoot 'utils') -File | Copy-Item -Destination $utilsRoot -Force

Copy-Item @(
  (Join-Path $OrgRoot 'compiler\\generic'),
  (Join-Path $OrgRoot 'compiler\\msg'),
  (Join-Path $OrgRoot 'compiler\\utils'),
  (Join-Path $OrgRoot 'compiler\\x86'),
  (Join-Path $OrgRoot 'compiler\\x86_64')
) -Destination $compilerRoot -Recurse -Force

$systemsRoot = Join-Path $compilerRoot 'systems'
New-Item -ItemType Directory -Force -Path $systemsRoot | Out-Null

Copy-Item @(
  (Join-Path $OrgRoot 'compiler\\systems\\.gitignore'),
  (Join-Path $OrgRoot 'compiler\\systems\\COPYING.txt'),
  (Join-Path $OrgRoot 'compiler\\systems\\mac_crea.txt'),
  (Join-Path $OrgRoot 'compiler\\systems\\i_win.pas'),
  (Join-Path $OrgRoot 'compiler\\systems\\t_win.pas')
) -Destination $systemsRoot -Force

Copy-Item @(
  (Join-Path $OrgRoot 'rtl\\inc'),
  (Join-Path $OrgRoot 'rtl\\objpas'),
  (Join-Path $OrgRoot 'rtl\\win'),
  (Join-Path $OrgRoot 'rtl\\win64'),
  (Join-Path $OrgRoot 'rtl\\x86_64')
) -Destination $rtlRoot -Recurse -Force

Copy-Item @(
  (Join-Path $OrgRoot 'utils\\build'),
  (Join-Path $OrgRoot 'utils\\debugsvr'),
  (Join-Path $OrgRoot 'utils\\fpcmkcfg'),
  (Join-Path $OrgRoot 'utils\\fpcres')
) -Destination $utilsRoot -Recurse -Force

Copy-Item (Join-Path $compilerRoot 'pp.pas') (Join-Path $compilerRoot 'ufpc.pas') -Force

Write-Host 'uFPC Windows x64 source baseline synchronized from Org.'
