// StickyPad WYSIWYG markdown editor — CodeMirror 6, fully offline.
// Stores/returns plain Markdown source (round-trips cleanly with vault .md).
import { EditorView, keymap, drawSelection, highlightActiveLine, placeholder } from "@codemirror/view";
import { EditorState, Prec, RangeSetBuilder, EditorSelection } from "@codemirror/state";
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

// ── 서식 명령 (버튼·단축키가 마크다운 문법을 대신 넣어준다) ──────────────────
function toggleInline(marker) {
  return (view) => {
    view.dispatch(view.state.changeByRange((range) => {
      const { from, to } = range;
      const before = view.state.sliceDoc(Math.max(0, from - marker.length), from);
      const after = view.state.sliceDoc(to, Math.min(view.state.doc.length, to + marker.length));
      if (before === marker && after === marker) {
        // 이미 감싸져 있으면 벗기기
        return {
          changes: [{ from: from - marker.length, to: from }, { from: to, to: to + marker.length }],
          range: EditorSelection.range(from - marker.length, to - marker.length),
        };
      }
      return {
        changes: [{ from, insert: marker }, { from: to, insert: marker }],
        range: EditorSelection.range(from + marker.length, to + marker.length),
      };
    }));
    view.focus();
    return true;
  };
}

function setHeading(level) {
  return (view) => {
    view.dispatch(view.state.changeByRange((range) => {
      const line = view.state.doc.lineAt(range.head);
      const m = line.text.match(/^(#{1,6}\s+)/);
      const cur = m ? m[1] : "";
      const curLevel = m ? m[1].trim().length : 0;
      const want = curLevel === level ? "" : "#".repeat(level) + " ";  // 같은 레벨이면 해제
      const shift = want.length - cur.length;
      return {
        changes: { from: line.from, to: line.from + cur.length, insert: want },
        range: EditorSelection.range(range.anchor + shift, range.head + shift),
      };
    }));
    view.focus();
    return true;
  };
}

const LINE_MARKERS = [/^>\s+/, /^[-*+]\s\[[ xX]\]\s+/, /^[-*+]\s+/, /^\d+\.\s+/];
function setLinePrefix(prefix) {
  return (view) => {
    view.dispatch(view.state.changeByRange((range) => {
      const line = view.state.doc.lineAt(range.head);
      let cur = "";
      for (const re of LINE_MARKERS) { const mm = line.text.match(re); if (mm) { cur = mm[0]; break; } }
      const want = cur === prefix ? "" : prefix;   // 같은 마커면 해제, 아니면 교체
      const shift = want.length - cur.length;
      return {
        changes: { from: line.from, to: line.from + cur.length, insert: want },
        range: EditorSelection.range(range.anchor + shift, range.head + shift),
      };
    }));
    view.focus();
    return true;
  };
}

function insertLink(view) {
  view.dispatch(view.state.changeByRange((range) => {
    const text = view.state.sliceDoc(range.from, range.to) || "링크";
    const insert = `[${text}](url)`;
    const urlStart = range.from + 1 + text.length + 2;   // "[" + text + "]("
    return {
      changes: { from: range.from, to: range.to, insert },
      range: EditorSelection.range(urlStart, urlStart + 3),   // "url" 선택
    };
  }));
  view.focus();
  return true;
}

const COMMANDS = {
  bold: toggleInline("**"),
  italic: toggleInline("*"),
  strike: toggleInline("~~"),
  code: toggleInline("`"),
  h1: setHeading(1),
  h2: setHeading(2),
  h3: setHeading(3),
  bullet: setLinePrefix("- "),
  numbered: setLinePrefix("1. "),
  task: setLinePrefix("- [ ] "),
  quote: setLinePrefix("> "),
  link: insertLink,
};

const formatKeymap = keymap.of([
  { key: "Mod-b", run: COMMANDS.bold, preventDefault: true },
  { key: "Mod-i", run: COMMANDS.italic, preventDefault: true },
  { key: "Mod-Shift-x", run: COMMANDS.strike, preventDefault: true },
  { key: "Mod-`", run: COMMANDS.code, preventDefault: true },
  { key: "Mod-k", run: COMMANDS.link, preventDefault: true },
  { key: "Mod-Alt-1", run: COMMANDS.h1, preventDefault: true },
  { key: "Mod-Alt-2", run: COMMANDS.h2, preventDefault: true },
  { key: "Mod-Alt-3", run: COMMANDS.h3, preventDefault: true },
  { key: "Mod-Shift-8", run: COMMANDS.bullet, preventDefault: true },
  { key: "Mod-Shift-7", run: COMMANDS.numbered, preventDefault: true },
  { key: "Mod-Shift-9", run: COMMANDS.task, preventDefault: true },
  { key: "Mod-Shift-.", run: COMMANDS.quote, preventDefault: true },
]);

// ── 지역화 (앱 언어를 ?lang= 로 전달받아 툴바 툴팁·플레이스홀더에 적용) ──────
const I18N = {
  en: {
    placeholder: "Type here…  (Ctrl+B bold, Ctrl+I italic)",
    bold: "Bold (Ctrl+B)", italic: "Italic (Ctrl+I)", strike: "Strikethrough (Ctrl+Shift+X)",
    code: "Inline code (Ctrl+`)", h1: "Heading 1 (Ctrl+Alt+1)", h2: "Heading 2 (Ctrl+Alt+2)",
    bullet: "Bulleted list", numbered: "Numbered list", task: "Checklist", quote: "Quote", link: "Link (Ctrl+K)",
  },
  ko: {
    placeholder: "여기에 입력하세요…  (Ctrl+B 굵게, Ctrl+I 기울임)",
    bold: "굵게 (Ctrl+B)", italic: "기울임 (Ctrl+I)", strike: "취소선 (Ctrl+Shift+X)",
    code: "인라인 코드 (Ctrl+`)", h1: "제목 1 (Ctrl+Alt+1)", h2: "제목 2 (Ctrl+Alt+2)",
    bullet: "글머리표 목록", numbered: "번호 목록", task: "체크박스 목록", quote: "인용", link: "링크 (Ctrl+K)",
  },
};
const lang = (new URLSearchParams(location.search).get("lang") || "").toLowerCase().startsWith("ko") ? "ko" : "en";
const L = I18N[lang];

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
        placeholder(L.placeholder),
        Prec.high(formatKeymap),
        Prec.high(keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap])),
      ],
    }),
  });
  view.focus();
}

