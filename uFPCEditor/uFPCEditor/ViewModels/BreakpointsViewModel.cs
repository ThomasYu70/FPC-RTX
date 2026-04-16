using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 브레이크포인트 패널 ViewModel
// 원본 참조: Org/packages/ide/fpdebug.pas  (TBreakpointCollection, TBreakpointsWindow)
// ─────────────────────────────────────────────────────────────────────────────

public partial class BreakpointsViewModel : ViewModelBase
{
    private readonly DebugController _debugger;

    public ObservableCollection<Breakpoint> Breakpoints => _debugger.Breakpoints;

    [ObservableProperty] private Breakpoint? _selectedBreakpoint;

    public BreakpointsViewModel(DebugController debugger)
    {
        _debugger = debugger;
        Title     = "Breakpoints";
    }

    // ── 명령 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RemoveBreakpoint()
    {
        if (SelectedBreakpoint == null) return;
        await _debugger.RemoveBreakpointAsync(SelectedBreakpoint);
    }

    [RelayCommand]
    private async Task EnableBreakpoint()
    {
        if (SelectedBreakpoint == null) return;
        await _debugger.EnableBreakpointAsync(SelectedBreakpoint);
    }

    [RelayCommand]
    private async Task DisableBreakpoint()
    {
        if (SelectedBreakpoint == null) return;
        await _debugger.DisableBreakpointAsync(SelectedBreakpoint);
    }

    [RelayCommand]
    private void RemoveAll()
    {
        var all = Breakpoints.ToList();
        foreach (var bp in all)
            _ = _debugger.RemoveBreakpointAsync(bp);
    }

    [RelayCommand]
    private void EditCondition()
    {
        if (SelectedBreakpoint == null) return;
        // EditBreakpointDialog 열기 → MainViewModel 이벤트로 처리
        ConditionEditRequested?.Invoke(this, SelectedBreakpoint);
    }

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    public event EventHandler<Breakpoint>? ConditionEditRequested;
}
