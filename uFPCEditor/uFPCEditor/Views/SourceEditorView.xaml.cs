using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using uFPCEditor.Core.Debugger;
using uFPCEditor.Core.Editor;
using uFPCEditor.ViewModels;

namespace uFPCEditor.Views;

// ─────────────────────────────────────────────────────────────────────────────
// 소스 에디터 뷰  (AvalonEdit 기반)
// ─────────────────────────────────────────────────────────────────────────────

public partial class SourceEditorView : UserControl
{
    private EditorViewModel?      _vm;
    private DebugLineHighlighter? _debugHighlighter;
    private BreakpointMargin?     _bpMargin;
    private DebugController?      _debugger;

    public SourceEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext 연결 ──────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // ── 이전 vm 정리 ─────────────────────────────────────────────────────
        if (_vm != null)
        {
            Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            Editor.TextChanged                    -= OnTextChanged;
            _vm.PropertyChanged                   -= OnVmPropertyChanged;
        }
        if (_debugHighlighter != null)
        {
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_debugHighlighter);
            _debugHighlighter = null;
        }
        if (_bpMargin != null)
        {
            Editor.TextArea.LeftMargins.Remove(_bpMargin);
            _bpMargin = null;
        }

        _vm = DataContext as EditorViewModel;
        if (_vm == null) return;

        // ── 구문 강조 ─────────────────────────────────────────────────────────
        ApplyHighlightingByExtension(_vm.FilePath);
        _vm.PropertyChanged += OnVmPropertyChanged;

        // ── 브레이크포인트 거터 마진 ──────────────────────────────────────────
        _debugger = App.Services.GetService(typeof(DebugController)) as DebugController;
        _bpMargin = new BreakpointMargin(_debugger, _vm);
        Editor.TextArea.LeftMargins.Insert(0, _bpMargin);

        // ── 디버그 라인 배경 강조 (IBackgroundRenderer) ───────────────────────
        // DocumentColorizingTransformer 는 텍스트 영역만 색칠하므로
        // IBackgroundRenderer 로 전체 라인 너비 사각형을 그린다.
        _debugHighlighter = new DebugLineHighlighter(_vm);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugHighlighter);

        // ── 이벤트 구독 ───────────────────────────────────────────────────────
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged                    += OnTextChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(EditorViewModel.FilePath):
                ApplyHighlightingByExtension(_vm!.FilePath);
                break;

            case nameof(EditorViewModel.DebugLine):
                // BackgroundRenderer 갱신 요청
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                if (_vm!.DebugLine > 0)
                    ScrollToDebugLine(_vm.DebugLine);
                break;

            case nameof(EditorViewModel.CaretLine):
                ScrollToLine(_vm!.CaretLine);
                break;
        }
    }

    // ── 구문 강조 ─────────────────────────────────────────────────────────────

    private void ApplyHighlightingByExtension(string filePath)
    {
        var definition = string.IsNullOrEmpty(filePath)
            ? PascalHighlightingLoader.GetByExtension(".pas")
            : PascalHighlightingLoader.GetByExtension(filePath);
        Editor.SyntaxHighlighting = definition;
    }

    // ── 이벤트 핸들러 ─────────────────────────────────────────────────────────

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_vm == null) return;
        _vm.CaretLine   = Editor.TextArea.Caret.Line;
        _vm.CaretColumn = Editor.TextArea.Caret.Column;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.IsModified = true;
    }

    // ── 스크롤 ───────────────────────────────────────────────────────────────

    /// <summary>디버그 라인으로 스크롤 (캐럿은 이동하지 않음)</summary>
    private void ScrollToDebugLine(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount) return;
        Editor.ScrollTo(line, 1);
    }

    private void ScrollToLine(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount) return;
        Editor.ScrollTo(line, 1);
        Editor.TextArea.Caret.Line   = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.TextArea.Caret.BringCaretToView();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 디버그 실행 라인 배경 강조  (IBackgroundRenderer)
