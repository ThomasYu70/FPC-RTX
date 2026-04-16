#!/usr/bin/env bash
# GDB 소스에 FPC Pascal 지원 패치를 적용합니다.
#
# 사용법:
#   GDB_SRC=/c/gdb-build/gdb-16.3 bash patch.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PATCH_DIR="$SCRIPT_DIR/../patches"
GDB_SRC="${GDB_SRC:-/c/gdb-build/gdb-16.3}"

echo "Applying FPC Pascal support patch to: $GDB_SRC/gdb/"

patch -p0 -d "$GDB_SRC/gdb" < "$PATCH_DIR/p-lang.h.patch"
patch -p0 -d "$GDB_SRC/gdb" < "$PATCH_DIR/p-lang.c.patch"
patch -p0 -d "$GDB_SRC/gdb" < "$PATCH_DIR/p-valprint.c.patch"

echo "Patch applied successfully."
