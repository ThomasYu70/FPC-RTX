# gdb-fpc — FPC Pascal 지원 개선 GDB 빌드

uFPC(Structured Text FPC) 전용 커스텀 GDB 빌드.  
GDB 16.3 소스에 FPC 고유 타입(`AnsiString` 등) 출력 개선 패치를 적용합니다.

---

## 개요

표준 GDB는 FPC `AnsiString` 변수를 포인터 주소만 출력합니다.  
이 패치를 적용하면 실제 문자열 내용을 출력합니다.

```
패치 전: s = 0x7fff12345678
패치 후: s = 'Hello, World!'
```

### 지원 타입

| FPC 타입 | 패치 전 | 패치 후 |
|---|---|---|
| `AnsiString` | 주소만 | 문자열 내용 |
| `RawByteString` | 주소만 | 문자열 내용 |
| `UTF8String` | 주소만 | 문자열 내용 |
| `ShortString` | GDB 기본 처리 | 변경 없음 |

### AnsiString 메모리 레이아웃 (x64)

```
ptr - 24 : CodePage   (uint16)
ptr - 22 : ElemSize   (uint16)
ptr - 20 : Padding    (uint32)
ptr -  8 : Length     (int64 = SizeInt)
ptr +  0 : Data[0]    ← AnsiString 변수가 가리키는 위치
```

---

## 빌드 방법

### 사전 조건

- MSYS2 MinGW64 (`C:\msys64`)
- 필요 패키지:

```bash
pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-make \
  autoconf automake make texinfo patch wget
```

### 단계

```bash
# 1. GDB 소스 다운로드
mkdir C:\gdb-build && cd /c/gdb-build
wget --no-check-certificate https://ftp.gnu.org/gnu/gdb/gdb-16.3.tar.gz
tar xzf gdb-16.3.tar.gz

# 2. 패치 적용
GDB_SRC=/c/gdb-build/gdb-16.3 bash scripts/patch.sh

# 3. Configure
mkdir /c/gdb-build/build && cd /c/gdb-build/build
bash /path/to/gdb-fpc/scripts/configure.sh

# 4. 빌드 (20~30분)
bash /path/to/gdb-fpc/scripts/build.sh
```

설치 위치: `C:\gdb-custom\bin\gdb.exe`

---

## 수정된 파일

| 파일 | 위치 | 변경 내용 |
|---|---|---|
| `p-lang.h` | `gdb/p-lang.h` | `fpc_is_ansistring_type()` 선언 추가 |
| `p-lang.c` | `gdb/p-lang.c` | `fpc_is_ansistring_type()` 구현 추가 |
| `p-valprint.c` | `gdb/p-valprint.c` | `fpc_print_ansistring()` 헬퍼 + 호출 추가 |

전체 수정 파일은 `src/` 디렉토리, 패치 파일은 `patches/` 디렉토리에 있습니다.

---

## Configure 옵션 (Windows x64 전용 슬림)

| 옵션 | 이유 |
|---|---|
| `--target=x86_64-w64-mingw32` | x64 Windows 전용 |
| `--disable-tui` | GDB/MI 전용 (터미널 UI 불필요) |
| `--disable-nls` | 영어 메시지 고정 → IDE 파서 안정화 |
| `--disable-sim` | CPU 시뮬레이터 제거 |
| `--disable-gdbserver` | 로컬 디버깅만 사용 |
| `--with-python=no` | Python 제거 (슬림화) |

---

## uFPCEditor 연동

`uFPCEditor/uFPCEditor/App.xaml.cs`의 `FindGdb()`가  
`C:\gdb-custom\bin\gdb.exe`를 최우선으로 탐색합니다.
