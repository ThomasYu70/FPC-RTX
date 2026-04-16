using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using uFPCEditor.Core.Compiler;
using uFPCEditor.Core.Debugger;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 메인 창 ViewModel
// 원본 참조: Org/packages/ide/fpide.pas  (TIDEApp)
//
// 역할:
//   - 메뉴/툴바 명령 처리
//   - 에디터 탭 관리
//   - 컴파일러·디버거 조율
//   - 디버그 이벤트 → 에디터 위치 이동
// ─────────────────────────────────────────────────────────────────────────────

public partial class MainViewModel : ViewModelBase
{
    // ── 의존성 ───────────────────────────────────────────────────────────────

    private readonly FpcCompiler          _compiler;
    private readonly DebugController      _debugger;
    public           BreakpointsViewModel BreakpointsViewModel { get; }
    public           WatchesViewModel     WatchesViewModel     { get; }
    public           CallStackViewModel   CallStackViewModel   { get; }
    public           RegistersViewModel   RegistersViewModel   { get; }
    public           CompilerViewModel    CompilerViewModel    { get; }

    // ── 열린 문서 목록 (AvalonDock DocumentsSource) ───────────────────────────

    public ObservableCollection<EditorViewModel> OpenDocuments { get; } = new();

    [ObservableProperty] private EditorViewModel? _activeDocument;
    [ObservableProperty] private EditorViewModel? _activeEditor;

    // ── 상태 ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusText   = "Ready";
    [ObservableProperty] private string _debugState   = string.Empty;
    [ObservableProperty] private string _outputText   = string.Empty;
    [ObservableProperty] private string _primaryFile  = string.Empty;

    public ObservableCollection<string> RecentFiles { get; } = new();

    // ── 생성자 ───────────────────────────────────────────────────────────────

    public MainViewModel(
        FpcCompiler          compiler,
        DebugController      debugger,
        BreakpointsViewModel breakpointsVm,
        WatchesViewModel     watchesVm,
        CallStackViewModel   callStackVm,
        RegistersViewModel   registersVm,
        CompilerViewModel    compilerVm)
    {
        _compiler            = compiler;
        _debugger            = debugger;
        BreakpointsViewModel = breakpointsVm;
        WatchesViewModel     = watchesVm;
        CallStackViewModel   = callStackVm;
        RegistersViewModel   = registersVm;
        CompilerViewModel    = compilerVm;

        Title = "Free Pascal IDE";

        // 디버거 이벤트 구독
        _debugger.Stopped        += OnDebuggerStopped;
        _debugger.SessionStarted += (_, _) => DebugState = "Debugging";
        _debugger.SessionEnded   += (_, code) =>
        {
            DebugState = $"Exited ({code})";
            ClearEditorDebugMarkers();
        };
        _debugger.ConsoleOutput  += (_, text) => OutputText += text;

        // 컴파일러 출력 → Output 패널
        _compiler.MessageReceived += (_, msg) =>
        {
            if (msg.Kind == MessageKind.Progress)
                StatusText = msg.Text;
        };

        // 와치/콜스택 → 소스 이동 요청
        WatchesViewModel.EvaluateRequested            += (_, expr) => ShowEvaluateDialog(expr);
        CallStackViewModel.SourceNavigationRequested  += (_, frame) => NavigateToSource(frame.FileName, frame.Line);
        BreakpointsViewModel.ConditionEditRequested   += (_, bp) => ShowBreakpointConditionDialog(bp);
        CompilerViewModel.ErrorNavigationRequested    += (_, msg) => NavigateToSource(msg.FileName, msg.Line, msg.Column);
    }

    // ════════════════════════════════════════════ 파일 명령 ═══

    [RelayCommand]
    private void NewFile()
    {
        var vm = CreateEditorViewModel();
        vm.Title = "Untitled";
        OpenDocuments.Add(vm);
        ActiveDocument = vm;
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open Pascal File",
            Filter = "Pascal Files (*.pas;*.pp;*.inc)|*.pas;*.pp;*.inc|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        OpenFileAt(dlg.FileName);
    }

