#!/usr/bin/env bash
# GDB 16.3 FPC 빌드 — 전체 과정 (다운로드 → 패치 → configure → 빌드 → DLL 복사)
# MSYS2 MinGW64 환경에서 실행하세요.
#
# 사용법:
#   bash setup.sh
#
# 환경변수로 경로를 변경할 수 있습니다:
#   GDB_BUILD_DIR=/c/gdb-build
#   PREFIX=/c/gdb-custom
#   JOBS=4

set -e

export PATH=/c/msys64/mingw64/bin:/c/msys64/usr/bin:$PATH

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
GDB_VERSION="${GDB_VERSION:-16.3}"
GDB_BUILD_DIR="${GDB_BUILD_DIR:-/c/gdb-build}"
GDB_SRC="$GDB_BUILD_DIR/gdb-$GDB_VERSION"
BUILD_DIR="$GDB_BUILD_DIR/build"
PREFIX="${PREFIX:-/c/gdb-custom}"
JOBS="${JOBS:-4}"
TARBALL="gdb-$GDB_VERSION.tar.gz"
GDB_URL="https://ftp.gnu.org/gnu/gdb/$TARBALL"

echo "============================================"
echo "  GDB $GDB_VERSION FPC 커스텀 빌드"
echo "  Build dir : $GDB_BUILD_DIR"
echo "  Install   : $PREFIX"
echo "  Jobs      : $JOBS"
echo "============================================"
echo ""

# ── 1. 사전 조건 패키지 확인 ──────────────────────────────────────────────────
echo "[1/6] 사전 조건 확인..."
for cmd in gcc g++ make patch wget tar; do
    if ! command -v $cmd &>/dev/null; then
        echo "ERROR: '$cmd' 없음. MSYS2에서 설치하세요:"
        echo "  pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-make autoconf automake make texinfo patch wget"
        exit 1
    fi
done
echo "  OK"

# ── 2. 소스 다운로드 ──────────────────────────────────────────────────────────
mkdir -p "$GDB_BUILD_DIR"
if [ ! -f "$GDB_BUILD_DIR/$TARBALL" ]; then
    echo "[2/6] GDB $GDB_VERSION 소스 다운로드..."
    wget --no-check-certificate -O "$GDB_BUILD_DIR/$TARBALL" "$GDB_URL"
else
    echo "[2/6] 소스 이미 존재: $GDB_BUILD_DIR/$TARBALL"
fi

# ── 3. 압축 해제 ──────────────────────────────────────────────────────────────
if [ ! -d "$GDB_SRC" ]; then
    echo "[3/6] 압축 해제..."
    tar xzf "$GDB_BUILD_DIR/$TARBALL" -C "$GDB_BUILD_DIR"
else
    echo "[3/6] 소스 디렉토리 이미 존재: $GDB_SRC"
fi

# ── 4. FPC 패치 적용 ──────────────────────────────────────────────────────────
echo "[4/6] FPC Pascal 지원 패치 적용..."
GDB_SRC="$GDB_SRC" bash "$SCRIPT_DIR/patch.sh"

# ── 5. Configure + 빌드 ───────────────────────────────────────────────────────
echo "[5/6] Configure..."
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"
GDB_SRC="$GDB_SRC" PREFIX="$PREFIX" bash "$SCRIPT_DIR/configure.sh"

echo ""
echo "[5/6] 빌드 시작 (약 20~30분)..."
JOBS="$JOBS" PREFIX="$PREFIX" bash "$SCRIPT_DIR/build.sh"

# ── 6. 런타임 DLL 복사 ────────────────────────────────────────────────────────
echo ""
echo "[6/6] MinGW64 런타임 DLL 복사..."
DEST="$PREFIX/bin" bash "$SCRIPT_DIR/install-dlls.sh"

echo ""
echo "============================================"
echo "  빌드 완료!"
echo "  실행파일: $PREFIX/bin/gdb.exe"
echo "============================================"
"$PREFIX/bin/gdb.exe" --version | head -1
