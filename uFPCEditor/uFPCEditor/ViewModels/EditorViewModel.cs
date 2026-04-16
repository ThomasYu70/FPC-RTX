using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using System.Globalization;
using System.IO;
using System.Text;

namespace uFPCEditor.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// 열린 소스 파일 하나를 나타내는 ViewModel (AvalonDock 문서 탭)
// 원본 참조: Org/packages/ide/weditor.pas  (TEditor), fpide.pas (PSourceWindow)
// ─────────────────────────────────────────────────────────────────────────────

public partial class EditorViewModel : ViewModelBase
{
    // ── 문서 ─────────────────────────────────────────────────────────────────

    public TextDocument Document { get; } = new();

    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private bool   _isModified;
    [ObservableProperty] private bool   _isSelected;
    [ObservableProperty] private bool   _isActive;
    [ObservableProperty] private bool   _isReadOnly;

    // ── 커서 위치 ─────────────────────────────────────────────────────────────

    [ObservableProperty] private int _caretLine   = 1;
    [ObservableProperty] private int _caretColumn = 1;

    public string PositionText => $"Ln {CaretLine}  Col {CaretColumn}";

    // ── 디버그 상태 ───────────────────────────────────────────────────────────

    /// <summary>현재 디버거가 멈춘 라인 (0 = 없음)</summary>
    [ObservableProperty] private int _debugLine;

    // ── Title 연동 ────────────────────────────────────────────────────────────

    partial void OnFilePathChanged(string value)
        => Title = string.IsNullOrEmpty(value) ? "Untitled" : Path.GetFileName(value);

    partial void OnIsModifiedChanged(bool value)
        => Title = (string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath))
                   + (value ? " *" : string.Empty);

    // ── 파일 I/O ──────────────────────────────────────────────────────────────

    public void LoadFile(string path)
    {
        // BOM 우선 감지, 없으면 시스템 ANSI 코드페이지 사용 (한국어 Windows: CP949)
        var fallback = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        using var sr = new StreamReader(path, fallback, detectEncodingFromByteOrderMarks: true);
        Document.Text = sr.ReadToEnd();
        FilePath   = path;
        IsModified = false;
    }

    public void SaveFile()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        // UTF-8 BOM 포함으로 저장 (FPC/IDE 공통 인식)
        File.WriteAllText(FilePath, Document.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        IsModified = false;
    }

    public void SaveFileAs(string path)
    {
        FilePath = path;
        SaveFile();
    }

    // ── 현재 단어 가져오기 (디버거 Evaluate 기본값) ───────────────────────────

    public string GetWordAtCaret()
    {
        var offset = Document.GetOffset(CaretLine, CaretColumn);
        int start  = offset;
        int end    = offset;

        while (start > 0 && IsWordChar(Document.GetCharAt(start - 1))) start--;
        while (end < Document.TextLength && IsWordChar(Document.GetCharAt(end))) end++;

        return start < end ? Document.GetText(start, end - start) : string.Empty;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ── 명령 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? CloseRequested;
}
