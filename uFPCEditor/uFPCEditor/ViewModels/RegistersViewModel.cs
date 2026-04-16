using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 레지스터 패널 ViewModel
// 원본 참조: Org/packages/ide/fpregs.pas  (TRegistersView, TIntRegs)
// ─────────────────────────────────────────────────────────────────────────────

public partial class RegistersViewModel : ViewModelBase
{
    private readonly DebugController _debugger;

    public ObservableCollection<Register> Registers => _debugger.Registers;

    public RegistersViewModel(DebugController debugger)
    {
        _debugger = debugger;
        Title     = "Registers";
        _debugger.RegistersUpdated += (_, _) => OnPropertyChanged(nameof(Registers));
    }

    [RelayCommand]
    private async Task Refresh()
        => await _debugger.RefreshRegistersAsync();
}
