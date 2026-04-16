# uFPCEditor — Free Pascal IDE (C# / WPF 재구현)

---

## 개요

**uFPCEditor**는 오리지널 Free Pascal Compiler IDE(`Org/`)를 C# .NET 9.0 WPF로 재구현한 데스크톱 통합 개발 환경입니다.  
파스칼 소스 파일의 편집·컴파일·디버깅을 하나의 창에서 수행할 수 있습니다.

### 목표
- 원본 FPC IDE(Pascal 구현)의 기능을 C# MVVM 아키텍처로 1:1 대응
- GDB/MI 프로토콜 기반 네이티브 디버거 통합
- 현대적인 WPF UI(AvalonEdit 에디터, AvalonDock 도킹 레이아웃) 적용

---

## 기술 스택

| 구분 | 내용 |
|------|------|
| 언어 | C# 12 / .NET 9.0-windows |
| UI 프레임워크 | WPF (Windows Presentation Foundation) |
| 코드 에디터 | AvalonEdit 6.3.0.90 (로컬 NuGet 패키지) |
| 도킹 레이아웃 | Dirkster.AvalonDock 4.73.0 |
| MVVM 툴킷 | CommunityToolkit.Mvvm 8.3.2 |
| DI 컨테이너 | Microsoft.Extensions.DependencyInjection 9.0.0 |
| 빌드 대상 | x64, Windows |
| IDE | Visual Studio 2022 Pro |

---

## 프로젝트 구조

```
uFPCEditor/
├── uFPCEditor.sln                  솔루션 (Debug|x64, Release|x64)
├── nuget.config                    로컬 패키지 소스 (avalonedit)
├── packages/                       로컬 NuGet 패키지
│   └── avalonedit.6.3.0.90.nupkg
└── uFPCEditor/                     C# 프로젝트
    ├── App.xaml / App.xaml.cs      앱 진입점, DI 컨테이너 구성
    ├── ServiceLocator.cs           XAML 바인딩용 ViewModel 접근자
    ├── MainWindow.xaml / .cs       메인 창 (메뉴, 툴바, 상태바, 도킹 레이아웃)
    ├── Core/
    │   ├── Compiler/
    │   │   ├── FpcCompiler.cs      FPC 컴파일러 래퍼 (비동기, 출력 파싱)
    │   │   └── CompilerTypes.cs    CompileResult, CompilerMessage, CompileMode
    │   ├── Debugger/
    │   │   ├── GdbMi/
    │   │   │   ├── GdbMiTypes.cs   GDB/MI 데이터 타입 (ResultRecord, AsyncRecord…)
    │   │   │   ├── GdbMiParser.cs  GDB/MI 출력 파서
    │   │   │   └── GdbMiProcess.cs GDB 프로세스 관리 및 MI 명령 전송
    │   │   ├── DebugController.cs  디버거 세션 전체 조율 (TDebugController 대응)
    │   │   ├── Breakpoint.cs       브레이크포인트 모델
    │   │   ├── Watch.cs            와치 모델
    │   │   └── StackFrame.cs       호출 스택 프레임 모델
    │   └── Editor/
    │       └── PascalHighlightingLoader.cs  XSHD 구문 강조 등록
    ├── ViewModels/
    │   ├── ViewModelBase.cs        ObservableObject 기반 공통 ViewModel
    │   ├── MainViewModel.cs        메인 창 ViewModel (메뉴·툴바 명령 전체)
    │   ├── EditorViewModel.cs      열린 파일 탭 ViewModel (AvalonEdit Document)
    │   ├── BreakpointsViewModel.cs 브레이크포인트 목록 뷰모델
    │   ├── WatchesViewModel.cs     와치 목록 뷰모델
    │   ├── CallStackViewModel.cs   호출 스택 뷰모델
    │   ├── RegistersViewModel.cs   레지스터 뷰모델
    │   └── CompilerViewModel.cs    컴파일러 메시지 뷰모델
    ├── Views/
    │   ├── SourceEditorView.xaml/cs  코드 에디터 뷰 (BreakpointMargin, DebugLineColorizer)
    │   ├── PaneTemplateSelector.cs   AvalonDock DataTemplate 선택자
    │   ├── Debug/
    │   │   ├── BreakpointsView      브레이크포인트 도구창
    │   │   ├── WatchesView          와치 도구창
    │   │   ├── CallStackView        호출 스택 도구창
    │   │   ├── RegistersView        레지스터 도구창
    │   │   └── EvaluateDialog       식 평가 다이얼로그
    │   └── Compiler/
    │       └── CompilerMessagesView  컴파일러 메시지 도구창
    ├── Resources/
    │   └── PascalSyntax.xshd       Pascal 구문 강조 정의 (EmbeddedResource)
    └── Themes/
        └── FpcTheme.xaml           전역 WPF 스타일

```

