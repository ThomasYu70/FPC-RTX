using Microsoft.Extensions.DependencyInjection;
using uFPCEditor.ViewModels;

namespace uFPCEditor;

// ─────────────────────────────────────────────────────────────────────────────
// XAML 바인딩용 서비스 로케이터
// App.xaml Resources에 <local:ServiceLocator x:Key="ServiceLocator"/> 로 등록.
//
// 프로퍼티는 DI 컨테이너에서 지연(lazy) 취득하므로
// App.Services가 초기화된 이후(OnStartup 완료 후)에만 접근한다.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ServiceLocator
{
    public MainViewModel        MainViewModel        => Get<MainViewModel>();
    public BreakpointsViewModel BreakpointsViewModel => Get<BreakpointsViewModel>();
    public WatchesViewModel     WatchesViewModel     => Get<WatchesViewModel>();
    public CallStackViewModel   CallStackViewModel   => Get<CallStackViewModel>();
    public RegistersViewModel   RegistersViewModel   => Get<RegistersViewModel>();
    public CompilerViewModel    CompilerViewModel    => Get<CompilerViewModel>();

    private static T Get<T>() where T : class
        => App.Services.GetRequiredService<T>();
}
