using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace uFPCEditor.Core.Debugger.GdbMi;

// ─────────────────────────────────────────────────────────────────────────────
// GDB 프로세스 관리 및 GDB/MI 비동기 통신
// 원본 참조: Org/packages/ide/gdbmiwrap.pas  (TGDBProcess, TGDBWrapper)
//
// 동작 방식:
//   1. GDB를 --interpreter=mi2 옵션으로 자식 프로세스로 실행
//   2. stdin 에 GDB/MI 명령 전송
//   3. stdout 을 비동기 스레드로 읽어 이벤트 발생
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GdbMiProcess : IDisposable
{
    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>GDB로부터 결과 레코드 수신 (^done, ^error, ...)</summary>
    public event EventHandler<GdbResultRecord>? ResultReceived;

    /// <summary>GDB로부터 비동기 레코드 수신 (*stopped, =thread-created, ...)</summary>
    public event EventHandler<GdbAsyncRecord>? AsyncReceived;

    /// <summary>GDB console/log/target 스트림 수신</summary>
    public event EventHandler<GdbStreamOutput>? StreamReceived;

    /// <summary>GDB 프로세스 종료</summary>
    public event EventHandler? ProcessExited;

    // ── 상태 ─────────────────────────────────────────────────────────────────

    private Process?       _process;
    private StreamWriter?  _stdin;
    private Thread?        _readerThread;
    private Thread?        _stderrThread;
    private int            _token;
    private volatile bool  _running;

    public bool IsRunning => _running && _process is { HasExited: false };

    // ── 로그 (thread-safe) ────────────────────────────────────────────────────

    private readonly object _logLock = new();
    private StreamWriter?   _miLog;

    /// <summary>외부에서 임의 메시지를 로그에 기록 (DebugController 등에서 사용)</summary>
    public void WriteLog(string message)
    {
        lock (_logLock)
            _miLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private void LogSend(string line)
    {
        lock (_logLock)
            _miLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] >>> {line.TrimEnd()}");
    }

    private void LogRecv(string line)
    {
        lock (_logLock)
            _miLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] <<< {line}");
    }

    private void OpenMiLog()
    {
        lock (_logLock)
        {
            _miLog?.Dispose();
            try
            {
                string path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "gdb_mi.log");
                _miLog = new StreamWriter(path, append: false, Encoding.UTF8)
                    { AutoFlush = true };
                _miLog.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ========== GDB SESSION STARTED ==========");
            }
            catch { _miLog = null; }
        }
    }

    private void CloseMiLog(string reason)
    {
        lock (_logLock)
        {
            _miLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ========== GDB SESSION ENDED: {reason} ==========");
            _miLog?.Dispose();
            _miLog = null;
        }
    }

    // ── GDB 시작/종료 ─────────────────────────────────────────────────────────

    /// <summary>GDB 프로세스 시작</summary>
    /// <param name="gdbPath">GDB 실행 파일 경로 (예: C:\msys2\mingw64\bin\gdb.exe)</param>
    public void Start(string gdbPath)
    {
        if (IsRunning) throw new InvalidOperationException("GDB is already running.");

        // 로그 파일을 먼저 열어야 SendCommand 에서도 기록 가능
        OpenMiLog();
        WriteLog($"GDB PATH: {gdbPath}");

        var psi = new ProcessStartInfo
        {
            FileName               = gdbPath,
            Arguments              = "--interpreter=mi2 --quiet",
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            // GDB(i386 네이티브)는 시스템 OEM 코드페이지(CP949)를 사용
            // stdin/stdout/stderr 모두 동일한 인코딩을 써야 파일 경로 등 non-ASCII 문자가 깨지지 않음
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding  = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardInputEncoding  = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;
        _process.Start();

        // NewLine을 "\n"으로 고정 — GDB/MI는 CRLF(\r\n)를 파싱하지 못하고 명령 끝에 \r이 붙으면 오작동
        _stdin   = new StreamWriter(_process.StandardInput.BaseStream,
                       Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage))
                   { AutoFlush = true, NewLine = "\n" };
        _running = true;
        _token   = 1;

        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name         = "GDB-MI-Reader"
        };
        _readerThread.Start();

        _stderrThread = new Thread(ReadStderrLoop)
        {
            IsBackground = true,
            Name         = "GDB-MI-Stderr"
        };
        _stderrThread.Start();
    }

    /// <summary>GDB 정상 종료</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        WriteLog("--- STOP requested");
        try { SendCommand("-gdb-exit"); }
        catch { /* 이미 종료됨 */ }
        _running = false;
    }

    // ── 토큰 할당 (SendCommandAsync 레이스 컨디션 방지용) ─────────────────────

    /// <summary>
    /// 토큰만 미리 할당한다. SendCommandRaw 와 함께 사용하면
    /// TCS 등록 → 명령 전송 순서를 보장할 수 있다.
    /// </summary>
    public int AllocateToken() => Interlocked.Increment(ref _token);

    /// <summary>미리 할당된 토큰으로 명령 전송 (토큰 증가 없음)</summary>
    public void SendCommandRaw(int token, string command)
    {
        if (_stdin == null || !IsRunning)
            throw new InvalidOperationException("GDB process is not running.");

        string line = $"{token}{command}";
        LogSend(line);
        lock (_stdin)
            _stdin.WriteLine(line);   // NewLine="\n", AutoFlush=true
    }

    // ── 명령 전송 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GDB/MI 명령을 전송하고 사용한 토큰 번호를 반환한다.
    /// 예: SendCommand("-break-insert main")
    /// </summary>
    public int SendCommand(string command)
    {
        if (_stdin == null || !IsRunning)
            throw new InvalidOperationException("GDB process is not running.");

        int tok = Interlocked.Increment(ref _token);
        string line = $"{tok}{command}";
        LogSend(line);
        lock (_stdin)
            _stdin.WriteLine(line);   // NewLine="\n", AutoFlush=true
        return tok;
    }

    // ── 편의 명령 ─────────────────────────────────────────────────────────────

    /// <summary>브레이크포인트 삽입  -break-insert [--temporary] location</summary>
    public int BreakInsert(string location, bool temporary = false)
    {
        string flags = temporary ? "--temporary " : string.Empty;
        return SendCommand($"-break-insert {flags}{location}");
    }

    /// <summary>브레이크포인트 삭제  -break-delete number</summary>
    public int BreakDelete(int gdbId)
        => SendCommand($"-break-delete {gdbId}");

    /// <summary>브레이크포인트 활성화</summary>
    public int BreakEnable(int gdbId)
        => SendCommand($"-break-enable {gdbId}");

    /// <summary>브레이크포인트 비활성화</summary>
    public int BreakDisable(int gdbId)
        => SendCommand($"-break-disable {gdbId}");

    /// <summary>조건 설정</summary>
    public int BreakCondition(int gdbId, string condition)
        => SendCommand($"-break-condition {gdbId} {condition}");

    /// <summary>실행  -exec-run</summary>
    public int ExecRun()
        => SendCommand("-exec-run");

    /// <summary>계속  -exec-continue</summary>
    public int ExecContinue()
        => SendCommand("-exec-continue");

    /// <summary>다음 줄 (Step Over)  -exec-next</summary>
    public int ExecNext()
        => SendCommand("-exec-next");

    /// <summary>함수 안으로 (Step Into)  -exec-step</summary>
    public int ExecStep()
        => SendCommand("-exec-step");

    /// <summary>함수 종료까지 (Step Out)  -exec-finish</summary>
    public int ExecFinish()
        => SendCommand("-exec-finish");

    /// <summary>주소까지 실행  -exec-until file:line</summary>
    public int ExecUntil(string file, int line)
        => SendCommand($"-exec-until {file}:{line}");

    /// <summary>인터럽트  -exec-interrupt</summary>
    public int ExecInterrupt()
        => SendCommand("-exec-interrupt");

    /// <summary>표현식 평가  -data-evaluate-expression expr</summary>
    public int DataEvaluate(string expression)
        => SendCommand($"-data-evaluate-expression {expression}");

    /// <summary>스택 프레임 목록  -stack-list-frames</summary>
    public int StackListFrames()
        => SendCommand("-stack-list-frames");

    /// <summary>레지스터 이름 목록  -data-list-register-names</summary>
    public int ListRegisterNames()
        => SendCommand("-data-list-register-names");

    /// <summary>레지스터 값  -data-list-register-values x</summary>
    public int ListRegisterValues()
        => SendCommand("-data-list-register-values x");

    /// <summary>어셈블리  -data-disassemble -f file -l line -n lines -- 0</summary>
    public int Disassemble(string file, int line, int count = 30)
        => SendCommand($"-data-disassemble -f \"{file}\" -l {line} -n {count} -- 0");

    /// <summary>GDB 변수 객체 생성  -var-create - @ expression</summary>
    public int VarCreate(string expression)
        => SendCommand($"-var-create - @ \"{expression}\"");

    /// <summary>GDB 변수 객체 삭제  -var-delete name</summary>
    public int VarDelete(string name)
        => SendCommand($"-var-delete {name}");

    /// <summary>소스 디렉토리 설정  -environment-directory dir</summary>
    public int SetSourceDir(string directory)
        => SendCommand($"-environment-directory \"{directory}\"");

    /// <summary>실행 파일 설정  -file-exec-and-symbols file</summary>
    public int FileExecAndSymbols(string execFile)
        => SendCommand($"-file-exec-and-symbols \"{execFile}\"");

    // ── stdout 읽기 루프 (별도 스레드) ────────────────────────────────────────

    private void ReadLoop()
    {
        var stdout = _process!.StandardOutput;
        while (_running)
        {
            string? line;
            try   { line = stdout.ReadLine(); }
            catch { break; }

            if (line == null) break;

            LogRecv(line);
            var parsed = GdbMiParser.Parse(line);
            DispatchLine(parsed);
        }
        CloseMiLog("ReadLoop ended");
    }

    // ── stderr 읽기 루프 (별도 스레드) ───────────────────────────────────────

    private void ReadStderrLoop()
    {
        var stderr = _process!.StandardError;
        while (_running)
        {
            string? line;
            try   { line = stderr.ReadLine(); }
            catch { break; }

            if (line == null) break;

            lock (_logLock)
                _miLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] !!! STDERR: {line}");
        }
    }

    private void DispatchLine(GdbMiLine line)
    {
        if (line.ResultRecord != null)
            ResultReceived?.Invoke(this, line.ResultRecord);
        else if (line.AsyncRecord != null)
            AsyncReceived?.Invoke(this, line.AsyncRecord);
        else if (line.StreamOutput != null)
            StreamReceived?.Invoke(this, line.StreamOutput);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _running = false;
        WriteLog("--- GDB PROCESS EXITED");
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _running = false;
        try { _process?.Kill(); } catch { /* 무시 */ }
        _stdin?.Dispose();
        _process?.Dispose();
        CloseMiLog("Dispose");
    }
}
