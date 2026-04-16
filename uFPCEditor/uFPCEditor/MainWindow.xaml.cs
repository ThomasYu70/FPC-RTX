using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using uFPCEditor.ViewModels;

namespace uFPCEditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        RegisterKeyBindings();
    }

    private void RegisterKeyBindings()
    {
        var vm = (MainViewModel)DataContext;

        // Debug step (VS 2022)
        InputBindings.Add(new KeyBinding(vm.StepOverCommand,         Key.F10, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.StepIntoCommand,         Key.F11, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.StepOutCommand,          Key.F11, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.RunToCursorCommand,      Key.F10, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.ToggleBreakpointCommand, Key.F9,  ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.EvaluateCommand,         Key.F9,  ModifierKeys.Shift));

        // Debug control (VS 2022)
        InputBindings.Add(new KeyBinding(vm.RunCommand,           Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.ResetDebuggerCommand, Key.F5, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.AddWatchCommand,      Key.W,  ModifierKeys.Control | ModifierKeys.Alt));

        // Build (VS 2022)
        InputBindings.Add(new KeyBinding(vm.CompileCommand, Key.B, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.BuildCommand,   Key.B, ModifierKeys.Control | ModifierKeys.Shift));

        // Editor shortcuts
        InputBindings.Add(new KeyBinding(vm.FindCommand,     Key.F,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.FindNextCommand, Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.FindPrevCommand, Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.ReplaceCommand,  Key.H,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.GotoLineCommand, Key.G,  ModifierKeys.Control));

        // Help
        InputBindings.Add(new KeyBinding(vm.HelpContentsCommand, Key.F1, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.HelpTopicCommand,    Key.F1, ModifierKeys.Control));
    }

    // PreviewKeyDown 은 터널 단계(Window→자식)로 전파되므로
    // AvalonEdit TextArea 가 키를 소비하기 전에 가로챌 수 있다.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (DataContext is not MainViewModel vm) return;

        var mod = e.KeyboardDevice.Modifiers;
        bool handled = true;

        switch (e.Key)
        {
            // Debug step (VS 2022)
            case Key.F10 when mod == ModifierKeys.None:
                if (vm.StepOverCommand.CanExecute(null)) vm.StepOverCommand.Execute(null);
                break;
            case Key.F11 when mod == ModifierKeys.None:
                if (vm.StepIntoCommand.CanExecute(null)) vm.StepIntoCommand.Execute(null);
                break;
            case Key.F11 when mod == ModifierKeys.Shift:
                if (vm.StepOutCommand.CanExecute(null)) vm.StepOutCommand.Execute(null);
                break;
            case Key.F10 when mod == ModifierKeys.Control:
                if (vm.RunToCursorCommand.CanExecute(null)) vm.RunToCursorCommand.Execute(null);
                break;
            case Key.F9 when mod == ModifierKeys.None:
                if (vm.ToggleBreakpointCommand.CanExecute(null)) vm.ToggleBreakpointCommand.Execute(null);
                break;
            case Key.F9 when mod == ModifierKeys.Shift:
                if (vm.EvaluateCommand.CanExecute(null)) vm.EvaluateCommand.Execute(null);
                break;
            // Debug control (VS 2022)
            case Key.F5 when mod == ModifierKeys.None:
                if (vm.RunCommand.CanExecute(null)) vm.RunCommand.Execute(null);
                break;
            case Key.F5 when mod == ModifierKeys.Shift:
                if (vm.ResetDebuggerCommand.CanExecute(null)) vm.ResetDebuggerCommand.Execute(null);
                break;
            // Help
            case Key.F1 when mod == ModifierKeys.None:
                if (vm.HelpContentsCommand.CanExecute(null)) vm.HelpContentsCommand.Execute(null);
                break;
            default:
                handled = false;
                break;
        }

        if (handled) e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.OnShutdown();
        base.OnClosed(e);
    }
}
