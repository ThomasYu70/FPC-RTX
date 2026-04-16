using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using uFPCEditor.Core.Compiler;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 컴파일러 메시지 패널 ViewModel
// 원본 참조: Org/packages/ide/fpcompil.pas  (TCompilerMessageWindow)
// ─────────────────────────────────────────────────────────────────────────────

public partial class CompilerViewModel : ViewModelBase
{
    private readonly FpcCompiler _compiler;

    public ObservableCollection<CompilerMessage> Messages { get; } = new();

    [ObservableProperty] private CompilerMessage? _selectedMessage;
    [ObservableProperty] private string           _statusText = string.Empty;
    [ObservableProperty] private int              _errorCount;
    [ObservableProperty] private int              _warningCount;

    public CompilerViewModel(FpcCompiler compiler)
    {
        _compiler = compiler;
        Title     = "Compiler Messages";

        _compiler.MessageReceived += (_, msg) =>
            App.Current.Dispatcher.Invoke(() => AddMessage(msg));
    }

    // ── 명령 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoToError()
    {
        if (SelectedMessage?.IsError == true || SelectedMessage?.IsWarning == true)
            ErrorNavigationRequested?.Invoke(this, SelectedMessage);
    }

    [RelayCommand]
    private void ClearMessages()
    {
        Messages.Clear();
        ErrorCount   = 0;
        WarningCount = 0;
        StatusText   = string.Empty;
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private void AddMessage(CompilerMessage msg)
    {
        Messages.Add(msg);
        if (msg.IsError)   ErrorCount++;
        if (msg.IsWarning) WarningCount++;
    }

    public void SetCompileResult(CompileResult result)
    {
        StatusText = result.Success
            ? $"Build succeeded — {result.WarningCount} warning(s)  [{result.Elapsed.TotalSeconds:F1}s]"
            : $"Build FAILED — {result.ErrorCount} error(s), {result.WarningCount} warning(s)";
        ErrorCount   = result.ErrorCount;
        WarningCount = result.WarningCount;
    }

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    public event EventHandler<CompilerMessage>? ErrorNavigationRequested;
}
