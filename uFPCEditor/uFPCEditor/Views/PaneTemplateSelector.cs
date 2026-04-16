using System.Windows;
using System.Windows.Controls;
using uFPCEditor.ViewModels;

namespace uFPCEditor.Views;

/// <summary>
/// AvalonDock DocumentsSource 아이템 타입에 따라 DataTemplate 선택.
/// EditorViewModel → EditorTemplate
/// </summary>
public sealed class PaneTemplateSelector : DataTemplateSelector
{
    public DataTemplate? EditorTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is EditorViewModel ? EditorTemplate : base.SelectTemplate(item, container);
}
