using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.ViewModels;

public partial class CallStackViewModel : ViewModelBase
{
    private readonly DebugController _debugger;

    public ObservableCollection<StackFrame> CallStack => _debugger.CallStack;

    [ObservableProperty] private StackFrame? _selectedFrame;

    public CallStackViewModel(DebugController debugger)
    {
        _debugger = debugger;
        Title     = "Call Stack";
        _debugger.CallStackUpdated += (_, _) => OnPropertyChanged(nameof(CallStack));
    }

    [RelayCommand]
    private void GoToSource()
    {
        if (SelectedFrame == null) return;
        SourceNavigationRequested?.Invoke(this, SelectedFrame);
    }

    /// <summary>에디터가 해당 소스 위치로 이동하도록 MainViewModel에 알림</summary>
    public event EventHandler<StackFrame>? SourceNavigationRequested;
}