//
// DocumentColorizingTransformer.ChangeLinePart 는 텍스트 런의 배경만 칠한다.
// IBackgroundRenderer 는 TextView 캔버스에 직접 사각형을 그려
// 전체 라인 너비에 걸쳐 노란 배경을 표시한다.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DebugLineHighlighter : IBackgroundRenderer
{
    // 반투명 노란색 (Delphi IDE 디버그 라인 색상)
    private static readonly Brush DebugBrush =
        new SolidColorBrush(Color.FromArgb(160, 255, 240, 0));

    static DebugLineHighlighter() => DebugBrush.Freeze();

    private readonly EditorViewModel _vm;
    public DebugLineHighlighter(EditorViewModel vm) => _vm = vm;

    // Background 레이어에 그린다 (텍스트 아래)
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_vm.DebugLine <= 0) return;
        if (!textView.VisualLinesValid) return;

        foreach (var vl in textView.VisualLines)
        {
            if (vl.FirstDocumentLine.LineNumber != _vm.DebugLine) continue;

            double top = vl.GetTextLineVisualYPosition(
                             vl.TextLines[0], VisualYPosition.LineTop)
                         - textView.VerticalOffset;

            drawingContext.DrawRectangle(
                DebugBrush, null,
                new Rect(0, top, textView.ActualWidth, vl.Height));
            break;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 브레이크포인트 거터 마진
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 16.0;

    private static readonly SolidColorBrush BpBrush       = new(Colors.Red);
    private static readonly SolidColorBrush BpBorderBrush = new(Color.FromRgb(140, 0, 0));
    private static readonly Pen             BpPen          = new(BpBorderBrush, 1.0);

    static BreakpointMargin()
    {
        BpBrush.Freeze();
        BpBorderBrush.Freeze();
        BpPen.Freeze();
    }

    private readonly DebugController? _debugger;
    private readonly EditorViewModel  _vm;

    public BreakpointMargin(DebugController? debugger, EditorViewModel vm)
    {
        _debugger = debugger;
        _vm       = vm;
        Width     = MarginWidth;

        if (debugger != null)
            debugger.Breakpoints.CollectionChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(228, 228, 228)),
            null,
            new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        if (TextView == null || !TextView.VisualLinesValid) return;

        foreach (var vl in TextView.VisualLines)
        {
            int lineNum = vl.FirstDocumentLine.LineNumber;
            if (!HasBreakpoint(lineNum)) continue;

            double y          = vl.GetTextLineVisualYPosition(
                                    vl.TextLines[0], VisualYPosition.LineTop)
                                - TextView.VerticalOffset;
            double lineHeight = vl.Height;
            double diameter   = Math.Min(lineHeight - 2.0, MarginWidth - 4.0);
            double cx         = RenderSize.Width / 2.0;
            double cy         = y + lineHeight / 2.0;

            dc.DrawEllipse(BpBrush, BpPen, new Point(cx, cy), diameter / 2, diameter / 2);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (TextView == null || _vm.FilePath == string.Empty) return;

        Point  pos    = e.GetPosition(TextView);
        double docPos = pos.Y + TextView.VerticalOffset;

        var vl = TextView.GetVisualLineFromVisualTop(docPos);
        if (vl == null) return;

        int lineNum = vl.FirstDocumentLine.LineNumber;
        if (_debugger != null)
            _ = _debugger.ToggleBreakpointAsync(_vm.FilePath, lineNum);

        InvalidateVisual();
        e.Handled = true;
    }

    private bool HasBreakpoint(int lineNumber)
    {
        if (_debugger == null) return false;
        return _debugger.Breakpoints.Any(
            bp => bp.State != BreakpointState.Deleted
               && string.Equals(bp.FileName, _vm.FilePath, StringComparison.OrdinalIgnoreCase)
               && bp.Line == lineNumber);
    }
}