---

## 디버깅 아키텍처

```
FPC 컴파일러 (-g -gw3)
    ↓ DWARF 디버그 정보 포함 실행 파일
GDB (--interpreter=mi2)
    ↓ GDB/MI 프로토콜 (stdout/stdin)
GdbMiProcess          — 프로세스 I/O 관리, 토큰 기반 비동기 명령
    ↓
GdbMiParser           — MI 출력 파싱 → GdbMiLine (ResultRecord / AsyncRecord)
    ↓
DebugController       — 세션 관리, 브레이크포인트/와치/레지스터/호출스택 수집
    ↓ 이벤트 (Stopped, SessionStarted, WatchesUpdated …)
MainViewModel / Tool Window ViewModels
    ↓ INotifyPropertyChanged / ObservableCollection
WPF Views             — SourceEditorView 노란 줄 강조, BreakpointMargin 빨간 점
```

원본 대응표:

| Org/ Pascal 파일 | C# 클래스 |
|-----------------|-----------|
| `gdbmiwrap.pas` | `GdbMiProcess`, `GdbMiParser`, `GdbMiTypes` |
| `fpdebug.pas`   | `DebugController` |
| `fpide.pas`     | `MainViewModel` |
| `weditor.pas`   | `EditorViewModel`, `SourceEditorView` |
| `fpcompil.pas`  | `FpcCompiler` |

---

## 빌드 방법

```bash
# VS 2022 Pro에서 열기
uFPCEditor.sln

# 구성: Debug | x64  또는  Release | x64
# 빌드 단축키: Ctrl+Shift+B
```

또는 dotnet CLI:

```bash
dotnet build uFPCEditor/uFPCEditor.csproj -c Debug -p:Platform=x64
```

출력: `uFPCEditor/bin/x64/Debug/net9.0-windows/uFPCEditor.exe`

---

## 리비전 히스토리

### v0.1.0 — 초기 설계 및 골격 구현

- **ORG 분석**: `Org/` 디렉터리의 Pascal FPC IDE 소스 학습
  - 디버깅 아키텍처: GDB/MI ↔ TDebugController ↔ UI
  - 컴파일러 래퍼: `TIDEApp.DoCompile / DoMake / DoBuild` 흐름
- **프로젝트 생성**: `uFPCEditor.sln` / `uFPCEditor.csproj` (.NET 9.0-windows, x64)
- **NuGet 설정**:
  - `avalonedit 6.3.0.90` — NuGet.org 미게시 → 로컬 캐시 복사 + `nuget.config`
  - `Dirkster.AvalonDock 4.73.0` (구 `AvalonDock` 2.x 대체)
  - `CommunityToolkit.Mvvm 8.3.2`, `Microsoft.Extensions.DependencyInjection 9.0.0`
  - `AssetTargetFallback` 추가 (net9.0 ↔ net7.0-windows 패키지 호환)
