using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.IO;
using System.Reflection;
using System.Xml;

namespace uFPCEditor.Core.Editor;

// ─────────────────────────────────────────────────────────────────────────────
// Pascal 구문 강조 정의 로더
//
// EmbeddedResource로 빌드된 PascalSyntax.xshd를 읽어
// AvalonEdit HighlightingManager에 "Pascal" 이름으로 등록한다.
//
// App.OnStartup() 에서 에디터 창이 열리기 전에 호출해야 한다.
// ─────────────────────────────────────────────────────────────────────────────

public static class PascalHighlightingLoader
{
    // 빌드 결과물의 리소스 이름: <RootNamespace>.<경로>.<파일명>
    private const string ResourceName = "uFPCEditor.Resources.PascalSyntax.xshd";

    private static readonly string[] PascalExtensions = [".pas", ".pp", ".inc"];

    /// <summary>
    /// PascalSyntax.xshd를 어셈블리 임베디드 리소스에서 로드하고
    /// AvalonEdit HighlightingManager에 등록한다.
    /// </summary>
    public static void Register()
    {
        var asm    = Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream(ResourceName);

        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                $"Make sure PascalSyntax.xshd is set to EmbeddedResource in the csproj.");

        using (stream)
        using (var xmlReader = new XmlTextReader(stream))
        {
            var definition = HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(
                "Pascal",
                PascalExtensions,
                definition);
        }
    }

    /// <summary>
    /// 파일 확장자로부터 강조 정의를 가져온다.
    /// Pascal 파일이 아니면 AvalonEdit의 내장 정의(있을 경우)를 반환한다.
    /// </summary>
    public static IHighlightingDefinition? GetByExtension(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }
}
