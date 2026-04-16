using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using uFPCEditor.Core.Compiler;
using uFPCEditor.Core.Debugger;
using uFPCEditor.Core.Editor;
using uFPCEditor.ViewModels;

namespace uFPCEditor;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 미처리 예외 → 로그 파일 기록
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // 한글 등 비 Unicode 코드페이지 지원 (CP949, EUC-KR 파일 읽기, FPC 출력 등)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // AvalonEdit에 Pascal 구문 강조 정의 등록
        // — 에디터 창보다 먼저 실행해야 한다.
        PascalHighlightingLoader.Register();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // FPC 컴파일러 / GDB 경로 기본값 설정
        const string fpcBin = @"C:\FPC\3.2.2\bin\i386-win32";
        var compiler = Services.GetRequiredService<FpcCompiler>();
        compiler.FpcPath    = System.IO.Path.Combine(fpcBin, "fpc.exe");
        // x64 타겟: ppcrossx64.exe + units/x86_64-win64 사용
        compiler.TargetCpu  = "x86_64";
        compiler.TargetOs   = "win64";

        var debugger = Services.GetRequiredService<DebugController>();
        // x64 실행 파일 디버깅에는 64비트 GDB가 필요하다.
        // FPC 번들 gdb.exe 는 32비트라 64비트 프로세스를 디버깅할 수 없음.
        // 우선순위: MSYS2 mingw64 → TDM-GCC-64 → FPC 번들(32비트, 임시)
        debugger.GdbPath = FindGdb(fpcBin);
    }

    // ── 예외 핸들러 ───────────────────────────────────────────────────────────

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        MessageBox.Show(
            $"오류가 발생했습니다:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            $"자세한 내용은 crash.log를 확인하세요.",
            "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;   // 앱 계속 유지
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteLog(ex);
    }

    private static void WriteLog(Exception ex)
    {
        try
        {
            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n" +
                $"{ex}\n\n");
        }
        catch { /* 로그 실패는 무시 */ }
    }

    /// <summary>
    /// 64비트 GDB를 찾아 반환. 없으면 32비트 번들 GDB 경로를 반환(동작하지 않을 수 있음).
    /// </summary>
    private static string FindGdb(string fpcBin)
    {
        string[] candidates =
        [
            @"C:\msys64\mingw64\bin\gdb.exe",   // MSYS2 (winget 기본 설치 경로)
            @"C:\msys2\mingw64\bin\gdb.exe",
            @"C:\TDM-GCC-64\bin\gdb.exe",
            System.IO.Path.Combine(fpcBin, "gdb.exe"),   // 32비트 fallback
        ];

        foreach (var path in candidates)
            if (System.IO.File.Exists(path))
                return path;

        return System.IO.Path.Combine(fpcBin, "gdb.exe");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<FpcCompiler>();
        services.AddSingleton<DebugController>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddSingleton<BreakpointsViewModel>();
        services.AddSingleton<WatchesViewModel>();
        services.AddSingleton<CallStackViewModel>();
        services.AddSingleton<RegistersViewModel>();
        services.AddSingleton<CompilerViewModel>();
    }
}
