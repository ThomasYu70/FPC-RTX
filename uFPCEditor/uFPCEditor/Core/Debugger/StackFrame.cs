namespace uFPCEditor.Core.Debugger;

// ─────────────────────────────────────────────────────────────────────────────
// 콜 스택 프레임
// 원본 참조: Org/packages/ide/fpdebug.pas  (TFrameEntry)
//           Org/packages/ide/gdbmiwrap.pas  (frame tuple)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StackFrame
{
    /// <summary>스택 레벨 (0 = 현재 프레임)</summary>
    public int Level { get; init; }

    /// <summary>함수/프로시저 이름</summary>
    public string Function { get; init; } = string.Empty;

    /// <summary>소스 파일 전체 경로</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>1-based 라인 번호</summary>
    public int Line { get; init; }

    /// <summary>프레임 주소</summary>
    public ulong Address { get; init; }

    /// <summary>인자 목록 (표시용 문자열)</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>소스 파일 이름만 (경로 없이)</summary>
    public string ShortFileName => System.IO.Path.GetFileName(FileName);

    public override string ToString()
        => $"#{Level}  {Function} at {ShortFileName}:{Line}";
}

// ─────────────────────────────────────────────────────────────────────────────
// 레지스터
// 원본 참조: Org/packages/ide/fpregs.pas  (TIntRegs, TRegistersView)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class Register
{
    public int    Number { get; init; }
    public string Name   { get; init; } = string.Empty;
    public string Value  { get; init; } = string.Empty;

    /// <summary>이전 값과 다른지 여부 (UI 색상 표시용)</summary>
    public bool Changed { get; init; }
}
