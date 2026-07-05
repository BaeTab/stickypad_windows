// StickyPad WYSIWYG markdown editor — CodeMirror 6, fully offline.
// Stores/returns plain Markdown source (round-trips cleanly with vault .md).
import { EditorView, keymap, drawSelection, highlightActiveLine, placeholder } from "@codemirror/view";
import { EditorState, Prec, RangeSetBuilder } from "@codemirror/state";
import { history, defaultKeymap, historyKeymap, indentWithTab } from "@codemirror/commands";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { syntaxTree, syntaxHighlighting, HighlightStyle } from "@codemirror/language";
import { tags as t } from "@lezer/highlight";
import { Decoration, ViewPlugin } from "@codemirror/view";

// ── 인라인 토큰 색/두께 (마크다운 소스를 서식처럼 보이게) ──────────────────
const hl = HighlightStyle.define([
  { tag: t.heading1, fontSize: "1.7em", fontWeight: "700", lineHeight: "1.3" },
  { tag: t.heading2, fontSize: "1.42em", fontWeight: "700" },
  { tag: t.heading3, fontSize: "1.22em", fontWeight: "700" },
  { tag: [t.heading4, t.heading5, t.heading6], fontWeight: "700" },
  { tag: t.strong, fontWeight: "700" },
  { tag: t.emphasis, fontStyle: "italic" },
  { tag: t.strikethrough, textDecoration: "line-through" },
  { tag: t.link, color: "#2563eb", textDecoration: "underline" },
  { tag: t.url, color: "#2563eb" },
  { tag: t.monospace, fontFamily: "Consolas, 'Cascadia Mono', monospace", background: "rgba(0,0,0,.06)" },
  { tag: t.quote, color: "#6b7280", fontStyle: "italic" },
  { tag: [t.processingInstruction, t.contentSeparator], color: "#9aa0a6" },
]);

// ── 라이브 프리뷰: 커서가 없는 줄에서는 마크다운 마커를 숨긴다 ───────────────
const HIDE_NODES = new Set([
  "HeaderMark", "EmphasisMark", "StrikethroughMark", "CodeMark", "QuoteMark", "LinkMark",
]);
const hiddenDeco = Decoration.replace({});

function activeLines(state) {
  const set = new Set();
  for (const r of state.selection.ranges) {
    const a = state.doc.lineAt(r.from).number, b = state.doc.lineAt(r.to).number;
    for (let n = a; n <= b; n++) set.add(n);
  }
  return set;
}

function buildDecorations(view) {
  const builder = new RangeSetBuilder();
  const act = activeLines(view.state);
  for (const { from, to } of view.visibleRanges) {
    syntaxTree(view.state).iterate({
      from, to,
      enter: (node) => {
        const isMark = HIDE_NODES.has(node.name);
        // 인라인 링크 `[텍스트](url)` 의 URL 도 숨겨 링크 텍스트만 보이게
        const isLinkUrl = node.name === "URL" && node.node.parent && node.node.parent.name === "Link";
        if (!isMark && !isLinkUrl) return;
        const line = view.state.doc.lineAt(node.from).number;
        if (act.has(line)) return;            // 편집 중인 줄은 원본 마커를 그대로 보여줌
        // HeaderMark 뒤 공백까지 함께 숨겨 제목이 왼쪽에 붙게
        let end = node.to;
        if (node.name === "HeaderMark") {
          const ch = view.state.doc.sliceString(end, end + 1);
          if (ch === " ") end += 1;
        }
        if (end > node.from) builder.add(node.from, end, hiddenDeco);
      },
    });
  }
  return builder.finish();
}

const livePreview = ViewPlugin.fromClass(
  class {
    constructor(view) { this.decorations = buildDecorations(view); }
    update(u) {
      if (u.docChanged || u.selectionSet || u.viewportChanged) {
        this.decorations = buildDecorations(u.view);
      }
    }
  },
  { decorations: (v) => v.decorations }
);

const theme = EditorView.theme({
  "&": { fontSize: "14px", height: "100%", background: "transparent", color: "inherit" },
  ".cm-content": { fontFamily: "'Segoe UI','Malgun Gothic',sans-serif", padding: "10px 12px", caretColor: "currentColor" },
  ".cm-scroller": { overflow: "auto", lineHeight: "1.55" },
  "&.cm-focused": { outline: "none" },
  ".cm-line": { padding: "0" },
});

// ── 호스트(C#) 브리지 ────────────────────────────────────────────────────
let view = null;
let suppress = false;
let debounce = null;

function postChange() {
  if (suppress) return;
  const md = view.state.doc.toString();
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(JSON.stringify({ type: "change", md }));
  }
}

const changeListener = EditorView.updateListener.of((u) => {
  if (!u.docChanged) return;
  clearTimeout(debounce);
  debounce = setTimeout(postChange, 250);
});

function createEditor(initial) {
  view = new EditorView({
    parent: document.getElementById("editor"),
    state: EditorState.create({
      doc: initial || "",
      extensions: [
        history(),
        drawSelection(),
        highlightActiveLine(),
        EditorView.lineWrapping,
        markdown({ base: markdownLanguage, addKeymap: true }),
        syntaxHighlighting(hl),
        livePreview,
        theme,
        changeListener,
        placeholder("여기에 마크다운을 입력하세요…"),
        Prec.high(keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap])),
      ],
    }),
  });
  view.focus();
}

// C# → JS : 노트 내용 주입(문자열 데이터로만, HTML 주입 아님)
window.SPEditor = {
  setMarkdown(md) {
    suppress = true;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: md ?? "" } });
    suppress = false;
  },
  getMarkdown() { return view.state.doc.toString(); },
  focus() { view.focus(); },
};

// WebView2 가 문자열 메시지로 초기 내용을 보낼 수도 있음
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener("message", (e) => {
    try {
      const m = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
      if (m && m.type === "load") window.SPEditor.setMarkdown(m.md);
    } catch { /* ignore */ }
  });
}

// 시작
const params = new URLSearchParams(location.search);
const demo = params.has("demo")
  ? "# StickyPad WYSIWYG\n\n**굵게**, *기울임*, ~~취소선~~, `인라인 코드`.\n\n## 할 일\n- [x] 배포 v2.0.1\n- [ ] 위키 정리\n- [ ] 스크린샷\n\n> 인용문도 이렇게.\n\n[링크](https://github.com/BaeTab/stickypad_windows) 와 #태그.\n"
  : "";
createEditor(demo);
view.contentDOM.addEventListener("blur", () => { clearTimeout(debounce); postChange(); });

// 호스트에 준비 완료 통지
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.postMessage(JSON.stringify({ type: "ready" }));
}
