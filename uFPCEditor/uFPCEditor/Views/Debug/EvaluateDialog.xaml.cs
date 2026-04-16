using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.Views.Debug;

// ─────────────────────────────────────────────────────────────────────────────
// Evaluate 대화상자
// 원본 참조: Org/packages/ide/fpevalw.pas  (Tevaluate_dialog)
// ─────────────────────────────────────────────────────────────────────────────

public partial class EvaluateDialog : Window
{
    public EvaluateDialog(DebugController debugger, string initialExpression)
    {
        InitializeComponent();
        DataContext = new EvaluateViewModel(debugger, initialExpression, this);
        ExpressionBox.Focus();
        ExpressionBox.SelectAll();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

// ── ViewModel ────────────────────────────────────────────────────────────────

public partial class EvaluateViewModel : ObservableObject
{
    private readonly DebugController _debugger;
    private readonly Window          _owner;

    [ObservableProperty] private string _expression  = string.Empty;
    [ObservableProperty] private string _resultText  = string.Empty;

    public EvaluateViewModel(DebugController debugger, string initialExpression, Window owner)
    {
        _debugger   = debugger;
        _owner      = owner;
        Expression  = initialExpression;
    }

    [RelayCommand]
    private async Task Evaluate()
    {
        if (string.IsNullOrWhiteSpace(Expression)) return;
        ResultText = await _debugger.EvaluateExpressionAsync(Expression);
    }

    [RelayCommand]
    private void AddWatch()
    {
        if (string.IsNullOrWhiteSpace(Expression)) return;
        _debugger.AddWatch(Expression.Trim());
    }
}
