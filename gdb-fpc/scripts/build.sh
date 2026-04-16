#!/usr/bin/env bash
# GDB 빌드 및 설치
# MSYS2 MinGW64 환경에서 실행하세요.
#
# 사전 조건:
#   1. configure.sh 실행 완료
#   2. 현재 디렉토리가 build/ 임
#
# 사용법:
#   cd /c/gdb-build/build
#   bash /path/to/build.sh

set -e

PREFIX="${PREFIX:-/c/gdb-custom}"

echo "=== Build started: $(date) ==="
make -j4 all-gdb
echo "=== Build done: $(date) ==="

echo "=== Installing to $PREFIX ==="
make -C gdb install

echo ""
echo "=== Installed GDB ==="
ls -lh "$PREFIX/bin/gdb.exe"
"$PREFIX/bin/gdb.exe" --version | head -1
