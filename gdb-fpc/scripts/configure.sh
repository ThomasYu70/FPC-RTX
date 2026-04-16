#!/usr/bin/env bash
# GDB 16.3 — Windows x64 전용 슬림 빌드 (uFPC용)
# MSYS2 MinGW64 환경에서 실행하세요.
#
# 사용법:
#   cd /c/gdb-build
#   mkdir build && cd build
#   bash /path/to/configure.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
GDB_SRC="${GDB_SRC:-/c/gdb-build/gdb-16.3}"
PREFIX="${PREFIX:-/c/gdb-custom}"

echo "=== GDB configure (Windows x64 slim) ==="
echo "  Source : $GDB_SRC"
echo "  Prefix : $PREFIX"

"$GDB_SRC/configure"           \
  --host=x86_64-w64-mingw32    \
  --target=x86_64-w64-mingw32  \
  --prefix="$PREFIX"           \
  --enable-targets=x86_64-w64-mingw32 \
  --disable-tui                \
  --disable-nls                \
  --disable-sim                \
  --disable-gdbserver          \
  --disable-gdbtk              \
  --without-guile              \
  --with-python=no             \
  --disable-werror

echo "=== Configure done. Run: make -j4 all-gdb ==="
