# uFPCManaged

`uFPCManaged` is the C++/CLI bridge between the editor and the native
`ufpcbridge.dll` layer.

Planned responsibilities:

- accept managed compile requests from `uFPCEditor`
- forward them to `ufpcbridge.dll`
- expose host-side executable copy support for `.bin` runtime images
- expose the fixed debug architecture contract to the editor
- later host debug session orchestration for the `DWARF` + `GDB/MI` stack

Current baseline:

- `uFPC/sdk/native/ufpc_api.h` defines the native ABI
- `uFPC/sdk/native/ufpcbridge.lpr` provides the native Win64 DLL source
- `uFPCManaged/uFPCManaged` contains the initial C++/CLI wrapper scaffold
- `CompilerBridge.CreateHostExecutable(...)` prepares a host `.exe` copy from a
  compiled `.bin` runtime image while keeping the companion `.dbg` mapping
- `CompilerBridge.CompileAndCreateHostExecutable(...)` runs the compiler first
  and then prepares the host `.exe` artifact in one managed call
- native bridge failures are recorded in `ufpcbridge.exception.log` next to
  `ufpcbridge.dll`
- managed wrapper exceptions and bridge-call failures are recorded in
  `uFPCManaged.exception.log` in the host application's base directory
