using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 와치 패널 ViewModel
// 원본 참조: Org/packages/ide/fpdebug.pas  (TWatch, TWatchesWindow)
//           Org/packages/ide/fpevalw.pas   (Tevaluate_dialog)
// ─────────────────────────────────────────────────────────────────────────────

public partial class WatchesViewModel : ViewModelBase
{
    private readonly DebugController _debugger;

    public ObservableCollection<Watch> Watches => _debugger.Watches;

    [ObservableProperty] private Watch?  _selectedWatch;
    [ObservableProperty] private string  _newExpression = string.Empty;

    public WatchesViewModel(DebugController debugger)
    {
        _debugger = debugger;
        Title     = "Watches";
        _debugger.WatchesUpdated += (_, _) => OnPropertyChanged(nameof(Watches));
    }

    // ── 명령 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddWatch()
    {
        if (string.IsNullOrWhiteSpace(NewExpression)) return;
        _debugger.AddWatch(NewExpression.Trim());
        NewExpression = string.Empty;
    }

    [RelayCommand]
    private void RemoveWatch()
    {
        if (SelectedWatch == null) return;
        _debugger.RemoveWatch(SelectedWatch);
    }

    [RelayCommand]
    private void RemoveAll()
    {
        foreach (var w in Watches.ToList())
            _debugger.RemoveWatch(w);
    }

    [RelayCommand]
    private async Task RefreshWatches()
        => await _debugger.RefreshWatchesAsync();

    /// <summary>Evaluate 대화상자 열기 요청 (MainViewModel에서 처리)</summary>
    [RelayCommand]
    private void Evaluate()
        => EvaluateRequested?.Invoke(this, SelectedWatch?.Expression ?? NewExpression);

    public event EventHandler<string>? EvaluateRequested;
}
