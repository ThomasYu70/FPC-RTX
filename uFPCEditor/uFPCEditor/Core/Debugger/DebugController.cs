using System.Collections.ObjectModel;
using System.IO;
using uFPCEditor.Core.Debugger.GdbMi;

namespace uFPCEditor.Core.Debugger;

// ─────────────────────────────────────────────────────────────────────────────
// IDE 디버그 컨트롤러
// 원본 참조: Org/packages/ide/fpdebug.pas  (TDebugController)
//
// 역할:
//   - GdbMiProcess를 래핑하여 IDE 수준의 디버그 동작 제공
//   - 브레이크포인트/와치 컬렉션 관리
//   - 실행 중단 시 이벤트 발행 → IDE UI 갱신
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DebugController : IDisposable
{
    // ── 공개 이벤트 ──────────────────────────────────────────────────────────

    /// <summary>디버거가 소스 라인에서 멈춤 (에디터 위치 이동 트리거)</summary>
    public event EventHandler<DebugStopInfo>? Stopped;

    /// <summary>디버거 세션 시작</summary>
    public event EventHandler? SessionStarted;

    /// <summary>디버거 세션 종료</summary>
    public event EventHandler<int>? SessionEnded;

    /// <summary>GDB 콘솔 출력 수신</summary>
    public event EventHandler<string>? ConsoleOutput;

    /// <summary>와치 값 갱신 완료</summary>
    public event EventHandler? WatchesUpdated;

    /// <summary>레지스터 목록 갱신 완료</summary>
    public event EventHandler? RegistersUpdated;

    /// <summary>콜 스택 갱신 완료</summary>
    public event EventHandler? CallStackUpdated;

    // ── 상태 ─────────────────────────────────────────────────────────────────

    public bool IsDebugging   => _gdb.IsRunning;
    public bool IsProgramRunning { get; private set; }

    public string? ExecutableFile { get; private set; }
    public string  GdbPath        { get; set; } = "gdb";

    // ── 컬렉션 (UI 바인딩용 ObservableCollection) ────────────────────────────

    public ObservableCollection<Breakpoint> Breakpoints { get; } = new();
    public ObservableCollection<Watch>      Watches     { get; } = new();
    public ObservableCollection<StackFrame> CallStack   { get; } = new();
    public ObservableCollection<Register>   Registers   { get; } = new();

    // ── 내부 상태 ─────────────────────────────────────────────────────────────

    private readonly GdbMiProcess _gdb = new();
    private readonly Dictionary<int, TaskCompletionSource<GdbResultRecord>> _pendingCommands = new();
    private readonly List<string> _registerNames = new();
    private readonly List<Register> _prevRegisters = new();

    // ── 로그 헬퍼 ────────────────────────────────────────────────────────────

    /// <summary>GDB MI 로그 파일에 IDE 수준 메시지를 기록</summary>
    public void Log(string message) => _gdb.WriteLog($"[IDE] {message}");

    // ── 생성자 ───────────────────────────────────────────────────────────────

    public DebugController()
    {
        _gdb.ResultReceived += OnResultReceived;
        _gdb.AsyncReceived  += OnAsyncReceived;
        _gdb.StreamReceived += OnStreamReceived;
        _gdb.ProcessExited  += OnProcessExited;
    }

    // ── 세션 시작/종료 ────────────────────────────────────────────────────────

    /// <summary>디버그 세션 시작 — GDB 프로세스 기동 후 실행 파일 로드</summary>
    public async Task StartSessionAsync(string executableFile, string? workDir = null)
    {
        // 이미 실행 중인 세션이 있으면 먼저 종료
        if (_gdb.IsRunning)
        {
            StopSession();
            await Task.Delay(300);   // GDB 종료 대기
        }

        ExecutableFile = executableFile;
        _gdb.Start(GdbPath);
        Log($"START SESSION: exec={executableFile} workDir={workDir}");

        // GDB MI는 경로 구분자로 '/' 를 사용한다. Windows 백슬래시는 이스케이프로 해석됨.
        string gdbExec   = executableFile.Replace('\\', '/');
        string? gdbDir   = workDir?.Replace('\\', '/');
        string? srcDir   = Path.GetDirectoryName(executableFile)?.Replace('\\', '/');

        // 실행 파일 + 심볼 로드
        await SendCommandAsync($"-file-exec-and-symbols \"{gdbExec}\"");

        // 작업 디렉토리 설정
        if (!string.IsNullOrEmpty(gdbDir))
            await SendCommandAsync($"-environment-cd \"{gdbDir}\"");

        // 소스 디렉토리 설정 (실행 파일 디렉토리)
        if (!string.IsNullOrEmpty(srcDir))
            await SendCommandAsync($"-environment-directory \"{srcDir}\"");

        // 레지스터 이름 미리 가져오기
        await LoadRegisterNamesAsync();

        // 기존 브레이크포인트 GDB에 설정
        await InsertAllBreakpointsAsync();

        Log("SESSION READY");
        SessionStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopSession()
    {
        Log("STOP SESSION");
        _gdb.Stop();
        IsProgramRunning = false;
        ClearDebugState();
    }

    public void ResetSession()
    {
        StopSession();
        ClearBreakpointGdbIds();
    }

    // ── 실행 제어 ─────────────────────────────────────────────────────────────

    /// <summary>프로그램 실행 시작</summary>
    public Task RunAsync()
    {
        Log($"RUN: IsProgramRunning={IsProgramRunning}");
        IsProgramRunning = true;
        _gdb.ExecRun();
        return Task.CompletedTask;
    }

    /// <summary>브레이크포인트 이후 계속</summary>
    public Task ContinueAsync()
    {
        Log($"CONTINUE: IsProgramRunning={IsProgramRunning}");
        IsProgramRunning = true;
        _gdb.ExecContinue();
        return Task.CompletedTask;
    }

    /// <summary>Step Over (F8)</summary>
    public Task StepOverAsync()
    {
        Log($"STEP OVER: IsProgramRunning={IsProgramRunning}, IsDebugging={IsDebugging}");
        IsProgramRunning = true;   // exec-next 전에 세팅 → refresh 경쟁 방지
        _gdb.ExecNext();
        return Task.CompletedTask;
    }

    /// <summary>Step Into (F7)</summary>
    public Task StepIntoAsync()
    {
        Log($"STEP INTO: IsProgramRunning={IsProgramRunning}, IsDebugging={IsDebugging}");
        IsProgramRunning = true;
        _gdb.ExecStep();
        return Task.CompletedTask;
    }

    /// <summary>Step Out (Shift+F8)</summary>
    public Task StepOutAsync()
    {
        Log($"STEP OUT: IsProgramRunning={IsProgramRunning}, IsDebugging={IsDebugging}");
        IsProgramRunning = true;
        _gdb.ExecFinish();
        return Task.CompletedTask;
    }

    /// <summary>커서 위치까지 실행 (F4)</summary>
    public Task RunToCursorAsync(string file, int line)
    {
        Log($"RUN TO CURSOR: {file}:{line}");
        IsProgramRunning = true;
        _gdb.ExecUntil(file, line);
        return Task.CompletedTask;
    }

    public Task InterruptAsync() { _gdb.ExecInterrupt(); return Task.CompletedTask; }

    // ── 브레이크포인트 관리 ───────────────────────────────────────────────────

    public async Task<Breakpoint> AddBreakpointAsync(string fileName, int line)
    {
        var bp = Breakpoint.ForLine(fileName, line);
        Breakpoints.Add(bp);

        if (IsDebugging)
            await SyncBreakpointToGdbAsync(bp);

        return bp;
    }

    public async Task RemoveBreakpointAsync(Breakpoint bp)
    {
        if (bp.GdbId > 0 && IsDebugging)
        {
            _gdb.BreakDelete(bp.GdbId);
            await Task.Delay(50); // GDB 처리 대기
        }
        bp.State  = BreakpointState.Deleted;
        Breakpoints.Remove(bp);
    }

    public async Task ToggleBreakpointAsync(string fileName, int line)
    {
        var existing = Breakpoints.FirstOrDefault(
            b => string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase)
              && b.Line == line
              && b.State != BreakpointState.Deleted);

        if (existing != null)
            await RemoveBreakpointAsync(existing);
        else
            await AddBreakpointAsync(fileName, line);
    }

    public async Task EnableBreakpointAsync(Breakpoint bp)
    {
        bp.State = BreakpointState.Enabled;
        if (bp.GdbId > 0 && IsDebugging)
        {
            _gdb.BreakEnable(bp.GdbId);
            await Task.Delay(50);
        }
    }

    public async Task DisableBreakpointAsync(Breakpoint bp)
    {
        bp.State = BreakpointState.Disabled;
        if (bp.GdbId > 0 && IsDebugging)
        {
            _gdb.BreakDisable(bp.GdbId);
            await Task.Delay(50);
        }
    }

    // ── 와치 관리 ─────────────────────────────────────────────────────────────

    public Watch AddWatch(string expression)
    {
        var watch = new Watch(expression);
        Watches.Add(watch);
        return watch;
    }

    public void RemoveWatch(Watch watch)
    {
        if (!string.IsNullOrEmpty(watch.VarObjectName) && IsDebugging)
            _gdb.VarDelete(watch.VarObjectName);
        Watches.Remove(watch);
    }

    /// <summary>멈춤 시 모든 와치 값을 갱신한다</summary>
    public async Task RefreshWatchesAsync()
    {
        foreach (var watch in Watches.Where(w => w.IsActive))
        {
            if (IsProgramRunning) return;   // 실행 재개됐으면 중단
            var result = await SendCommandAsync($"-data-evaluate-expression \"{watch.Expression}\"");
            if (IsProgramRunning) return;
            if (result?.ResultClass == GdbResultClass.Done)
            {
                string val = result.Results.GetString("value") ?? "<error>";
                watch.UpdateValue(val);
            }
            else
            {
                watch.Invalidate();
            }
        }
        WatchesUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>단일 표현식 평가 (Evaluate 대화상자)</summary>
    public async Task<string> EvaluateExpressionAsync(string expression)
    {
        if (!IsDebugging) return "<debugger not active>";
        var result = await SendCommandAsync($"-data-evaluate-expression \"{expression}\"");
        return result?.Results.GetString("value") ?? "<error>";
    }

    // ── 레지스터 ─────────────────────────────────────────────────────────────

    public async Task RefreshRegistersAsync()
    {
        if (!IsDebugging || IsProgramRunning) return;

        var result = await SendCommandAsync("-data-list-register-values x");
        if (result?.ResultClass != GdbResultClass.Done || IsProgramRunning) return;

        var list = result.Results.GetList("register-values");
        if (list == null) return;

        var newRegisters = new List<Register>();
        foreach (var item in list.Items)
        {
            if (item is not GdbMi.GdbTupleValue t) continue;
            if (!int.TryParse(t.GetString("number"), out int num)) continue;
            string val  = t.GetString("value") ?? string.Empty;
            string name = num < _registerNames.Count ? _registerNames[num] : $"r{num}";

            string prev = _prevRegisters.FirstOrDefault(r => r.Number == num)?.Value ?? val;
            newRegisters.Add(new Register
            {
                Number  = num,
                Name    = name,
                Value   = val,
                Changed = val != prev
            });
        }

        _prevRegisters.Clear();
        _prevRegisters.AddRange(newRegisters);

        App.Current.Dispatcher.Invoke(() =>
        {
            Registers.Clear();
            foreach (var r in newRegisters) Registers.Add(r);
        });

        RegistersUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── 콜 스택 ──────────────────────────────────────────────────────────────

    public async Task RefreshCallStackAsync()
    {
        if (!IsDebugging || IsProgramRunning) return;

        var result = await SendCommandAsync("-stack-list-frames");
        if (result?.ResultClass != GdbResultClass.Done || IsProgramRunning) return;

        var stack = result.Results.GetList("stack");
        if (stack == null) return;

        var frames = new List<StackFrame>();
        foreach (var item in stack.Items)
        {
            if (item is not GdbMi.GdbTupleValue t) continue;
            string? levelStr = t.GetString("level");
            if (!int.TryParse(levelStr, out int level)) continue;

            ulong.TryParse(
                (t.GetString("addr") ?? string.Empty).TrimStart('0', 'x'),
                System.Globalization.NumberStyles.HexNumber, null, out ulong addr);
            int.TryParse(t.GetString("line"), out int lineNum);

            frames.Add(new StackFrame
            {
                Level    = level,
                Function = t.GetString("func")     ?? string.Empty,
                FileName = t.GetString("fullname") ?? t.GetString("file") ?? string.Empty,
                Line     = lineNum,
                Address  = addr
            });
        }

        App.Current.Dispatcher.Invoke(() =>
        {
            CallStack.Clear();
            foreach (var f in frames) CallStack.Add(f);
        });

        CallStackUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── GDB 이벤트 핸들러 ────────────────────────────────────────────────────

    private void OnResultReceived(object? sender, GdbResultRecord result)
    {
        if (result.Token == null) return;
        if (!int.TryParse(result.Token, out int tok)) return;

        lock (_pendingCommands)
        {
            if (_pendingCommands.TryGetValue(tok, out var tcs))
            {
                _pendingCommands.Remove(tok);
                tcs.TrySetResult(result);
            }
        }
    }

    private async void OnAsyncReceived(object? sender, GdbAsyncRecord record)
    {
        if (record.AsyncClass == GdbAsyncClass.Exec)
        {
            if (record.AsyncType == "stopped")
            {
                IsProgramRunning = false;
                var info = GdbMiParser.ExtractStopInfo(record);

                // 상대 경로인 경우 실행 파일 디렉토리 기준으로 절대 경로 보정
                info = ResolveInfoPaths(info);

                Log($"STOPPED: reason={info.Reason} file=\"{info.FileName}\" line={info.Line} func={info.Function}");

                // UI 스레드에서 이벤트 발행
                App.Current.Dispatcher.Invoke(() => Stopped?.Invoke(this, info));

                // 디버그 데이터 갱신 — 각 단계 사이에 exec 명령이 들어오면 중단
                await RefreshWatchesAsync();
                if (!IsProgramRunning) await RefreshRegistersAsync();
                if (!IsProgramRunning) await RefreshCallStackAsync();
            }
            else if (record.AsyncType == "running")
            {
                Log("RUNNING");
                IsProgramRunning = true;
            }
        }
    }

    private void OnStreamReceived(object? sender, GdbStreamOutput stream)
    {
        if (stream.Channel == '~')
            ConsoleOutput?.Invoke(this, stream.Text);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        IsProgramRunning = false;
        int code = _gdb.IsRunning ? 0 : -1;
        App.Current.Dispatcher.Invoke(() =>
        {
            ClearDebugState();
            SessionEnded?.Invoke(this, code);
        });
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    /// <summary>
    /// GDB가 상대 경로(파일명만)로 보고한 경우 실행 파일 디렉토리 기준으로 절대화한다.
    /// 구버전 FPC 번들 GDB는 fullname 대신 file 만 보내는 경우가 있다.
    /// </summary>
    private DebugStopInfo ResolveInfoPaths(DebugStopInfo info)
    {
        if (string.IsNullOrEmpty(info.FileName)) return info;

        // 이미 절대 경로면 그대로 반환 (/ → \ 정규화만)
        string normalized = info.FileName.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return info with { FileName = normalized };

        // 상대 경로 → 실행 파일 디렉토리 기준으로 탐색
        string? exeDir = ExecutableFile != null
            ? Path.GetDirectoryName(ExecutableFile)
            : null;

        if (exeDir != null)
        {
            // 1) exeDir + filename
            string candidate = Path.Combine(exeDir, Path.GetFileName(normalized));
            if (File.Exists(candidate))
            {
                Log($"PATH RESOLVED: \"{info.FileName}\" → \"{candidate}\"");
                return info with { FileName = candidate };
            }

            // 2) exeDir + relative path (subdirectory 포함 경우)
            candidate = Path.GetFullPath(Path.Combine(exeDir, normalized));
            if (File.Exists(candidate))
            {
                Log($"PATH RESOLVED: \"{info.FileName}\" → \"{candidate}\"");
                return info with { FileName = candidate };
            }
        }

        Log($"PATH UNRESOLVED: \"{info.FileName}\" (exeDir={exeDir ?? "null"})");
        return info with { FileName = normalized };
    }

    /// <summary>
    /// TCS를 먼저 등록한 뒤 명령을 전송하여 레이스 컨디션 방지.
    /// (기존: SendCommand → TCS 등록 → GDB가 TCS 등록 전에 응답하면 유실)
    /// </summary>
    private async Task<GdbResultRecord?> SendCommandAsync(string command, int timeoutMs = 5000)
    {
        var tcs = new TaskCompletionSource<GdbResultRecord>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // ① 토큰 할당
        int token = _gdb.AllocateToken();

        // ② TCS 먼저 등록
        lock (_pendingCommands)
            _pendingCommands[token] = tcs;

        // ③ 그 다음 명령 전송
        try
        {
            _gdb.SendCommandRaw(token, command);
        }
        catch (Exception ex)
        {
            lock (_pendingCommands)
                _pendingCommands.Remove(token);
            Log($"SendCommandAsync SEND ERROR: {ex.Message}");
            return null;
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() => tcs.TrySetCanceled());

        try   { return await tcs.Task; }
        catch (OperationCanceledException)
        {
            Log($"SendCommandAsync TIMEOUT: token={token} cmd={command}");
            return null;
        }
        catch { return null; }
        finally
        {
            lock (_pendingCommands)
                _pendingCommands.Remove(token);
        }
    }

    private async Task InsertAllBreakpointsAsync()
    {
        foreach (var bp in Breakpoints.Where(b => b.State == BreakpointState.Enabled))
            await SyncBreakpointToGdbAsync(bp);
    }

    private async Task SyncBreakpointToGdbAsync(Breakpoint bp)
    {
        string cmd;
        if (bp.Type == BreakpointType.FileLine)
        {
            // FPC 번들 구버전 GDB는 Windows 절대 경로를 파싱하지 못한다.
            // (드라이브 콜론 C: 혼동, --source 옵션 미지원)
            // -environment-directory 로 소스 디렉터리를 미리 지정했으므로
            // 파일명만 넘기면 GDB가 소스 디렉터리에서 자동으로 찾는다.
            string file = System.IO.Path.GetFileName(bp.FileName);
            cmd = $"-break-insert {file}:{bp.Line}";
        }
        else
        {
            string loc = bp.ToGdbLocation();
            cmd = bp.Type switch
            {
                BreakpointType.Watch     => $"-break-watch {loc}",
                BreakpointType.ReadWatch => $"-break-watch -r {loc}",
                BreakpointType.AnyWatch  => $"-break-watch -a {loc}",
                _                         => $"-break-insert {loc}"
            };
        }

        var result = await SendCommandAsync(cmd);
        if (result?.ResultClass == GdbResultClass.Done)
        {
            var bpInfo = result.Results.GetTuple("bkpt");
            if (int.TryParse(bpInfo?.GetString("number"), out int gdbId))
            {
                bp.GdbId   = gdbId;
                bp.GdbState = BreakpointState.Enabled;
            }
        }

        if (!string.IsNullOrEmpty(bp.Condition) && bp.GdbId > 0)
            _gdb.BreakCondition(bp.GdbId, bp.Condition);
    }

    private async Task LoadRegisterNamesAsync()
    {
        var result = await SendCommandAsync("-data-list-register-names");
        if (result?.ResultClass != GdbResultClass.Done) return;

        var list = result.Results.GetList("register-names");
        if (list == null) return;

        _registerNames.Clear();
        foreach (var item in list.Items)
        {
            if (item is GdbMi.GdbStringValue s)
                _registerNames.Add(s.Value);
        }
    }

    private void ClearDebugState()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            CallStack.Clear();
        });
    }

    private void ClearBreakpointGdbIds()
    {
        foreach (var bp in Breakpoints)
        {
            bp.GdbId   = -1;
            bp.GdbState = BreakpointState.Disabled;
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _gdb.Dispose();
}
