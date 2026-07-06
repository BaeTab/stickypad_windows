// Node 셀프테스트 — DOM 없이 EditorState + 명령 함수만으로 스펙-1(§0/§5-1)의 실측을 회귀 고정한다.
// editor.js 는 브라우저 전역(document, window.chrome 등)에 의존하므로 여기서 import 하지 않는다.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

import { EditorState, EditorSelection } from "@codemirror/state";
import { keymap } from "@codemirror/view";
import { defaultKeymap, historyKeymap, indentWithTab } from "@codemirror/commands";
import {
  markdown, markdownLanguage, insertNewlineContinueMarkup, deleteMarkupBackward,
} from "@codemirror/lang-markdown";
import { ensureSyntaxTree } from "@codemirror/language";
import { taskToggleChange } from "./core.mjs";

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));

// editor.js 와 동일한 확장 배열(뷰 비의존 부분만) — markdown() 이 keymap.of(...) 보다 앞에 오는
// 순서까지 재현해 §0-1 에서 실측한 디스패치 우선순위를 그대로 검증한다.
function markdownState(doc, cursor = doc.length) {
  const state = EditorState.create({
    doc,
    selection: EditorSelection.single(cursor),
    extensions: [
      markdown({ base: markdownLanguage, addKeymap: true }),
      keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap]),
    ],
  });
  ensureSyntaxTree(state, state.doc.length, 5000);
  return state;
}

// ── §0-1 : keymap 우선순위 회귀 — markdown 키맵이 defaultKeymap 보다 항상 먼저 본다 ──────
test("Enter 1순위는 lang-markdown 의 insertNewlineContinueMarkup", () => {
  const state = markdownState("- item");
  const binding = state.facet(keymap).flat().find((b) => b.key === "Enter");
  assert.equal(binding.run, insertNewlineContinueMarkup);
});

test("Backspace 1순위는 lang-markdown 의 deleteMarkupBackward", () => {
  const state = markdownState("- item");
  const binding = state.facet(keymap).flat().find((b) => b.key === "Backspace");
  assert.equal(binding.run, deleteMarkupBackward);
});

// ── §0-2 : 리스트/인용 자동 이어쓰기 — 표 전체(9케이스 + 일반 텍스트 폴스루) ────────────
function runEnter(doc, cursor) {
  const state = markdownState(doc, cursor);
  let result = null;
  const handled = insertNewlineContinueMarkup({ state, dispatch: (tr) => { result = tr.state; } });
  return { handled, doc: result ? result.doc.toString() : null, cursor: result ? result.selection.main.head : null };
}

test("불릿 리스트: 마커 이어쓰기", () => {
  const r = runEnter("- item", 6);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "- item\n- ");
});

test("번호 리스트: 번호 자동 증가", () => {
  const r = runEnter("1. one", 6);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "1. one\n2. ");
});

test("번호 리스트: 뒤따르는 항목 자동 리넘버", () => {
  const r = runEnter("1. one\n2. two", 6);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "1. one\n2. \n3. two");
});

test("태스크 리스트: 항상 빈 체크박스로 이어쓰기", () => {
  const r = runEnter("- [x] done", 10);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "- [x] done\n- [ ] ");
});

test("중첩 리스트: 깊이 유지", () => {
  const r = runEnter("- a\n  - b", 9);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "- a\n  - b\n  - ");
});

test("인용: 마커 이어쓰기", () => {
  const r = runEnter("> q", 3);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "> q\n> ");
});

test("유일한 빈 항목: 즉시 목록 해제", () => {
  const r = runEnter("- ", 2);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "");
});

test("3번째 빈 항목: 즉시 목록 해제", () => {
  const r = runEnter("- a\n- b\n- ", 10);
  assert.equal(r.handled, true);
  assert.equal(r.doc, "- a\n- b\n");
});

test("2항목 중 빈 2번째: 1차 Enter 는 빈 줄 삽입, 2차 Enter 가 마커 제거 (CM6 고유 시맨틱)", () => {
  const first = runEnter("- item\n- ", 9);
  assert.equal(first.handled, true);
  assert.equal(first.doc, "- item\n\n- ");

  const second = runEnter(first.doc, first.cursor);
  assert.equal(second.handled, true);
  assert.equal(second.doc, "- item\n\n");
});

test("일반 텍스트 줄: 처리하지 않고 defaultKeymap 으로 폴스루", () => {
  const r = runEnter("hello", 5);
  assert.equal(r.handled, false);
});

