# uFPC Architecture

## Goal

uFPC is a fork of Free Pascal dedicated to the RTX64 runtime flow. It is not a
general-purpose Pascal compiler. The target is fixed to `x86_64-win64`, and the
language surface will be reduced to Structured Text compatible constructs, with
five explicit retained features:

- arrays
- records
- classes
- properties
- exception handling

Debugging is mandatory architecture, not optional tooling. The fixed debug
baseline is `DWARF` debug info with a `GDB/MI` control layer, and the runtime
image keeps the debug payload outside the executable as a companion `.dbg`
file.

## Final execution flow

The intended pipeline is:

1. `uFPCEditor` loads and edits multiple `.pas` files.
2. `uFPCManaged` coordinates compilation and deployment.
3. `uFPC` and `ufpcbridge.dll` expose compile/debug services to
   `uFPCManaged`.
4. `uFPC` compiles and links the input into an RTX64 executable payload,
   currently named `seq.bin`, and writes the DWARF companion file as
   `seq.dbg`.
5. `uFPCEditor` uses the debug architecture for breakpoints, watches, stack,
   and register inspection in development mode.
6. `uFPCEditor` sends `seq.bin` through RTX shared memory.
7. `SEQ.RTSS` stores the payload in a fixed 10 MB static memory region.
8. `uFPCEditor` requests execution and RTSS starts the loaded binary.

## Source strategy

`Org` is treated as the original upstream reference only. All source files used
to build uFPC must live under `uFPC` so the fork remains self-contained.

The current baseline keeps only the source areas that are directly relevant to
Windows x64 bootstrap work:

- compiler core
- compiler `x86` and `x86_64` backends
- compiler system tables and only the Win64 target handlers
- RTL pieces for `win`, `win64`, and `x86_64`
- RTL charmaps needed by the Windows x64 RTL build
- selected utility sources needed for later bootstrap support

## Near-term milestones

1. Bootstrap a local `ufpc.exe` from `uFPC/fpcsrc`.
2. Freeze the Windows x64 target and remove unrelated platform paths.
3. Define the exact ST-to-uFPC grammar surface.
4. Cut unsupported Pascal constructs from scanner, parser, and semantic stages.
5. Lock the compile/debug ABI for `ufpcbridge.dll` and `uFPCManaged`.
6. Add the RTX64-specific output and loader interface.
