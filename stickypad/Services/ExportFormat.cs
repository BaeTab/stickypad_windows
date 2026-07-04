namespace StickyPad.Services;

/// 선택한 노트를 어떤 형태로 내보낼지.
public enum ExportFormat
{
    /// 노트 1개당 .md 파일 1개로, 선택한 폴더에 저장.
    MarkdownFiles,

    /// 모든 노트를 스타일이 적용된 단일 .html 문서로 저장.
    Html,

    /// 위 HTML 문서를 WebView2 로 렌더해 단일 .pdf 로 저장.
    Pdf,
}
