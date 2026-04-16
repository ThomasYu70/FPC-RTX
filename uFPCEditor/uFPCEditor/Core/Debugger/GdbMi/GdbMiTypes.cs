namespace uFPCEditor.Core.Debugger.GdbMi;

// ─────────────────────────────────────────────────────────────────────────────
// GDB/MI 프로토콜 타입 정의
// 원본 참조: Org/packages/ide/gdbmiwrap.pas  (TGDBMI_ResultRecord, etc.)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>GDB/MI 결과 레코드 종류 (done / running / error / exit)</summary>
public enum GdbResultClass
{
    Done,
    Running,
    Connected,
    Error,
    Exit
}

/// <summary>GDB/MI 비동기 레코드 종류 (*stopped / =thread-created / +download)</summary>
public enum GdbAsyncClass
{
    Exec,       // *  — 실행 상태 변경 (stopped, running)
    Status,     // +  — 진행 상태
    Notify      // =  — 이벤트 알림
}

/// <summary>프로그램이 멈춘 이유</summary>
public enum StopReason
{
    Unknown,
    BreakpointHit,
    EndSteppingRange,
    FunctionFinished,
    SignalReceived,
    ExitedNormally,
    ExitedWithError,
    Exited
}

// ─── Value 계층 ──────────────────────────────────────────────────────────────

public abstract class GdbValue { }

/// <summary>단순 문자열 값  (예: name="main")</summary>
public sealed class GdbStringValue : GdbValue
{
    public string Value { get; }
    public GdbStringValue(string value) => Value = value;
    public override string ToString() => Value;
}

/// <summary>중괄호 튜플  {key=val, ...}</summary>
public sealed class GdbTupleValue : GdbValue
{
    public Dictionary<string, GdbValue> Fields { get; } = new(StringComparer.Ordinal);

    public string? GetString(string key)
        => Fields.TryGetValue(key, out var v) && v is GdbStringValue s ? s.Value : null;

    public GdbTupleValue? GetTuple(string key)
        => Fields.TryGetValue(key, out var v) ? v as GdbTupleValue : null;

    public GdbListValue? GetList(string key)
        => Fields.TryGetValue(key, out var v) ? v as GdbListValue : null;
}

/// <summary>대괄호 리스트  [val, val, ...]  또는  [key=val, ...]</summary>
public sealed class GdbListValue : GdbValue
{
    public List<GdbValue> Items { get; } = new();
}

// ─── 레코드 ──────────────────────────────────────────────────────────────────

/// <summary>GDB/MI 결과 레코드  ^done,result-class,result...</summary>
public sealed class GdbResultRecord
{
    public string?        Token       { get; init; }
    public GdbResultClass ResultClass { get; init; }
    public GdbTupleValue  Results     { get; init; } = new();
}

/// <summary>GDB/MI 비동기 레코드  *stopped,reason="..."</summary>
public sealed class GdbAsyncRecord
{
    public string?       Token      { get; init; }
    public GdbAsyncClass AsyncClass { get; init; }
    public string        AsyncType  { get; init; } = string.Empty;  // "stopped", "thread-created" …
    public GdbTupleValue Results    { get; init; } = new();
}

/// <summary>GDB console / log / target 스트림 출력</summary>
public sealed class GdbStreamOutput
{
    public char   Channel { get; init; }   // '~' console  '@' target  '&' log
    public string Text    { get; init; } = string.Empty;
}

/// <summary>파싱된 GDB/MI 응답 한 줄</summary>
public sealed class GdbMiLine
{
    public GdbResultRecord?  ResultRecord  { get; init; }
    public GdbAsyncRecord?   AsyncRecord   { get; init; }
    public GdbStreamOutput?  StreamOutput  { get; init; }
    public bool              IsPrompt      { get; init; }   // (gdb)
}

// ─── 멈춤 이벤트 DTO ─────────────────────────────────────────────────────────

public sealed record DebugStopInfo
{
    public StopReason Reason         { get; init; }
    public int        BreakpointId   { get; init; }
    public string     FileName       { get; init; } = string.Empty;
    public int        Line           { get; init; }
    public string     Function       { get; init; } = string.Empty;
    public string?    SignalName     { get; init; }
    public int        ExitCode       { get; init; }
}