- **핵심 파일 구현**:
  - `GdbMiTypes.cs` / `GdbMiParser.cs` / `GdbMiProcess.cs` — GDB/MI 완전 구현
  - `DebugController.cs` — 세션·브레이크포인트·와치·레지스터·호출스택
  - `FpcCompiler.cs` — 비동기 컴파일, `GeneratedRegex` 출력 파싱
  - `MainViewModel.cs` — 모든 메뉴/툴바 명령
  - `EditorViewModel.cs` — AvalonEdit TextDocument 연동
  - `SourceEditorView.xaml.cs` — BreakpointMargin, DebugLineColorizer
  - `PascalSyntax.xshd` — Pascal 구문 강조 (11 색상 범주)

---

### v0.2.0 — x64 빌드 수정

- **문제**: VS 2022에서 `Debug|x64` 구성이 없어 빌드 불가
- **수정**:
  - `uFPCEditor.sln` — `Debug|x64`, `Release|x64` 플랫폼 구성 추가
  - `uFPCEditor.csproj` — `<PlatformTarget>` 조건 추가

---

### v0.3.0 — 컴파일 오류 수정

| 오류 | 원인 | 수정 |
|------|------|------|
| CS0102 `PrimaryFile` 중복 | CommunityToolkit 소스 생성자 충돌 | `PrimaryFile()` → `BrowsePrimaryFile()` 로 메서드 이름 변경 |
| CS0103 `Path` 미발견 | `using System.IO;` 누락 | `PascalHighlightingLoader.cs`에 추가 |
| CS7064 `fpc.ico` 없음 | 아이콘 파일 미생성 | `csproj`·`MainWindow.xaml`에서 아이콘 참조 주석 처리 |
| CS0067 `ProgressChanged` 미사용 경고 | 향후 사용 예정 이벤트 | `#pragma warning disable CS0067` 처리 |

---

### v0.4.0 — 런타임 크래시 수정 (ServiceLocator)

- **증상**: 실행 시 즉시 `XamlParseException: 이름이 'ServiceLocator'인 리소스를 찾을 수 없습니다`
- **원인**: `MainWindow.xaml`이 `{StaticResource ServiceLocator}`를 사용하나 `App.xaml`에 미등록
- **수정**:
  - `ServiceLocator.cs` 신규 작성 (DI 컨테이너에서 ViewModel 지연 취득)
  - `App.xaml` — `xmlns:local="clr-namespace:uFPCEditor"` 추가
  - `App.xaml` — `<local:ServiceLocator x:Key="ServiceLocator"/>` 리소스 등록

---

### v0.5.0 — 한글 깨짐 수정

- **증상**: 한국어 Windows에서 파스칼 소스 파일 로드 시 한글 깨짐, FPC 컴파일러 메시지 한글 깨짐
- **원인**: .NET 6+ 기본 인코딩이 UTF-8이지만 FPC/GDB는 시스템 OEM 코드페이지(CP949)로 출력
- **수정**:

  | 파일 | 변경 내용 |
  |------|----------|
  | `App.xaml.cs` | `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` — 앱 시작 시 비 Unicode 코드페이지 전체 활성화 |
  | `EditorViewModel.cs` | `LoadFile`: BOM 자동 감지 + 없으면 시스템 ANSI 코드페이지(CP949) fallback 사용 |
  | `EditorViewModel.cs` | `SaveFile`: UTF-8 BOM 포함 저장 |
  | `FpcCompiler.cs` | `StandardOutputEncoding` / `StandardErrorEncoding` → `OEMCodePage` |
  | `GdbMiProcess.cs` | GDB 출력 인코딩 → `OEMCodePage` |

---

### v0.6.0 — 구문 강조 오류 수정 (XSHD)

