# uFPC Debug Architecture

## Why it is mandatory

`uFPC` must ship with a first-class debug architecture, not a later add-on.
The compiler, managed bridge, and editor have to agree on one stable debug
story from the start because RTX64 deployment, source mapping, and breakpoint
control all depend on it.

## Fixed baseline

- Platform: `x86_64-win64`
- Debug format: `DWARF`
- Front-end protocol: `GDB/MI`
- Mandatory debug surfaces:
  - source line breakpoints
  - watches / expression evaluation
  - register snapshots
  - call stack inspection
  - source path mapping

## Runtime layers

1. `ufpc.exe`
   - compiles and links the user program
   - emits the RTX64 runtime image as `.bin`
   - emits DWARF debug info as a separate `.dbg` companion file
2. `ufpcbridge.dll`
   - native Win64 embedding API
   - callable from `uFPCManaged`
   - publishes the fixed debug architecture contract
3. `uFPCManaged`
   - C++/CLI wrapper for the native bridge
   - feeds compile requests and later debug session requests to the editor
4. `uFPCEditor`
   - drives the user workflow
   - already models breakpoints, watches, call stack, and registers
   - uses a `GDB/MI`-style controller on the UI side

## Required build modes

- **Debug build**
  - keep DWARF info enabled
  - keep the runtime image free of embedded debug payload
  - preserve source path information
  - usable with the editor-side debug controller
- **Deploy build**
  - may strip or reduce symbols later
  - must still keep a deterministic mapping strategy for the development build

## Current implementation baseline

- `uFPC/fpcsrc/compiler/ufpclang.pas` starts enforcing the reduced language
  surface for user code.
- `uFPC/sdk/native/ufpcbridge.lpr` defines the native Win64 bridge DLL.
- `uFPC/sdk/native/ufpc_api.h` fixes the ABI for compile/debug integration.
- `uFPC/scripts/Build-HostBridge.ps1` builds the bridge DLL with the installed
  host FPC toolchain.

## Immediate next steps

1. Extend the bridge from compile-only to a real debug session backend.
2. Emit a debug manifest alongside `seq.bin` for editor/managed/runtime
   coordination.
3. Bind `uFPCEditor` to `uFPCManaged` instead of shelling out directly to
   `fpc`/`gdb`.
