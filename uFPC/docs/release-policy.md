# uFPC Release Policy

This document defines how `uFPC` revisions are named, recorded, and promoted.
It complements `uFPC/docs/revision-history.md`, which records the actual
project milestones.

## Purpose

`uFPC` is not being tracked by Git in this workspace, so release discipline
must be maintained by project documents. The release policy keeps the
compiler, bridge, managed wrapper, runtime output, and documentation aligned.

## Current baseline

- Current recorded baseline: `Rev 0.11`
- Next planned baseline: `Rev 0.12`
- Current phase: pre-1.0 architecture and stabilization

## Revision numbering

`uFPC` uses a simple `Rev major.minor` format.

- `major`
  - increments when a compatibility boundary changes
  - examples:
    - language contract changes
    - compile/debug ABI changes
    - runtime output contract changes
    - deployment pipeline changes
- `minor`
  - increments for additive work within the same compatibility line
  - examples:
    - supported syntax expansion
    - validation fixes
    - bootstrap improvements
    - documentation and sample expansion
    - bridge implementation progress without ABI break

## Release phases

- `Rev 0.x`
  - bootstrap and architecture phase
  - contracts may still be refined
  - major design choices must be documented immediately
- `Rev 1.0`
  - first frozen baseline for:
    - RTX64-only target contract
    - supported language surface
    - `.bin` + `.dbg` artifact contract
    - native bridge ABI for editor integration
- `Rev 1.x`
  - compatible feature growth and bug fixes after the 1.0 freeze
- `Rev 2.0` and later
  - use only when a breaking contract change is intentional and documented

## When to increment the revision

Increment the revision whenever one of the following changes is completed:

- compiler target behavior
- language acceptance or rejection rules
- bootstrap or RTL compatibility behavior
- debug architecture behavior
- bridge ABI or managed wrapper behavior
- output artifact naming or deployment contract
- mandatory sample or validation baseline

Do not increment the revision for temporary local experiments that are not
accepted into the project baseline.

## Mandatory update set for every revision

For every new revision, update all applicable items below:

1. `uFPC/docs/revision-history.md`
   - add a new dated revision entry
   - summarize the actual completed milestone
2. `uFPC/README.md`
   - update baseline statements if the visible project contract changed
3. architecture documents
   - update when compile, debug, target, or deployment flow changed:
     - `uFPC/docs/architecture.md`
     - `uFPC/docs/debug-architecture.md`
     - `uFPC/docs/source-baseline.md`
4. validation samples
   - update or extend samples if the language surface changed
5. bridge and wrapper documentation
   - update if ABI or integration behavior changed

## Recommended revision entry format

Each revision entry in `revision-history.md` should follow this structure:

```text
## YYYY-MM-DD - Rev X.Y - Short title

- Completed milestone 1
- Completed milestone 2
- Completed milestone 3
```

Keep entries short, factual, and tied to accepted project state.

## Required verification before recording a revision

Before a new revision is recorded, verify the items that apply to the change:

- `uFPC/build/bin/ufpc.exe` builds successfully
- `ufpcbridge.dll` builds successfully when bridge code changes
- `uFPCManaged` builds successfully when wrapper code changes
- sample source compiles successfully when language or compiler behavior changes
- runtime artifacts are generated correctly:
  - `.bin`
  - `.dbg`

## Revision ownership rule

Only record a revision after the related source, scripts, and documents are in
a coherent state. If a milestone is partially complete, keep it in working
notes and do not promote it into the revision history yet.
