using CommunityToolkit.Mvvm.ComponentModel;

namespace uFPCEditor.Core.Debugger;

// ─────────────────────────────────────────────────────────────────────────────
// 와치(Watch) — 디버거가 실행 중단 시 자동으로 값을 갱신하는 표현식
// 원본 참조: Org/packages/ide/fpdebug.pas  (TWatch)
// ─────────────────────────────────────────────────────────────────────────────

public partial class Watch : ObservableObject
{
    // ── 식별 ─────────────────────────────────────────────────────────────────

    public int Id { get; } = NextId();

    /// <summary>GDB var-object 이름 (내부 핸들)</summary>
    [ObservableProperty] private string _varObjectName = string.Empty;

    // ── 표현식 ───────────────────────────────────────────────────────────────

    [ObservableProperty] private string _expression = string.Empty;

    // ── 값 ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _currentValue = string.Empty;
    [ObservableProperty] private string _lastValue    = string.Empty;
    [ObservableProperty] private string _type         = string.Empty;

    /// <summary>마지막 갱신 이후 값이 변경됨</summary>
    public bool IsChanged => CurrentValue != LastValue;

    // ── 상태 ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isValid  = true;
    [ObservableProperty] private bool _isActive = true;

    // ── 생성자 ───────────────────────────────────────────────────────────────

    public Watch() { }

    public Watch(string expression)
    {
        Expression = expression;
    }

    // ── 값 갱신 ──────────────────────────────────────────────────────────────

    public void UpdateValue(string newValue, string? typeName = null)
    {
        LastValue    = CurrentValue;
        CurrentValue = newValue;
        if (typeName != null) Type = typeName;
        OnPropertyChanged(nameof(IsChanged));
    }

    public void Invalidate()
    {
        LastValue    = CurrentValue;
        CurrentValue = "<error>";
        IsValid      = false;
        OnPropertyChanged(nameof(IsChanged));
    }

    // ── ID 생성기 ─────────────────────────────────────────────────────────────

    private static int _nextId = 1;
    private static int NextId() => Interlocked.Increment(ref _nextId);
}
