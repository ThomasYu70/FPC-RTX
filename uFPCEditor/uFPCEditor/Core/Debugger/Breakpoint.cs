using CommunityToolkit.Mvvm.ComponentModel;

namespace uFPCEditor.Core.Debugger;

// ─────────────────────────────────────────────────────────────────────────────
// 브레이크포인트 모델
// 원본 참조: Org/packages/ide/fpdebug.pas  (TBreakpoint)
// ─────────────────────────────────────────────────────────────────────────────

public enum BreakpointType
{
    FileLine,   // bt_file_line — 소스 파일:라인
    Function,   // bt_function  — 함수 이름
    Address,    // bt_address   — 절대 주소
    Watch,      // bt_watch     — 쓰기 감시
    ReadWatch,  // bt_rwatch    — 읽기 감시
    AnyWatch    // bt_awatch    — 읽기/쓰기 감시
}

public enum BreakpointState
{
    Enabled,
    Disabled,
    Deleted,
    PendingDelete   // bs_delete_after
}

public partial class Breakpoint : ObservableObject
{
    // ── 식별 ─────────────────────────────────────────────────────────────────

    /// <summary>IDE 내부 고유 ID</summary>
    public int Id { get; } = NextId();

    /// <summary>GDB가 할당한 브레이크포인트 번호 (-1 = 아직 미설정)</summary>
    [ObservableProperty] private int _gdbId = -1;

    // ── 위치 ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private BreakpointType _type = BreakpointType.FileLine;

    /// <summary>소스 파일 전체 경로</summary>
    [ObservableProperty] private string _fileName = string.Empty;

    /// <summary>1-based 라인 번호</summary>
    [ObservableProperty] private int _line;

    /// <summary>함수 이름 또는 감시 표현식</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>절대 주소 (bt_address 전용)</summary>
    [ObservableProperty] private ulong _address;

    // ── 조건/동작 ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _condition    = string.Empty;
    [ObservableProperty] private int    _ignoreCount;
    [ObservableProperty] private string _commands     = string.Empty;

    // ── 상태 ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private BreakpointState _state = BreakpointState.Enabled;

    /// <summary>GDB에 실제 반영된 상태</summary>
    [ObservableProperty] private BreakpointState _gdbState = BreakpointState.Disabled;

    // ── 히트 카운트 (UI 표시용) ───────────────────────────────────────────────

    [ObservableProperty] private int _hitCount;

    // ── 표시 문자열 ───────────────────────────────────────────────────────────

    public string DisplayLocation => Type switch
    {
        BreakpointType.FileLine   => $"{System.IO.Path.GetFileName(FileName)}:{Line}",
        BreakpointType.Function   => Name,
        BreakpointType.Address    => $"0x{Address:X8}",
        BreakpointType.Watch      => $"watch({Name})",
        BreakpointType.ReadWatch  => $"rwatch({Name})",
        BreakpointType.AnyWatch   => $"awatch({Name})",
        _                          => string.Empty
    };

    public bool IsEnabled => State == BreakpointState.Enabled;

    // ── 팩토리 ───────────────────────────────────────────────────────────────

    public static Breakpoint ForLine(string fileName, int line) => new()
    {
        Type     = BreakpointType.FileLine,
        FileName = fileName,
        Line     = line
    };

    public static Breakpoint ForFunction(string functionName) => new()
    {
        Type = BreakpointType.Function,
        Name = functionName
    };

    public static Breakpoint ForAddress(ulong address) => new()
    {
        Type    = BreakpointType.Address,
        Address = address
    };

    public static Breakpoint ForWatch(string expression, BreakpointType watchType = BreakpointType.Watch) => new()
    {
        Type = watchType,
        Name = expression
    };

    // ── GDB 위치 문자열 생성 ──────────────────────────────────────────────────

    public string ToGdbLocation() => Type switch
    {
        // GDB는 경로 구분자로 '/' 를 사용한다. Windows 백슬래시는 이스케이프로 해석되므로 반드시 변환.
        BreakpointType.FileLine  => $"\"{FileName.Replace('\\', '/')}\":{Line}",
        BreakpointType.Function  => Name,
        BreakpointType.Address   => $"*0x{Address:X}",
        _                         => Name
    };

    // ── ID 생성기 ─────────────────────────────────────────────────────────────

    private static int _nextId = 1;
    private static int NextId() => Interlocked.Increment(ref _nextId);
}
