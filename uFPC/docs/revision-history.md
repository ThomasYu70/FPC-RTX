# uFPC Revision History

This document is a manually maintained revision history reconstructed from the
current workspace baseline. The repository is not under Git revision control in
this environment, so the entries below describe project milestones rather than
source-control commits. Revision numbering rules are defined in
`uFPC/docs/release-policy.md`.

## 2026-04-16 - Rev 0.1 - Local source baseline

- Created the standalone `uFPC` project structure.
- Copied the required Free Pascal source baseline into `uFPC/fpcsrc` so the
  fork does not depend on `Org` at build time.
- Added bootstrap and sync scripts for the local source tree:
  - `uFPC/scripts/Sync-OrgWin64.ps1`
  - `uFPC/scripts/Build-CompilerBootstrap.ps1`
- Added initial project documentation:
  - `uFPC/README.md`
  - `uFPC/docs/source-baseline.md`
  - `uFPC/docs/architecture.md`

## 2026-04-16 - Rev 0.2 - Host bootstrap compiler

- Bootstrapped a local `ufpc.exe` under `uFPC/build/bin`.
- Fixed local compiler source compatibility needed for the staged build.
- Confirmed the staged compiler reports the expected compiler version.

## 2026-04-16 - Rev 0.3 - Reduced language gate

- Added the `uFPC` language restriction layer in
  `uFPC/fpcsrc/compiler/ufpclang.pas`.
- Integrated declaration and AST validation so unsupported Pascal features are
  rejected for user code.
- Kept the mandatory retained language features available:
  - arrays
  - records
  - classes
- Later expanded the retained features to also include:
  - properties
  - exception handling

## 2026-04-16 - Rev 0.4 - Native bridge and managed wrapper

- Added the native Win64 bridge DLL project for host integration:
  - `uFPC/sdk/native/ufpc_api.h`
  - `uFPC/sdk/native/ufpcbridge.lpr`
  - `uFPC/scripts/Build-HostBridge.ps1`
- Added the `uFPCManaged` C++/CLI wrapper used by the editor-side tooling.
- Fixed the compile/debug ABI contract between `ufpc.exe`,
  `ufpcbridge.dll`, and `uFPCManaged`.

## 2026-04-16 - Rev 0.5 - Mandatory debug architecture

- Formalized the fixed debug architecture for the fork.
- Locked the baseline to:
  - platform: `x86_64-win64`
  - debug format: `DWARF`
  - control protocol: `GDB/MI`
- Documented the compile/debug flow in:
  - `uFPC/docs/debug-architecture.md`
  - `uFPC/docs/architecture.md`
- Fixed the build flow to emit the runtime image and debug payload separately:
  - runtime image: `.bin`
  - debug info: `.dbg`

## 2026-04-16 - Rev 0.6 - RTX64-only target freeze

- Removed non-required platform paths from the active compiler source scope.
- Fixed the fork to target RTX64 / Win64 x64 only.
- Preserved compatible target directory naming for RTL and unit builds while
  keeping the user-facing target identity as `rtx64`.
- Updated compiler system definitions so the default executable payload is
  emitted as `.bin`.

## 2026-04-16 - Rev 0.7 - RTL compatibility and validation refinements

- Rebuilt the RTL against the current staged `ufpc.exe`.
- Added compatibility handling so `WIN64`-based RTL code paths continue to
  compile within the RTX64-specific fork.
- Refined the validation layer so internal compiler and RTL-generated symbols
  are not rejected as unsupported user features.
- Refined declaration validation so internal symbols used by class/property
  support do not block valid user programs.

## 2026-04-16 - Rev 0.8 - Language sample validation baseline

- Added a language showcase sample for supported `uFPC` constructs:
  - `uFPC/samples/language/SampleSupport.pas`
  - `uFPC/samples/language/CommandShowcase.pas`
- Verified successful compilation with the staged compiler.
- Confirmed runtime and debug artifacts are generated correctly:
  - `CommandShowcase.bin`
  - `CommandShowcase.dbg`
- Confirmed the `.bin` output is a PE-format executable image intended for the
  RTX64 runtime flow.

## 2026-04-16 - Rev 0.9 - Host executable bridge support

- Added a native bridge API for preparing a host `.exe` copy from a compiled
  `.bin` runtime image:
  - `uFPC/sdk/native/ufpc_api.h`
  - `uFPC/sdk/native/ufpcbridge.lpr`
- Added the matching `uFPCManaged` wrapper entry point so editor-side code can
  request host executable preparation later without duplicating file logic.
- Preserved companion debug mapping by copying the `.dbg` file when the output
  executable name changes.

## 2026-04-16 - Rev 0.10 - Managed combined compile and host executable flow

- Added `CompilerBridge.CompileAndCreateHostExecutable(...)` to
  `uFPCManaged` so managed callers can run the compiler and prepare the host
  executable in one step.
- Kept the combined method orchestration in the managed layer so the existing
  native compile API and host-executable API remain reusable independently.
- Verified the combined flow against the sample program and confirmed the
  expected `.bin`, `.exe`, and `.dbg` artifacts are produced.

## 2026-04-16 - Rev 0.11 - Exception logging for bridge diagnostics

- Added persistent native bridge diagnostics logging in
  `ufpcbridge.exception.log` for invalid requests, launch failures, internal
  exceptions, and debug-session stub errors.
- Added managed wrapper diagnostics logging in
  `uFPCManaged.exception.log` for managed exceptions and failed bridge calls.
- Verified log generation so issue tracing can be done without reproducing the
  problem under a debugger.

## Current baseline summary

- Compiler executable: `uFPC/build/bin/ufpc.exe`
- Native bridge DLL: `uFPC/build/sdk/ufpcbridge.dll`
- Managed wrapper: `uFPCManaged`
- Target platform: `RTX64 / x86_64-win64`
- Runtime output: `.bin`
- Debug output: `.dbg`
- Current recorded revision: `Rev 0.11`
- Required retained language features:
  - arrays
  - records
  - classes
  - properties
  - exception handling
