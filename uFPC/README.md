# uFPC

uFPC is a reduced Free Pascal Compiler fork for the RTX64 Windows x64 target.

Current bootstrap scope:

- Source baseline is copied into `uFPC/fpcsrc` so `Org` can be removed later.
- Target platform is fixed to `x86_64-win64` / RTX64.
- User program output is fixed to a runtime image such as `seq.bin`.
- Debug information is emitted separately as a `.dbg` companion file.
- The future language surface is limited to the Structured Text 1:1 subset.
- Arrays, records, classes, properties, and exception handling stay available
  even if ST does not map them 1:1.

Current repository layout:

- `docs` - architecture notes, source scope, revision history, and release policy.
- `fpcsrc` - local FPC source baseline used for the fork.
- `sdk` - native bridge ABI and DLL sources for embedding uFPC.
- `scripts` - sync and bootstrap helper scripts.
- `build` - staged compiler binaries and intermediate outputs.
- `artifacts` - generated runtime images such as `seq.bin` and companion `.dbg` files.

Immediate implementation phases:

1. Keep the source baseline local to `uFPC`.
2. Bootstrap a host `ufpc.exe` for `x86_64-win64`.
3. Remove non-ST syntax and semantics from the parser and semantic phases.
4. Preserve arrays, records, classes, properties, and exception handling as
   mandatory extensions.
5. Keep the mandatory debug architecture (`DWARF` + `GDB/MI`) with external
   `.dbg` output for RTX64 builds.
6. Add the native bridge DLL for `uFPCManaged`.
7. Add the `seq.bin` output path for the RTX64 loader pipeline.