    [RelayCommand]
    private void OpenRecent(string path) => OpenFileAt(path);

    [RelayCommand]
    private void Save()
    {
        if (ActiveDocument == null) return;
        if (string.IsNullOrEmpty(ActiveDocument.FilePath))
            SaveAs();
        else
            ActiveDocument.SaveFile();
    }

    [RelayCommand]
    private void SaveAs()
    {
        if (ActiveDocument == null) return;
        var dlg = new SaveFileDialog
        {
            Title      = "Save Pascal File",
            Filter     = "Pascal Files (*.pas;*.pp)|*.pas;*.pp|All Files (*.*)|*.*",
            FileName   = ActiveDocument.FilePath
        };
        if (dlg.ShowDialog() != true) return;
        ActiveDocument.SaveFileAs(dlg.FileName);
        AddRecentFile(dlg.FileName);
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var doc in OpenDocuments.Where(d => d.IsModified))
            doc.SaveFile();
    }

    [RelayCommand]
    private void Close()
    {
        if (ActiveDocument == null) return;
        CloseDocument(ActiveDocument);
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    // ════════════════════════════════════════════ 검색 명령 ═══

    [RelayCommand] private void Find()        => FindRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void FindNext()    => FindNextRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void FindPrev()    => FindPrevRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void Replace()     => ReplaceRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void GotoLine()    => GotoLineRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void FindInFiles() => FindInFilesRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void FindSymbol()  => FindSymbolRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void FindProcedure() => FindProcedureRequested?.Invoke(this, EventArgs.Empty);

    // ════════════════════════════════════════════ 컴파일 명령 ═══

    [RelayCommand]
    private async Task Compile()
        => await RunCompileAsync(CompileMode.Compile);

    [RelayCommand]
    private async Task Build()
        => await RunCompileAsync(CompileMode.Build);

    [RelayCommand]
    private async Task Make()
        => await RunCompileAsync(CompileMode.Make);

    [RelayCommand]
    private async Task Run()
    {
        var result = await RunCompileAsync(CompileMode.Build);
        if (result?.Success == true && result.OutputFile != null)
            await StartDebuggingAsync(result.OutputFile);
    }

    [RelayCommand]
    private void Parameters() => ParametersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void BrowsePrimaryFile()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Set Primary File",
            Filter = "Pascal Files (*.pas;*.pp)|*.pas;*.pp"
        };
        if (dlg.ShowDialog() == true)
            PrimaryFile = dlg.FileName;
    }

    [RelayCommand]
    private void ClearPrimary() => PrimaryFile = string.Empty;

    // ════════════════════════════════════════════ 디버그 명령 ═══

    [RelayCommand]
    private async Task StepOver()
    {
        _debugger.Log($"F8 StepOver requested: IsDebugging={_debugger.IsDebugging} IsProgramRunning={_debugger.IsProgramRunning}");
        if (!_debugger.IsDebugging)
        {
            StatusText = "디버거가 실행 중이 아닙니다.";
            return;
        }
        if (_debugger.IsProgramRunning)
        {
            StatusText = "프로그램이 실행 중입니다. 중단 후 사용하세요.";
            return;
        }
        await _debugger.StepOverAsync();
    }

    [RelayCommand]
    private async Task StepInto()
    {
        if (!_debugger.IsDebugging || _debugger.IsProgramRunning) return;
        await _debugger.StepIntoAsync();
    }

    [RelayCommand]
    private async Task StepOut()
    {
        if (!_debugger.IsDebugging || _debugger.IsProgramRunning) return;
        await _debugger.StepOutAsync();
    }

    [RelayCommand]
    private async Task RunToCursor()
    {
        if (!_debugger.IsDebugging || _debugger.IsProgramRunning || ActiveDocument == null) return;
        await _debugger.RunToCursorAsync(ActiveDocument.FilePath, ActiveDocument.CaretLine);
    }

    [RelayCommand]
    private async Task ToggleBreakpoint()
    {
        if (ActiveDocument == null) return;
        await _debugger.ToggleBreakpointAsync(ActiveDocument.FilePath, ActiveDocument.CaretLine);
    }

    [RelayCommand]
    private void ShowBreakpoints() => ShowBreakpointsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AddWatch()
    {
        string expr = ActiveDocument?.GetWordAtCaret() ?? string.Empty;
        ShowEvaluateDialog(expr);
    }

    [RelayCommand]
    private void ShowWatches() => ShowWatchesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowCallStack() => ShowCallStackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowRegisters() => ShowRegistersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowDisassembly() => ShowDisassemblyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Evaluate()
    {
        string expr = ActiveDocument?.GetWordAtCaret() ?? string.Empty;
        ShowEvaluateDialog(expr);
    }

    [RelayCommand]
    private void ResetDebugger()
    {
        _debugger.ResetSession();
        ClearEditorDebugMarkers();
        DebugState = string.Empty;
        StatusText = "Debugger reset.";
    }

    [RelayCommand]
    private void OpenGdbConsole() => GdbConsoleRequested?.Invoke(this, EventArgs.Empty);

    // ════════════════════════════════════════════ 도구 명령 ═══

    [RelayCommand] private void CompilerOptions()  => CompilerOptionsRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void DebuggerOptions()  => DebuggerOptionsRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void Directories()      => DirectoriesRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void Calculator()       => CalculatorRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void AsciiTable()       => AsciiTableRequested?.Invoke(this, EventArgs.Empty);

    // ════════════════════════════════════════════ 창 명령 ═══

    [RelayCommand] private void TileHorizontal() => TileHorizontalRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void TileVertical()   => TileVerticalRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void Cascade()        => CascadeRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void WindowList()     => WindowListRequested?.Invoke(this, EventArgs.Empty);

    // ════════════════════════════════════════════ 도움말 명령 ═══

    [RelayCommand] private void HelpContents() => HelpContentsRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void HelpTopic()    => HelpTopicRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void About()
    {
        MessageBox.Show(
            "Free Pascal IDE\nC# / WPF Implementation\n\nBased on FPC IDE (© Berczi Gabor, Pierre Muller)",
            "About Free Pascal IDE",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ════════════════════════════════════════════ 내부 헬퍼 ═══

    private async Task<CompileResult?> RunCompileAsync(CompileMode mode)
    {
        string src = !string.IsNullOrEmpty(PrimaryFile)
                     ? PrimaryFile
                     : ActiveDocument?.FilePath ?? string.Empty;

        if (string.IsNullOrEmpty(src))
        {
            MessageBox.Show("No source file to compile.", "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        CompilerViewModel.ClearMessagesCommand.Execute(null);
        StatusText = "Compiling...";
        IsBusy     = true;

        try
        {
            var result = await _compiler.CompileAsync(src, mode);
            CompilerViewModel.SetCompileResult(result);
            StatusText = result.Success ? "Build succeeded." : "Build failed.";
            return result;
        }
        finally { IsBusy = false; }
    }

    private async Task StartDebuggingAsync(string execFile)
    {
        StatusText = "Starting debugger...";
        string? workDir = Path.GetDirectoryName(execFile);
        try
        {
            await _debugger.StartSessionAsync(execFile, workDir);
            await _debugger.RunAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"디버거 시작 실패: {ex.Message}";
            _debugger.Log($"StartDebuggingAsync EXCEPTION: {ex}");
        }
    }

    private void OnDebuggerStopped(object? sender, Core.Debugger.GdbMi.DebugStopInfo info)
    {
        DebugState = $"Stopped at {Path.GetFileName(info.FileName)}:{info.Line}";
        StatusText = DebugState;

        if (!string.IsNullOrEmpty(info.FileName) && info.Line > 0)
            NavigateToSource(info.FileName, info.Line);
    }

    public void NavigateToSource(string fileName, int line, int column = 0)
    {
        // GDB는 Windows에서도 '/' 를 사용하는 경우가 있으므로 경로 정규화
        string normFile = NormalizePath(fileName);

        var doc = OpenDocuments.FirstOrDefault(
            d => string.Equals(NormalizePath(d.FilePath), normFile, StringComparison.OrdinalIgnoreCase))
            ?? OpenFileAt(normFile);

        if (doc == null) return;

        ActiveDocument = doc;
        doc.CaretLine   = line;
        doc.CaretColumn = Math.Max(1, column);
        doc.DebugLine   = line;

        SourceNavigationRequested?.Invoke(this, new SourceNavigationArgs(normFile, line, column));
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // 슬래시 통일 → GetFullPath로 절대 경로화 (존재하지 않아도 OK)
        try { return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)); }
        catch { return path.Replace('/', Path.DirectorySeparatorChar); }
    }

    private void ClearEditorDebugMarkers()
    {
        foreach (var doc in OpenDocuments)
            doc.DebugLine = 0;
    }

    private EditorViewModel? OpenFileAt(string path)
    {
        if (!File.Exists(path)) return null;

        var existing = OpenDocuments.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ActiveDocument = existing;
            return existing;
        }

        var vm = CreateEditorViewModel();
        vm.LoadFile(path);
        OpenDocuments.Add(vm);
        ActiveDocument = vm;
        AddRecentFile(path);
        return vm;
    }

    private EditorViewModel CreateEditorViewModel()
    {
        var vm = App.Services.GetRequiredService<EditorViewModel>();
        vm.CloseRequested += (s, _) =>
        {
            if (s is EditorViewModel editor)
                CloseDocument(editor);
        };
        return vm;
    }

    private void CloseDocument(EditorViewModel vm)
    {
        if (vm.IsModified)
        {
            var result = MessageBox.Show(
                $"Save changes to '{vm.Title}'?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)    vm.SaveFile();
        }
        OpenDocuments.Remove(vm);
    }

    private void AddRecentFile(string path)
    {
        if (RecentFiles.Contains(path)) RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 9)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    private void ShowEvaluateDialog(string initialExpression)
        => EvaluateRequested?.Invoke(this, initialExpression);

    private void ShowBreakpointConditionDialog(Core.Debugger.Breakpoint bp)
        => BreakpointConditionRequested?.Invoke(this, bp);

    public void OnShutdown()
    {
        _debugger.StopSession();
        _debugger.Dispose();
    }

    // ════════════════════════════════════════════ UI 요청 이벤트 ═══
    // View(코드-비하인드)가 구독하여 Dialog/Panel 조작

    public event EventHandler?                     FindRequested;
    public event EventHandler?                     FindNextRequested;
    public event EventHandler?                     FindPrevRequested;
    public event EventHandler?                     ReplaceRequested;
    public event EventHandler?                     GotoLineRequested;
    public event EventHandler?                     FindInFilesRequested;
    public event EventHandler?                     FindSymbolRequested;
    public event EventHandler?                     FindProcedureRequested;
    public event EventHandler?                     ParametersRequested;
    public event EventHandler?                     ShowBreakpointsRequested;
    public event EventHandler?                     ShowWatchesRequested;
    public event EventHandler?                     ShowCallStackRequested;
    public event EventHandler?                     ShowRegistersRequested;
    public event EventHandler?                     ShowDisassemblyRequested;
    public event EventHandler?                     GdbConsoleRequested;
    public event EventHandler<string>?             EvaluateRequested;
    public event EventHandler<Core.Debugger.Breakpoint>? BreakpointConditionRequested;
    public event EventHandler?                     CompilerOptionsRequested;
    public event EventHandler?                     DebuggerOptionsRequested;
    public event EventHandler?                     DirectoriesRequested;
    public event EventHandler?                     CalculatorRequested;
    public event EventHandler?                     AsciiTableRequested;
    public event EventHandler?                     TileHorizontalRequested;
    public event EventHandler?                     TileVerticalRequested;
    public event EventHandler?                     CascadeRequested;
    public event EventHandler?                     WindowListRequested;
    public event EventHandler?                     HelpContentsRequested;
    public event EventHandler?                     HelpTopicRequested;
    public event EventHandler<SourceNavigationArgs>? SourceNavigationRequested;
}

public sealed record SourceNavigationArgs(string FileName, int Line, int Column);