- **증상**: .pas 파일 열기 시 `InvalidOperationException: A highlighting rule matched 0 characters` 오류 다이얼로그 다수 발생
- **원인 1**: 문자열 Span 내부의 `''` 이스케이프를 `<Span><Begin>''</Begin><End/></Span>`으로 구현 → 빈 End 패턴이 0문자 매칭
- **원인 2**: `#\$[0-9A-Fa-f]+` — AvalonEdit 6.x에서 `\$`가 end-of-line 앵커로 해석됨
- **수정** (`PascalSyntax.xshd`):

  | 항목 | 수정 전 | 수정 후 |
  |------|---------|---------|
  | `''` 이스케이프 | `<Span><End/></Span>` | `<Rule color="String">''</Rule>` (2문자 소비) |
  | 16진 문자 리터럴 | `#\$[0-9A-Fa-f]+` | `#[$][0-9A-Fa-f]+` |
  | 16진 숫자 | `\$[0-9A-Fa-f]+` | `[$][0-9A-Fa-f]+` |
  | 실수/지수 숫자 | `\b\d+(\.\d+)?([eE][+-]?\d+)?\b` | `\b[0-9]+([.][0-9]+)?([eE][+-]?[0-9]+)?\b` |

- **예외 핸들러 추가** (`App.xaml.cs`):
  - `DispatcherUnhandledException` → 오류 다이얼로그 + `crash.log` 기록
  - `AppDomain.UnhandledException` → `crash.log` 기록

---

### v0.7.0 — XSHD Verbose 모드 `[#]` 수정 + FPC 경로 기본값 설정

- **XSHD Verbose 모드 `#` 문제 최종 수정** (`PascalSyntax.xshd`):
  - **원인**: AvalonEdit 6.3.0.90은 Rule 정규식을 `RegexOptions.IgnorePatternWhitespace`(Verbose 모드)로 컴파일한다.  
    Verbose 모드에서 `#`는 줄 주석 시작 문자 → 이후 전체 무시 → 빈 패턴 → 0문자 매칭 오류.  
    `#[0-9]+`, `#[$][0-9A-Fa-f]+` 등 모든 `#` 시작 패턴이 영향을 받았다.
  - **수정**: 문자 리터럴 규칙에서 `#` → `[#]` 문자 클래스로 대체  
    (`[...]` 안에서는 `#`이 주석으로 해석되지 않음)

  | 항목 | 수정 전 | 수정 후 |
  |------|---------|---------|
  | 16진 문자 리터럴 | `#[$][0-9A-Fa-f]+` | `[#][$][0-9A-Fa-f]+` |
  | 10진 문자 리터럴 | `#[0-9]+` | `[#][0-9]+` |

- **FPC 컴파일러 경로 기본값 설정** (`App.xaml.cs`):
  - `Services.BuildServiceProvider()` 직후 `FpcCompiler.FpcPath`를 실제 경로로 초기화
  - `C:\FPC\3.2.2\bin\i386-win32\fpc.exe`

  ```csharp
  var compiler = Services.GetRequiredService<FpcCompiler>();
  compiler.FpcPath = @"C:\FPC\3.2.2\bin\i386-win32\fpc.exe";
  ```

  > **참고**: FPC for Windows는 64비트 패키지를 설치해도 컴파일러 드라이버 `fpc.exe`는  
  > 32비트 실행 파일로 `i386-win32` 디렉터리에 위치한다.  
  > 64비트 코드를 생성하려면 `-P x86_64` 옵션 또는 `ppcrossx64.exe`를 사용한다.

---

---

### v0.8.0 — GDB/MI 디버거 안정화 (F8 Step Over 수정)

#### 버그 수정 (5개)

| # | 증상 | 원인 | 수정 파일 |
|---|------|------|-----------|
| 1 | F8 후 응답 없음, 5초 타임아웃 반복 | `SendCommandAsync`에서 TCS 등록 전에 명령을 전송 → 응답이 먼저 도착하면 TCS 미존재로 유실 | `DebugController.cs` |
| 2 | 레지스터 뷰 비어 있음 | `ParseList`가 `{`로 시작하는 아이템을 key=value로 오해석 → `register-values` 전체 누락 | `GdbMiParser.cs` |
| 3 | 프로그램 실행 중 F8 → 조용히 무시 | `IsProgramRunning` 가드 부재 → 실행 중 GDB에 `-exec-next` 전송 → GDB 오류 무시 | `MainViewModel.cs` |
| 4 | 브레이크포인트 히트 후 에디터 이동 안 됨 | GDB가 `file="test.pas"` (상대경로) 반환 → `NavigateToSource`가 절대경로를 찾지 못함 | `DebugController.cs` |
| 5 | 로그 파일 내용 오염 | `LogSend`(UI 스레드)와 `LogRecv`(ReadLoop 스레드) 동시 접근 — lock 미적용 | `GdbMiProcess.cs` |

