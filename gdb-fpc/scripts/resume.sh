#!/usr/bin/env bash
# 중단된 GDB 빌드를 이어서 재개합니다.
# 이미 configure 가 완료된 /c/gdb-build/build 디렉토리에서 사용하세요.
#
# 사용법:
#   bash resume.sh
#   BUILD_DIR=/c/gdb-build/build bash resume.sh

export PATH=/c/msys64/mingw64/bin:/c/msys64/usr/bin:$PATH

BUILD_DIR="${BUILD_DIR:-/c/gdb-build/build}"
PREFIX="${PREFIX:-/c/gdb-custom}"
LOG="${LOG:-/c/gdb-build/build.log}"

if [ ! -d "$BUILD_DIR" ]; then
    echo "ERROR: build dir not found: $BUILD_DIR"
    echo "  먼저 configure.sh 를 실행하세요."
    exit 1
fi

echo "=== Build resumed: $(date) ===" | tee "$LOG"
make -j2 all-gdb -C "$BUILD_DIR" >> "$LOG" 2>&1

if [ $? -eq 0 ]; then
    echo "=== Build SUCCESS: $(date) ===" | tee -a "$LOG"
    make -C "$BUILD_DIR/gdb" install >> "$LOG" 2>&1
    echo "=== Install done: $(date) ===" | tee -a "$LOG"
    ls -lh "$PREFIX/bin/gdb.exe" | tee -a "$LOG"
    "$PREFIX/bin/gdb.exe" --version | tee -a "$LOG"
else
    echo "=== Build FAILED: $(date) ===" | tee -a "$LOG"
    echo "로그 확인: $LOG"
    exit 1
fi
