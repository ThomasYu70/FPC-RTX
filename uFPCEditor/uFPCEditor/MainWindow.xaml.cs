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

        // Debug shortcuts
        InputBindings.Add(new KeyBinding(vm.StepOverCommand,         Key.F8,  ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.StepIntoCommand,         Key.F7,  ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.StepOutCommand,          Key.F8,  ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.RunToCursorCommand,      Key.F4,  ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.ToggleBreakpointCommand, Key.F5,  ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.EvaluateCommand,         Key.F4,  ModifierKeys.Control));

        // Compiler shortcuts
        InputBindings.Add(new KeyBinding(vm.CompileCommand, Key.F9, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(vm.BuildCommand,   Key.F9, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.RunCommand,     Key.F9, ModifierKeys.Control));

        // Editor shortcuts
        InputBindings.Add(new KeyBinding(vm.FindCommand,        Key.F,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.FindNextCommand,    Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.FindPrevCommand,    Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.ReplaceCommand,     Key.H,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.GotoLineCommand,    Key.G,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.AddWatchCommand,    Key.F7, ModifierKeys.Control));

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
            case Key.F8 when mod == ModifierKeys.None:
                if (vm.StepOverCommand.CanExecute(null)) vm.StepOverCommand.Execute(null);
                break;
            case Key.F7 when mod == ModifierKeys.None:
                if (vm.StepIntoCommand.CanExecute(null)) vm.StepIntoCommand.Execute(null);
                break;
            case Key.F8 when mod == ModifierKeys.Shift:
                if (vm.StepOutCommand.CanExecute(null)) vm.StepOutCommand.Execute(null);
                break;
            case Key.F4 when mod == ModifierKeys.None:
                if (vm.RunToCursorCommand.CanExecute(null)) vm.RunToCursorCommand.Execute(null);
                break;
            case Key.F5 when mod == ModifierKeys.None:
                if (vm.ToggleBreakpointCommand.CanExecute(null)) vm.ToggleBreakpointCommand.Execute(null);
                break;
            case Key.F9 when mod == ModifierKeys.Control:
                if (vm.RunCommand.CanExecute(null)) vm.RunCommand.Execute(null);
                break;
            case Key.F9 when mod == ModifierKeys.None:
                if (vm.BuildCommand.CanExecute(null)) vm.BuildCommand.Execute(null);
                break;
            case Key.F9 when mod == ModifierKeys.Alt:
                if (vm.CompileCommand.CanExecute(null)) vm.CompileCommand.Execute(null);
                break;
            case Key.F2 when mod == ModifierKeys.None:
                if (vm.ResetDebuggerCommand.CanExecute(null)) vm.ResetDebuggerCommand.Execute(null);
                break;
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
