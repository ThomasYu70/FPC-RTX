#!/usr/bin/env bash
# MinGW64 런타임 DLL을 gdb-custom/bin 으로 복사합니다.
# MSYS2 MinGW64 환경에서 실행하세요.
#
# 사용법:
#   bash install-dlls.sh
#   DEST=/c/my-gdb/bin bash install-dlls.sh

set -e

MSYS="${MSYS:-/c/msys64/mingw64/bin}"
DEST="${DEST:-/c/gdb-custom/bin}"

if [ ! -d "$MSYS" ]; then
    echo "ERROR: MinGW64 bin not found: $MSYS"
    echo "  MSYS2 설치 경로를 확인하거나 MSYS 환경변수를 설정하세요."
    exit 1
fi

if [ ! -d "$DEST" ]; then
    echo "ERROR: gdb-custom bin not found: $DEST"
    echo "  먼저 build.sh 를 실행하세요."
    exit 1
fi

echo "=== MinGW64 런타임 DLL 복사 ==="
echo "  From : $MSYS"
echo "  To   : $DEST"
echo ""

DLLS=(
    "libexpat-1.dll"
    "libgcc_s_seh-1.dll"
    "libgmp-10.dll"
    "libiconv-2.dll"
    "liblzma-5.dll"
    "libmpfr-6.dll"
    "libstdc++-6.dll"
    "libtermcap-0.dll"
    "libwinpthread-1.dll"
    "libxxhash.dll"
)

for dll in "${DLLS[@]}"; do
    src="$MSYS/$dll"
    if [ -f "$src" ]; then
        cp -v "$src" "$DEST/"
    else
        echo "WARNING: $dll 없음 — $src"
    fi
done

echo ""
echo "=== DLL 복사 완료 ==="
ls -lh "$DEST"/*.dll 2>/dev/null