#### 세부 변경

**`GdbMiProcess.cs`**
- `ProcessStartInfo`에 `StandardInputEncoding = OEMCodePage` 추가 (stdin/stdout/stderr 인코딩 통일)
- stdin `NewLine = "\n"` 고정 — GDB/MI 파서는 CRLF(`\r\n`)를 명령 끝에 `\r`이 붙은 것으로 인식해 오작동
- `AllocateToken()` / `SendCommandRaw(token, cmd)` 메서드 추가 — 레이스 컨디션 방지용
- `ReadStderrLoop()` 스레드 추가 — GDB stderr를 `!!! STDERR:` 접두사로 로그
- `WriteLog(string)` 공개 메서드 추가 — 외부(DebugController)에서 `[IDE]` 접두사로 기록
- 모든 로그 메서드에 `lock (_logLock)` 적용

**`DebugController.cs`**
- `SendCommandAsync`: TCS를 `_pendingCommands`에 먼저 등록 후 명령 전송 (레이스 컨디션 수정)
- `ResolveInfoPaths(DebugStopInfo)` 추가 — `frame.file`(상대경로)을 exe 디렉토리 기준 절대경로로 변환
- 모든 step 메서드(`StepOverAsync` 등)에 `IsProgramRunning` 가드 + 로그 추가
- `OnAsyncReceived`에 stopped/running 이벤트 상세 로그 추가

**`GdbMiParser.cs`**
- `ParseList` 수정: `{`, `[`, `"` 로 시작하는 리스트 아이템은 key= 없이 값으로 직접 파싱  
  (수정 전: `register-values=[{number="0",...}]` 파싱 실패)

**`GdbMiTypes.cs`**
- `DebugStopInfo`: `sealed class` → `sealed record` (`with` 표현식 사용 위해)

**`MainViewModel.cs`**
- `StepOver` 등 step 명령: `IsDebugging` + `IsProgramRunning` 이중 가드 + 상태바 메시지 표시

---

### v0.9.0 — x64 컴파일·디버깅 환경 구성

#### 배경
사용자가 FPC x64 크로스 컴파일 패키지를 설치. 기존 환경(32비트 GDB)으로는 x64 실행 파일 디버깅 불가.

#### 변경

**`GdbMiProcess.cs`** (인코딩/줄끝 수정)
- `StandardInputEncoding = OEMCodePage` 추가 (이전 버전에서 UTF-8 기본값이 사용되던 문제 수정)
- stdin `StreamWriter`를 `NewLine = "\n"`, `AutoFlush = true`로 명시 생성

**`App.xaml.cs`** (x64 타겟 + GDB 경로 자동 탐지)
- FPC 컴파일 타겟: `compiler.TargetCpu = "x86_64"`, `compiler.TargetOs = "win64"` 추가  
  → `ppcrossx64.exe` + `units/x86_64-win64` 사용
- `FindGdb()` 메서드 추가 — 64비트 GDB를 우선순위대로 자동 탐색:
  1. `C:\msys64\mingw64\bin\gdb.exe` (MSYS2, winget 기본 경로)
  2. `C:\msys2\mingw64\bin\gdb.exe`
  3. `C:\TDM-GCC-64\bin\gdb.exe`
  4. `C:\FPC\3.2.2\bin\i386-win32\gdb.exe` (32비트 fallback)

#### 외부 도구 설치
- **MSYS2** (`winget install msys2.msys2`) — `C:\msys64` 설치
- **GDB 16.3 x86-64** (`mingw-w64-x86_64-gdb` 패키지) — `C:\msys64\mingw64\bin\gdb.exe`
- FPC 번들 GDB 7.2(i386) → 더 이상 사용하지 않음

