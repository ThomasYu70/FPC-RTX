# Source Baseline

The current `uFPC/fpcsrc` tree is copied from `Org` and narrowed to a Windows
x64 bootstrap scope.

Copied top-level build files:

- `LICENSE`
- `README.md`
- `Makefile`
- `Makefile.fpc`
- `fpmake.pp`
- `fpmake_add1.inc`
- `fpmake_proc1.inc`

Copied compiler scope:

- root compiler source files
- `compiler/generic`
- `compiler/msg`
- `compiler/systems` core metadata plus the Win64 target handlers only
- `compiler/utils`
- `compiler/x86`
- `compiler/x86_64`

Copied RTL scope:

- root RTL build files
- `rtl/inc`
- `rtl/charmaps`
- `rtl/objpas`
- `rtl/win`
- `rtl/win64`
- `rtl/x86_64`

Copied utility scope:

- root utility build files
- `utils/build`
- `utils/debugsvr`
- `utils/fpcmkcfg`
- `utils/fpcres`

Not copied on purpose:

- non-Windows targets
- non-x64 CPU backends
- non-Win64 system handler units
- non-x64 compiler IDE project files
- broad package sets
- installer and test trees

This keeps the fork focused on the first bootstrap milestone while ensuring the
source needed for uFPC stays under the `uFPC` directory.
