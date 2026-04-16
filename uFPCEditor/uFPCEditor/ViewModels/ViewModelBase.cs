using CommunityToolkit.Mvvm.ComponentModel;

namespace uFPCEditor.ViewModels;

/// <summary>모든 ViewModel의 공통 기반</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;
}