// 툴바 버튼 배선 — 클릭해도 에디터 선택이 유지되도록 mousedown 을 막는다.
function wireToolbar() {
  document.querySelectorAll("#toolbar [data-cmd]").forEach((btn) => {
    const key = btn.getAttribute("data-cmd");
    if (L[key]) btn.title = L[key];   // 툴팁을 앱 언어로
    btn.addEventListener("mousedown", (e) => e.preventDefault());
    btn.addEventListener("click", () => {
      const cmd = COMMANDS[key];
      if (cmd) cmd(view);
    });
  });
}

// C# → JS : 노트 내용 주입(문자열 데이터로만, HTML 주입 아님)
window.SPEditor = {
  setMarkdown(md) {
    suppress = true;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: md ?? "" } });
    suppress = false;
  },
  getMarkdown() { return view.state.doc.toString(); },
  exec(name) { const c = COMMANDS[name]; if (c) c(view); },
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
wireToolbar();
view.contentDOM.addEventListener("blur", () => { clearTimeout(debounce); postChange(); });

// 셀프테스트(?selftest=1): 명령이 실제로 마크다운 문법을 넣는지 확인용.
if (params.has("selftest")) {
  window.SPEditor.setMarkdown("bold me");
  view.dispatch({ selection: EditorSelection.single(0, view.state.doc.length) });
  window.SPEditor.exec("bold");
  const el = document.getElementById("selftest");
  if (el) el.textContent = JSON.stringify(window.SPEditor.getMarkdown());
}

// 호스트에 준비 완료 통지
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.postMessage(JSON.stringify({ type: "ready" }));
}