// ── 클릭 체크박스 토글 코어 (core.mjs, DOM 비의존) ──────────────────────────────────
function taskState(doc) {
  const state = EditorState.create({ doc, extensions: [markdown({ base: markdownLanguage, addKeymap: true })] });
  ensureSyntaxTree(state, state.doc.length, 5000);
  return state;
}

test("체크박스 토글: [ ] -> [x]", () => {
  const spec = taskToggleChange(taskState("- [ ] todo"), 2);
  assert.deepEqual(spec, { from: 2, to: 5, insert: "[x]" });
});

test("체크박스 토글: [x] -> [ ]", () => {
  const spec = taskToggleChange(taskState("- [x] done"), 2);
  assert.deepEqual(spec, { from: 2, to: 5, insert: "[ ]" });
});

test("체크박스 토글: [X](대문자) -> [ ] 로 정규화", () => {
  const spec = taskToggleChange(taskState("- [X] DONE"), 2);
  assert.deepEqual(spec, { from: 2, to: 5, insert: "[ ]" });
});

test("본문 속 가짜 [ ] 텍스트는 반응하지 않음 (TaskMarker 노드가 아님)", () => {
  const doc = "this is not [ ] a task";
  const spec = taskToggleChange(taskState(doc), doc.indexOf("[ ]"));
  assert.equal(spec, null);
});

test("중첩 태스크: 깊이 무관하게 동작", () => {
  const doc = "- a\n  - [ ] nested";
  const pos = doc.indexOf("[ ]");
  const spec = taskToggleChange(taskState(doc), pos);
  assert.deepEqual(spec, { from: pos, to: pos + 3, insert: "[x]" });
});

test("토글 치환은 항상 등길이(3문자)", () => {
  for (const doc of ["- [ ] a", "- [x] b", "- [X] c"]) {
    const spec = taskToggleChange(taskState(doc), 2);
    assert.equal(spec.to - spec.from, 3);
    assert.equal(spec.insert.length, 3);
  }
});

// ── §3-3 : 코드 하이라이트 언어팩 배선 ──────────────────────────────────────────────
// editor.js 는 브라우저 전역 의존이라 import 하지 않는다. 대신 (a) 소스 텍스트에서 별칭 존재를
// 문자열로 확인하고 (b) 실제 패키지가 lockfile 로 해석되는지 확인한다.
// 실행 로딩(esbuild 가 실제로 모듈을 번들링할 수 있는지)은 CI 의 번들 빌드 스텝이 담당한다.
const editorSrc = readFileSync(path.join(__dirname, "editor.js"), "utf8");
const codeLanguagesBlock = editorSrc.match(/const codeLanguages = \[[\s\S]*?\n\];/)?.[0];

test("editor.js 에 codeLanguages 배열이 정의돼 있다", () => {
  assert.ok(codeLanguagesBlock, "codeLanguages 블록을 찾을 수 없음");
});

test("codeLanguages 가 스펙 1차 지원 언어의 별칭을 모두 포함한다", () => {
  const aliases = [
    "js", "jsx", "ts", "tsx", "py", "htm", "cs", '"c#"',
    "bash", "sh", "zsh", "yml", "ps1", "c", "cpp",
  ];
  for (const alias of aliases) {
    const needle = alias.startsWith('"') ? alias : `"${alias}"`;
    assert.ok(codeLanguagesBlock.includes(needle), `별칭 누락: ${alias}`);
  }
});

test("코드 하이라이트용 CodeMirror 언어팩 10개가 lockfile 로 해석된다", () => {
  const direct = [
    "@codemirror/lang-javascript", "@codemirror/lang-python", "@codemirror/lang-html",
    "@codemirror/lang-css", "@codemirror/lang-json", "@codemirror/lang-sql",
    "@codemirror/lang-xml", "@codemirror/lang-cpp", "@codemirror/lang-java",
  ];
  for (const pkg of direct) assert.doesNotThrow(() => require.resolve(pkg), pkg);

  // legacy-modes 는 exports 필드에 루트 진입점이 없어(서브모듈 전용) editor.js 가 실제로
  // import 하는 서브모듈 경로로 확인한다.
  const legacyModes = [
    "@codemirror/legacy-modes/mode/clike", "@codemirror/legacy-modes/mode/shell",
    "@codemirror/legacy-modes/mode/yaml", "@codemirror/legacy-modes/mode/powershell",
  ];
  for (const mod of legacyModes) assert.doesNotThrow(() => require.resolve(mod), mod);
});