#### 확인된 환경

| 항목 | 값 |
|---|---|
| FPC 컴파일러 | `C:\FPC\3.2.2\bin\i386-win32\fpc.exe` |
| FPC 타겟 | `x86_64-win64` |
| GDB | `C:\msys64\mingw64\bin\gdb.exe` (v16.3, 64비트) |

---

### v0.10.0 — GDB FPC Pascal 지원 개선 (커스텀 GDB 빌드)

#### 배경
uFPC는 IEC 61131-3 Structured Text 대응 FPC 축소판(Windows x64 전용).  
MSYS2 표준 GDB 16.3은 FPC 고유 타입(`AnsiString`, `ShortString`, 동적 배열 등)을  
포인터 주소로만 표시 → 디버깅 시 변수값 확인 불가.  
GDB 소스를 수정하여 FPC 타입을 올바르게 출력하도록 개선.

#### GDB 소스 수정 (`C:\gdb-build\gdb-16.3\gdb\`)

**`p-lang.h`**
- `fpc_is_ansistring_type(struct type *)` 함수 선언 추가

**`p-lang.c`**
- `fpc_is_ansistring_type()` 구현 추가  
  FPC가 DWARF에 방출하는 타입명(`ANSISTRING`, `RAWBYTESTRING`, `UTF8STRING` — 대소문자 무관)으로  
  포인터 타입을 식별. FPC 버전별 대소문자 차이를 `strcasecmp`로 흡수.

**`p-valprint.c`**
- `fpc_print_ansistring()` 정적 헬퍼 추가  
  FPC AnsiString 메모리 레이아웃 기준으로 문자열 내용 출력:
  ```
  x64: Len(int64) at ptr-8, Data at ptr+0
  x32: Len(int32) at ptr-4, Data at ptr+0
  ```
  `gdbarch_ptr_bit()`로 아키텍처 자동 감지.  
  길이 음수/64MiB 초과 시 `<invalid AnsiString>` 출력(안전장치).
- `value_print_inner()` → `TYPE_CODE_PTR` 케이스에  
  `fpc_is_ansistring_type()` 검사 후 `fpc_print_ansistring()` 호출 삽입

#### FPC 타입별 지원 현황

| 타입 | 수정 전 | 수정 후 |
|---|---|---|
| `AnsiString` | `0x7fff12345678` (주소만) | `'Hello, World!'` |
| `RawByteString` | 주소만 | 문자열 내용 |
| `UTF8String` | 주소만 | 문자열 내용 |
| `ShortString` | 기존 GDB가 이미 처리 (`length`+`st` 구조) | 변경 없음 |
| 동적 배열 | 주소만 | 향후 개선 예정 |

#### 빌드 환경 (Windows x64 전용 슬림 빌드)

```
./configure
  --host=x86_64-w64-mingw32
  --target=x86_64-w64-mingw32
  --prefix=C:/gdb-custom
  --enable-targets=x86_64-w64-mingw32
  --disable-tui          # 터미널 UI 불필요 (GDB/MI 전용)
  --disable-nls          # 영어 메시지 고정 → GDB/MI 파서 안정화
  --disable-sim          # CPU 시뮬레이터 제거
  --disable-gdbserver    # gdbserver 제거 (로컬 전용)
  --disable-gdbtk        # GUI 프론트엔드 제거
  --without-guile        # Guile 스크립팅 제거
  --with-python=no       # Python 제거 (슬림화)
  --disable-werror
```

- 빌드 도구: MSYS2 MinGW-w64 (GCC, make)
- 소스: `C:\gdb-build\gdb-16.3\`
- 설치 경로: `C:\gdb-custom\bin\gdb.exe`

#### App.xaml.cs 업데이트 예정
`FindGdb()` 우선순위에 `C:\gdb-custom\bin\gdb.exe` 추가 (빌드 완료 후)

*마지막 업데이트: 2026-04-17*
