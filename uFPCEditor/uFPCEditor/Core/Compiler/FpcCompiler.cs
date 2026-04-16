using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace uFPCEditor.Core.Compiler;

// ─────────────────────────────────────────────────────────────────────────────
// FPC 컴파일러 래퍼
// 원본 참조: Org/packages/ide/fpcompil.pas  (TIDEApp.DoCompile, DoMake, DoBuild)
//
// FPC 출력 메시지 형식:
//   filename(line,col) Error: message
//   filename(line,col) Warning: message
//   filename(line,col) Note: message
//   filename(line,col) Hint: message
//   Fatal: message
//   Compiling filename
//   Linking filename
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class FpcCompiler
{
    // ── 이벤트 ───────────────────────────────────────────────────────────────

    public event EventHandler<CompilerMessage>? MessageReceived;
#pragma warning disable CS0067   // ProgressChanged는 향후 진행률 UI에서 사용 예정
    public event EventHandler<double>?          ProgressChanged;
#pragma warning restore CS0067

    // ── 설정 ─────────────────────────────────────────────────────────────────

    public string FpcPath       { get; set; } = "fpc";
    public string PrimaryFile   { get; set; } = string.Empty;
    public string OutputDir     { get; set; } = string.Empty;
    public string UnitOutputDir { get; set; } = string.Empty;
    public string TargetOs      { get; set; } = string.Empty;
    public string TargetCpu     { get; set; } = string.Empty;

    // 컴파일러 스위치 (fpswitch.pas 에서 관리하던 옵션들)
    public bool DebugInfo        { get; set; } = true;   // -g
    public bool DwarfDebugInfo   { get; set; } = true;   // -gw
    public bool RangeChecks      { get; set; } = false;  // -Cr
    public bool IOChecks         { get; set; } = false;  // -Ci
    public bool OverflowChecks   { get; set; } = false;  // -Co
    public bool OptimizeSpeed    { get; set; } = false;  // -O2
    public string ExtraOptions   { get; set; } = string.Empty;

    // ── FPC 메시지 파싱 정규식 ───────────────────────────────────────────────
    // 형식: file(line,col) Kind: message
    [GeneratedRegex(
        @"^(?<file>.+)\((?<line>\d+),(?<col>\d+)\)\s+(?<kind>Error|Warning|Note|Hint|Fatal):\s+(?<msg>.+)$",
        RegexOptions.Compiled)]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"^(?<kind>Fatal|Error):\s+(?<msg>.+)$", RegexOptions.Compiled)]
    private static partial Regex FatalPattern();

    [GeneratedRegex(@"^(Compiling|Linking|Assembling)\s+(?<file>.+)$", RegexOptions.Compiled)]
    private static partial Regex ProgressPattern();

    // ── 컴파일 실행 ──────────────────────────────────────────────────────────

    public async Task<CompileResult> CompileAsync(
        string sourceFile,
        CompileMode mode = CompileMode.Compile,
        CancellationToken cancellationToken = default)
    {
        var messages  = new List<CompilerMessage>();
        var sw        = Stopwatch.StartNew();
        var args      = BuildArguments(sourceFile, mode);

        var psi = new ProcessStartInfo
        {
            FileName               = FpcPath,
            Arguments              = args,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            // FPC는 시스템 OEM 코드페이지(한국어 Windows: CP949)로 출력
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding  = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // stdout + stderr 동시 비동기 읽기
        var stdoutTask = ReadOutputAsync(process.StandardOutput, messages, cancellationToken);
        var stderrTask = ReadOutputAsync(process.StandardError,  messages, cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(cancellationToken);

        sw.Stop();

        int errorCount   = messages.Count(m => m.IsError);
        int warningCount = messages.Count(m => m.IsWarning);

        return new CompileResult
        {
            Success      = process.ExitCode == 0,
            ErrorCount   = errorCount,
            WarningCount = warningCount,
            Elapsed      = sw.Elapsed,
            Messages     = messages,
            OutputFile   = process.ExitCode == 0
                           ? DeriveOutputFile(sourceFile)
                           : null
        };
    }

    // ── 인자 조립 ─────────────────────────────────────────────────────────────

    private string BuildArguments(string sourceFile, CompileMode mode)
    {
        var sb = new StringBuilder();

        // 모드별 옵션
        if (mode == CompileMode.Build) sb.Append("-B ");
        if (mode == CompileMode.Make)  sb.Append("-M ");

        // 디버그 정보
        if (DebugInfo)
        {
            sb.Append(DwarfDebugInfo ? "-gw3 " : "-g ");
            sb.Append("-gl ");   // 라인 번호 정보
        }

        // 런타임 체크
        if (RangeChecks)    sb.Append("-Cr ");
        if (IOChecks)       sb.Append("-Ci ");
        if (OverflowChecks) sb.Append("-Co ");

        // 최적화
        if (OptimizeSpeed) sb.Append("-O2 ");

        // 출력 디렉토리
        if (!string.IsNullOrEmpty(OutputDir))
            sb.Append($"-FE\"{OutputDir}\" ");
        if (!string.IsNullOrEmpty(UnitOutputDir))
            sb.Append($"-FU\"{UnitOutputDir}\" ");

        // 크로스 컴파일 타겟
        if (!string.IsNullOrEmpty(TargetOs))
            sb.Append($"-T{TargetOs} ");
        if (!string.IsNullOrEmpty(TargetCpu))
            sb.Append($"-P{TargetCpu} ");

        // 메시지 출력 형식 (파일/라인/컬럼 포함)
        sb.Append("-vewnhi ");

        // 추가 사용자 옵션
        if (!string.IsNullOrEmpty(ExtraOptions))
            sb.Append(ExtraOptions + " ");

        // 소스 파일
        sb.Append($"\"{sourceFile}\"");

        return sb.ToString();
    }

    // ── 출력 스트림 파싱 ─────────────────────────────────────────────────────

    private async Task ReadOutputAsync(
        System.IO.StreamReader reader,
        List<CompilerMessage>  messages,
        CancellationToken      ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            var msg = ParseLine(line);
            if (msg != null)
            {
                lock (messages) messages.Add(msg);
                MessageReceived?.Invoke(this, msg);
            }
        }
    }

    private CompilerMessage? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        // 진행 상황 (Compiling / Linking)
        var progMatch = ProgressPattern().Match(line);
        if (progMatch.Success)
        {
            return new CompilerMessage
            {
                Kind = MessageKind.Progress,
                Text = line
            };
        }

        // 파일:라인:컬럼 진단 메시지
        var diagMatch = DiagnosticPattern().Match(line);
        if (diagMatch.Success)
        {
            int.TryParse(diagMatch.Groups["line"].Value, out int lineNum);
            int.TryParse(diagMatch.Groups["col"].Value,  out int colNum);
            return new CompilerMessage
            {
                Kind     = ParseKind(diagMatch.Groups["kind"].Value),
                FileName = diagMatch.Groups["file"].Value,
                Line     = lineNum,
                Column   = colNum,
                Text     = diagMatch.Groups["msg"].Value
            };
        }

        // Fatal (파일 없음)
        var fatalMatch = FatalPattern().Match(line);
        if (fatalMatch.Success)
        {
            return new CompilerMessage
            {
                Kind = ParseKind(fatalMatch.Groups["kind"].Value),
                Text = fatalMatch.Groups["msg"].Value
            };
        }

        // 기타 정보성 메시지
        return new CompilerMessage
        {
            Kind = MessageKind.Info,
            Text = line
        };
    }

    private static MessageKind ParseKind(string s) => s.ToLowerInvariant() switch
    {
        "error"   => MessageKind.Error,
        "fatal"   => MessageKind.Fatal,
        "warning" => MessageKind.Warning,
        "note"    => MessageKind.Note,
        "hint"    => MessageKind.Hint,
        _          => MessageKind.Info
    };

    private string DeriveOutputFile(string sourceFile)
    {
        string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFile);
        string dir      = string.IsNullOrEmpty(OutputDir)
                          ? System.IO.Path.GetDirectoryName(sourceFile) ?? string.Empty
                          : OutputDir;
        string ext      = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return System.IO.Path.Combine(dir, baseName + ext);
    }
}
