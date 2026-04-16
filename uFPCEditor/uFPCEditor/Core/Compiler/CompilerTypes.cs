namespace uFPCEditor.Core.Compiler;

// ─────────────────────────────────────────────────────────────────────────────
// FPC 컴파일러 관련 타입 정의
// 원본 참조: Org/packages/ide/fpcompil.pas  (TCompileMode, TCompilerMessage)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>컴파일 모드</summary>
public enum CompileMode
{
    Compile,    // 현재 파일만 컴파일
    Make,       // 변경된 파일 컴파일
    Build,      // 전체 재빌드
    Run         // 빌드 후 실행
}

/// <summary>컴파일러 메시지 종류</summary>
public enum MessageKind
{
    Info,
    Hint,
    Note,
    Warning,
    Error,
    Fatal,
    Progress
}

/// <summary>FPC 컴파일러 출력 메시지 한 건</summary>
public sealed class CompilerMessage
{
    public MessageKind Kind       { get; init; }
    public string      FileName   { get; init; } = string.Empty;
    public int         Line       { get; init; }
    public int         Column     { get; init; }
    public string      Text       { get; init; } = string.Empty;

    /// <summary>UI 표시 텍스트</summary>
    public string DisplayText =>
        string.IsNullOrEmpty(FileName)
            ? Text
            : $"{System.IO.Path.GetFileName(FileName)}({Line},{Column}): {Text}";

    public bool IsError   => Kind is MessageKind.Error or MessageKind.Fatal;
    public bool IsWarning => Kind == MessageKind.Warning;
}

/// <summary>컴파일 결과 요약</summary>
public sealed class CompileResult
{
    public bool Success      { get; init; }
    public int  ErrorCount   { get; init; }
    public int  WarningCount { get; init; }
    public TimeSpan Elapsed  { get; init; }
    public string? OutputFile { get; init; }

    public IReadOnlyList<CompilerMessage> Messages { get; init; } = Array.Empty<CompilerMessage>();
}
