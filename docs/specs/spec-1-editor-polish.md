# 스펙 1 — "에디터 한 뼘 더" (리스트 이어쓰기 · 클릭 체크박스 · 코드 하이라이트)

- **대상 버전:** v2.2.2 → **v2.3.0 제안** (기능 추가, minor)
- **작성일:** 2026-07-06
- **상태:** 구현 전 스펙 (코드 미변경)
- **선행 검증:** 본 스펙의 모든 단정은 실제 소스/설치 패키지(`@codemirror/lang-markdown@6.5.0`,
  lockfile 핀)로 재현 검증했다. §0 참조.

---

## 0. 선행 검증 결과 (스펙의 전제)

### 0-1. "리스트 자동 이어쓰기가 죽어있다"는 가설 → **기각. 이미 동작한다.**

의심 지점은 `tools/mdeditor/editor.js:247`의
`Prec.high(keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap]))`가
`markdown({ addKeymap: true })`(`editor.js:240`)의 Enter 처리를 가로챈다는 것이었다.

실측 결과(설치된 `@codemirror/lang-markdown@6.5.0` + editor.js와 동일한 확장 배열로
`EditorState`를 구성해 `state.facet(keymap)` 디스패치 순서를 확인):

```text
Enter dispatch order:     ["insertNewlineContinueMarkup(markdown)", "insertNewlineAndIndent(default)"]
Backspace dispatch order: ["deleteMarkupBackward(markdown)", "deleteCharBackward"]
```

이유: lang-markdown 6.5.0은 `addKeymap` 시 **내부에서 이미 `Prec.high(keymap.of(markdownKeymap))`로 등록**한다
(`node_modules/@codemirror/lang-markdown/dist/index.js` L421-422). 같은 `Prec.high` 버킷 안에서는
확장 배열의 등장 순서가 우선순위를 정하는데, `markdown()`(240행)이 마지막 keymap(247행)보다 앞이므로
markdown 키맵이 항상 먼저 실행된다. CI의 번들 드리프트 검증(`.github/workflows/ci.yml` editor-bundle job)이
커밋된 번들 = lockfile 빌드를 보장하므로, **배포 번들에서도 이어쓰기는 살아있다.**

→ 기능 1의 작업은 "구현"이 아니라 **(a) 암묵적 순서 의존을 명시적으로 굳히고 (b) 회귀 테스트를 심는 것**으로 축소된다.

### 0-2. `insertNewlineContinueMarkup` 실제 시맨틱 (UX 정의의 근거)

| 입력(커서 = 줄 끝) | Enter 결과 | 비고 |
|---|---|---|
| `- item` | `- item\n- ` | 마커 이어쓰기 |
| `1. one` | `1. one\n2. ` | 번호 자동 증가 |
| `1. one▮\n2. two` (1행 끝) | `1. one\n2. \n3. two` | **뒤따르는 항목 자동 리넘버** |
| `- [x] done` | `- [x] done\n- [ ] ` | 태스크는 빈 체크박스로 이어쓰기 |
| `- a\n  - b` | `…\n  - ` | 중첩 깊이 유지 |
| `> q` | `> q\n> ` | 인용도 이어쓰기 |
| `- ` (유일한 빈 항목) | `` (빈 문서) | 즉시 목록 해제 |
| `- a\n- b\n- ` (3번째 빈 항목) | `- a\n- b\n` | 즉시 목록 해제 |
| `- item\n- ` (2항목 중 빈 2번째) | 1차 Enter: 빈 줄 삽입 → 2차 Enter: 마커 제거 | CM6 고유 시맨틱(§1-2) |
| 일반 텍스트 줄 | 처리 안 함 → defaultKeymap으로 폴스루 | 정상 |

### 0-3. GFM 태스크 구문 트리 (체크박스 위젯의 대상 노드)

`markdownLanguage`(GFM)로 `- [ ] todo` 파싱 시 노드:
`BulletList > ListItem > ( ListMark, Task > ( TaskMarker, …텍스트 ) )`.
위젯 치환 대상은 **`TaskMarker`**(`[ ]` / `[x]`, 3문자 고정), 불릿 숨김 대상은 Task를 형제로 둔 `ListMark`.

### 0-4. 하이라이트 언어팩 번들 크기 실측

editor.js 전체 + 아래 §3-3 언어 세트를 esbuild(iife, minify, chrome110)로 프로브 빌드:

| | 크기 |
|---|---|
| 현재 `mdeditor.bundle.js` | 514,636 B (약 503KB) |
| 언어팩 포함 프로브 | 771,603 B (약 754KB) |
| **증가분** | **+257KB (+50%)** |

단일 파일 exe에 임베드되는 자산이므로 배포 크기도 그만큼 증가. 데스크톱 앱 기준 수용 가능으로 판단.

---

## 1. 목표 / 비목표

### 목표
1. **리스트 자동 이어쓰기(WYSIWYG):** 이미 동작함을 회귀 테스트로 고정하고, keymap 우선순위 의존을 명시화한다.
2. **클릭 가능한 체크박스(WYSIWYG):** `- [ ]`/`- [x]`를 렌더된 체크박스로 보여주고 클릭으로 토글한다.
3. **코드블록 구문 강조:** ```` ```lang ```` 펜스 코드블록을
   (a) WYSIWYG 에디터(CM6)와 (b) 렌더 미리보기/내보내기(Markdig) 양쪽에서 하이라이트한다.

### 비목표 (명시적 제외)
- **렌더 미리보기(Preview WebView2)에서의 체크박스 클릭.** `IsScriptEnabled=false`는 보안 리뷰로 확정된
  불변식(`NoteWindow.xaml.cs:606`)이며, 스크립트 없이 클릭→마크다운 소스 반영 경로가 없다.
  토글은 **WYSIWYG 에디터로 스코프 한정**한다. (미리보기의 Markdig 태스크 체크박스는 지금처럼 `disabled` 표시 전용.)
- 빈 항목 Enter 시맨틱의 커스텀 오버라이드. CM6 기본(§0-2)을 그대로 수용한다 — 리넘버링·중첩까지
  검증된 구현을 손으로 재작성하는 위험 > "2항목 리스트에서 Enter 두 번" 마이너 UX 차이.
- 저장 포맷 변경 없음. **노트 내용은 순수 마크다운 소스 그대로**(볼트 `.md` 왕복 호환 유지).
  하이라이트·체크박스 위젯은 전부 뷰 데코레이션이며 문서를 바꾸지 않는다. 문서가 바뀌는 순간은
  사용자가 의도한 두 경우뿐: Enter 이어쓰기, 체크박스 클릭 토글(`[ ]`↔`[x]` 3문자 등길이 치환).
- 다크 테마 하이라이트 팔레트(노트 배경이 파스텔 라이트 계열이므로 라이트 팔레트 단일).

---

## 2. UX 동작 정의

### 2-1. 리스트 자동 이어쓰기 (WYSIWYG)

§0-2 표가 그대로 스펙이다. 요약 규칙:
- 내용 있는 목록 항목에서 Enter → 같은 깊이·같은 종류 마커 자동 삽입(번호는 +1, 뒤 항목 자동 리넘버).
- 태스크 항목에서 Enter → 항상 `- [ ] `(빈 체크박스)로 이어쓰기.
- 빈 항목에서 Enter → 목록 해제(마커 제거). 단 "2항목 리스트의 빈 2번째 항목"만 CM6 시맨틱상
  1차 Enter가 빈 줄을 삽입하고 2차 Enter가 해제한다. **문서화하고 수용한다.**
- Backspace: 줄 머리에서 마커 한 겹 삭제(`deleteMarkupBackward`) — 이미 활성, 테스트만 추가.
- 일반 문단에서는 개입하지 않는다(defaultKeymap 폴스루).

### 2-2. 클릭 가능한 체크박스 (WYSIWYG)

- **표시:** 커서가 없는 줄(라이브 프리뷰의 기존 철학 = `activeLines`)에서
  `- [ ]`/`- [x]`의 `ListMark`("-")와 공백을 숨기고 `TaskMarker`를 실제 `<input type="checkbox">` 위젯으로 치환.
  커서가 있는 줄은 지금처럼 원본 소스(`- [ ]`)를 그대로 보여준다(기존 마커 숨김 규칙과 동일).
- **클릭:** 체크박스 클릭 시
  - 문서에서 해당 `TaskMarker` 3문자를 `[ ]`↔`[x]`로 치환한다(`[X]` 대문자도 `[ ]`로 정규화).
  - **커서/선택은 이동하지 않는다.** `mousedown`을 `preventDefault`하여 클릭이 선택을 옮기지 않게 하고,
    치환은 등길이라 기존 커서 위치가 그대로 보존된다(선택 매핑 불변).
  - 한 번의 dispatch = 한 번의 undo 스텝.
  - 토글은 기존 `changeListener`(250ms 디바운스) → `postMessage` → C# 저장 경로를 그대로 탄다.
- **엣지케이스:**
  - 중첩 태스크: 각 항목의 TaskMarker가 독립 위젯 — 깊이 무관 동작.
  - `[x]` 뒤 본문에 있는 `[ ]` 문자열: TaskMarker 노드가 아니므로 위젯화하지 않는다(구문 트리 기준).
  - 토글 직후 해당 줄로 커서를 옮기면 소스가 다시 보인다(활성 줄 규칙) — 의도된 동작.
- **(선택, nice-to-have)** 체크된 항목 본문에 취소선+흐림 스타일. 구현 여유 시에만.

### 2-3. 코드블록 구문 강조

- **WYSIWYG(CM6):** ```` ```js ```` 등 정보 문자열로 언어를 찾아 코드 토큰(키워드/문자열/주석/숫자 등)에 색을 입힌다.
  미지원 언어·정보 없음 → 현행처럼 모노스페이스 무색. 코드블록 줄에 옅은 배경(라인 데코, 선택 항목).
- **렌더 미리보기 + HTML/PDF 내보내기(Markdig):** 동일한 펜스 블록을 서버(C#)측에서
  **인라인 스타일로 구워** 하이라이트한다. 스크립트 0줄 — CSP·`IsScriptEnabled=false` 불변식 유지.
- **지원 언어(1차):** JavaScript/TypeScript/JSX, Python, HTML, CSS, JSON, SQL, XML, C/C++, Java,
  C#, Shell(bash), YAML, PowerShell. (CM6 측 별칭: `js, jsx, ts, tsx, py, htm, cs, sh, bash, zsh, yml, ps1` 등)
- 에디터와 미리보기의 팔레트는 "유사하면 충분"(동일 강제 아님). 양쪽 모두 라이트 파스텔 배경에서 가독 확보.

---

## 3. 기술 설계

### 3-1. `tools/mdeditor/editor.js` — keymap 우선순위 명시화 (기능 1)

현재 247행의 `Prec.high(...)`는 markdown 키맵과 같은 버킷이라 "배열 순서"라는 암묵 규칙에 기대고 있다.
**defaultKeymap 묶음을 기본 우선순위로 강등**해 구조적으로 markdown 키맵이 항상 이기게 한다:

```js
// BEFORE (editor.js:246-247)
Prec.high(formatKeymap),
Prec.high(keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap])),

// AFTER — 불변식: lang-markdown 의 Enter/Backspace(insertNewlineContinueMarkup /
// deleteMarkupBackward)는 Prec.high 로 등록되므로(6.5.0 dist L421) 이 키맵보다 항상 먼저 본다.
// 여기를 다시 Prec.high 로 올리면 배열 순서에 따라 리스트 이어쓰기가 죽을 수 있다 — 올리지 말 것.
Prec.high(formatKeymap),
keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap]),
```

영향 분석: 이 파일에는 다른 키맵 소스가 formatKeymap(서식 전용 키, Enter/Backspace 없음)과
markdown 키맵뿐이므로 강등으로 순서가 바뀌는 다른 바인딩이 없다. 동작 변화 0, 의도 명시화가 전부.

### 3-2. `tools/mdeditor/editor.js` — 체크박스 위젯 (기능 2)

CM6 공식 데코레이션 예제(boolean toggle) 패턴을 그대로 쓴다. 기존 `buildDecorations`(42행)에 통합
(RangeSetBuilder는 from 오름차순 add — 같은 iterate 루프 안이므로 자연 충족):

```js
// TaskMarker([ ] / [x])를 클릭 가능한 체크박스로 — 커서 없는 줄에서만(라이브 프리뷰 철학 유지)
class TaskCheckbox extends WidgetType {
  constructor(checked) { super(); this.checked = checked; }
  eq(other) { return other.checked === this.checked; }
  toDOM() {
    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = this.checked;
    box.className = "cm-task-toggle";
    return box;
  }
  ignoreEvent(e) { return e.type !== "mousedown"; }   // mousedown 은 아래 핸들러가 처리
}

// buildDecorations 의 enter 콜백에 추가:
if (node.name === "TaskMarker") {
  const line = view.state.doc.lineAt(node.from).number;
  if (!act.has(line)) {
    const checked = /x/i.test(view.state.doc.sliceString(node.from, node.to));
    builder.add(node.from, node.to, Decoration.replace({ widget: new TaskCheckbox(checked) }));
  }
  return;
}
// Task 줄의 ListMark("- ")는 숨긴다: node.name === "ListMark" 이고
// node.node.nextSibling?.name === "Task" 인 경우 HIDE 대상에 합류(뒤따르는 공백 1칸 포함).

// 클릭 → 문서 토글(커서 비이동). livePreview 플러그인의 eventHandlers 로 배선:
function toggleTaskAt(view, pos) {
  // pos 는 위젯 DOM 의 posAtDOM 결과 — 해당 위치의 TaskMarker 를 트리에서 찾아 등길이 치환
  let done = false;
  syntaxTree(view.state).iterate({ from: pos, to: pos + 3, enter: (n) => {
    if (n.name !== "TaskMarker" || done) return;
    const cur = view.state.doc.sliceString(n.from, n.to);
    view.dispatch({ changes: { from: n.from, to: n.to, insert: /x/i.test(cur) ? "[ ]" : "[x]" } });
    done = true;
  }});
  return done;
}
// ViewPlugin.fromClass(..., { decorations: ..., eventHandlers: {
//   mousedown(e, view) {
//     if (e.target.classList?.contains("cm-task-toggle"))
//       return toggleTaskAt(view, view.posAtDOM(e.target)) && e.preventDefault(), true;
//   }}})
```

- `toggleTaskAt`은 **뷰 비의존 로직을 최대한 분리**(입력: state+pos → 출력: change spec)해서
  Node 셀프테스트(§5-1)에서 DOM 없이 검증 가능하게 작성한다.
- livePreview의 `update()` 조건(docChanged/selectionSet/viewportChanged)은 그대로 충분 —
  토글은 docChanged로 재빌드된다.

### 3-3. `tools/mdeditor/editor.js` + `package.json` — CM6 코드 하이라이트 (기능 3a)

`@codemirror/language-data`(전 언어, 수 MB) 대신 **정적 임포트 큐레이션 세트**를 쓴다(+257KB 실측, §0-4):

```js
// package.json dependencies 추가(모두 codemirror 공식 스코프, lockfile 핀):
// @codemirror/lang-javascript, lang-python, lang-html, lang-css, lang-json,
// lang-sql, lang-xml, lang-cpp, lang-java, legacy-modes
import { LanguageDescription, StreamLanguage } from "@codemirror/language";
import { javascript } from "@codemirror/lang-javascript";
// … (python, html, css, json, sql, xml, cpp, java)
import { csharp } from "@codemirror/legacy-modes/mode/clike";
import { shell } from "@codemirror/legacy-modes/mode/shell";
import { yaml } from "@codemirror/legacy-modes/mode/yaml";
import { powerShell } from "@codemirror/legacy-modes/mode/powershell";  // 존재 확인 완료

const codeLanguages = [
  LanguageDescription.of({ name: "javascript", alias: ["js", "jsx", "node"],
    load: async () => javascript({ jsx: true }) }),
  LanguageDescription.of({ name: "typescript", alias: ["ts", "tsx"],
    load: async () => javascript({ typescript: true, jsx: true }) }),
  // … python/html/css/json/sql/xml/cpp(c 별칭 포함)/java …
  LanguageDescription.of({ name: "csharp", alias: ["cs", "c#"],
    load: async () => new LanguageSupport(StreamLanguage.define(csharp)) }),
  // … shell(bash/sh/zsh), yaml(yml), powershell(ps1) …
];

// 240행 변경:
markdown({ base: markdownLanguage, addKeymap: true, codeLanguages }),
```

`hl`(HighlightStyle, 12행)에 코드 토큰 색 추가 — 현재는 마크다운 태그만 있어 코드 토큰이 무색이다:

```js
{ tag: t.keyword, color: "#7c3aed" },
{ tag: [t.string, t.special(t.string)], color: "#15803d" },
{ tag: t.comment, color: "#9ca3af", fontStyle: "italic" },
{ tag: [t.number, t.bool, t.atom], color: "#b45309" },
{ tag: [t.typeName, t.className, t.namespace], color: "#0e7490" },
{ tag: [t.function(t.variableName), t.function(t.propertyName)], color: "#1d4ed8" },
{ tag: [t.operator, t.punctuation], color: "#6b7280" },
{ tag: [t.propertyName, t.attributeName], color: "#a21caf" },
{ tag: [t.tagName], color: "#be123c" },
```

주의: `t.monospace` 배경(22행)은 인라인 코드용 — 펜스 블록 배경은 별도 라인 데코(FencedCode 라인에
`rgba(0,0,0,.05)`)로 처리하거나 생략(선택 항목).

### 3-4. `stickypad/Utils/HtmlRenderer.cs` — 미리보기·내보내기 하이라이트 (기능 3b)

**방안 비교:**

| 방안 | 장점 | 단점 | 판정 |
|---|---|---|---|
| **(A) ColorCode-Universal** (`ColorCode.HtmlFormatter` NuGet) | MS CommunityToolkit 조직·MIT·오프라인·`HtmlFormatter`가 **인라인 스타일** 출력 → CSP `style-src 'unsafe-inline'`에 그대로 부합, 스크립트 0 | 새 NuGet 2개(Core+HtmlFormatter) 공급망 추가; 언어 커버리지가 CM6보다 좁음(단 §2-3 목록은 전부 커버: C#/C++/CSS/HTML/Java/JS/TS/PS/Python/SQL/XML 등) | **채택** |
| (B) Markdig.SyntaxHighlighting | 배선 간단 | 서드파티 개인 메인테이너·구버전 ColorCode 래퍼·업데이트 정체 | 기각 |
| (C) CM6만 하이라이트(미리보기 무강조) | 공급망 증가 0 | 미리보기가 기본 화면(분할 뷰·`.md`는 미리보기로 열림)이라 체감 반감 | 기각 |

**구현:** Markdig 파이프라인에 커스텀 렌더러 — `HtmlObjectRenderer<CodeBlock>`을 교체:

```csharp
// 새 파일: stickypad/Utils/CodeBlockHighlightRenderer.cs
/// FencedCodeBlock 의 info(```csharp 등)를 ColorCode 언어로 매핑해 인라인 스타일 HTML 로 렌더.
/// 미지원 언어·비펜스 블록은 기존과 동일하게 이스케이프된 <pre><code> 로 폴백.
internal sealed class CodeBlockHighlightRenderer : HtmlObjectRenderer<CodeBlock>
{
    private static readonly HtmlFormatter Formatter = new(StyleDictionary.DefaultLight);

    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        var lang = (block as FencedCodeBlock)?.Info is { Length: > 0 } info
            ? Languages.FindById(MapAlias(info)) : null;
        var code = ExtractSource(block);            // block.Lines → 원문 텍스트
        if (lang is null) { WritePlainEscaped(renderer, code); return; }   // 현행 경로 유지
        renderer.Write(Formatter.GetHtmlString(code, lang));               // ColorCode 가 이스케이프 수행
    }
    // MapAlias: "js"→"javascript", "cs"→"csharp", "ps1"→"powershell", "yml"→(미지원→null) …
}

// HtmlRenderer.cs — 파이프라인은 그대로, 렌더러 교체 지점만 추가:
//   Markdown.ToHtml(source, Pipeline) 을 MarkdownPipeline + 커스텀 HtmlRenderer 인스턴스 경로로 바꾸고
//   renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(new CodeBlockHighlightRenderer());
//   Render()/RenderDocument() 둘 다 같은 헬퍼(ToHtmlHighlighted)를 사용해 미리보기·내보내기·인쇄 일관 적용.
```

**보안 요구(테스트로 고정, §5-2):**
- ColorCode 출력에 `<script>`·이벤트 핸들러 속성이 절대 포함되지 않을 것 —
  ```` ```html<script>… ```` 입력이 이스케이프된 텍스트로만 나오는지 어서션.
- 미지원 언어 폴백 경로는 기존과 바이트 동일한 이스케이프 출력.
- CSP는 변경하지 않는다(인라인 스타일은 이미 허용, 스크립트는 여전히 차단).
- `RenderDocument`(내보내기)의 더 엄격한 CSP(`img-src data:`만)도 변경 없음 — 하이라이트는 style 속성뿐.

**공급망 명시:** `ColorCode.Core` + `ColorCode.HtmlFormatter`(github.com/CommunityToolkit/ColorCode-Universal,
MIT, Microsoft CommunityToolkit 조직). csproj에 **정확 버전 핀**(구현 시점 최신 안정, 범위 지정 금지),
CHANGELOG 릴리즈 노트에 신규 의존성 고지. NuGet 서명·패키지 소스 매핑은 기존 정책 그대로.

### 3-5. 자산 파이프라인 (변경 없음 확인 사항)

- `stickypad.csproj:25-30`의 임베드(EmbeddedResource) 구조는 그대로 — 번들 파일명 불변이므로 수정 불필요.
- `MarkdownWysiwyg.EnsureAssetsExtracted`(`stickypad/Utils/MarkdownWysiwyg.cs:109`)는 매 프로세스 첫 사용 시
  `File.Create`로 **덮어쓰므로** 구버전 추출 캐시 문제 없음 — 코드 변경 불필요(스모크에서 크기로 확인만).
- 재빌드 절차: `tools/mdeditor`에서 `npm install`(deps 추가 → lockfile 갱신) → `npm run build` →
  `dist/mdeditor.bundle.js`를 `stickypad/Assets/mdeditor/`로 복사. CI editor-bundle job이 드리프트를 잡는다.
- `editor.html`은 체크박스 커서 스타일 1줄(`.cm-task-toggle { cursor: pointer; }`) 외 변경 없음 — CSP 불변.

---

## 4. 작업 분해 + 위험도 분류표

| # | 작업 | 파일 | 위험도 | 근거 |
|---|---|---|---|---|
| 1 | keymap 우선순위 강등 + 불변식 주석 | `tools/mdeditor/editor.js` | **저위험(Sonnet)** | 검증된 1줄 변경 + 주석, 동작 변화 0 |
| 2 | Node 셀프테스트 신규(`test.mjs`): 디스패치 순서·이어쓰기 9케이스·토글 로직 | `tools/mdeditor/test.mjs`(신규), `package.json` scripts | **저위험(Sonnet)** | §0에서 이미 작성·실행한 검증 코드의 정식화 |
| 3 | CI에 셀프테스트 스텝 추가 | `.github/workflows/ci.yml` | **저위험(Sonnet)** | 기존 editor-bundle job에 `npm test` 1스텝 |
| 4 | 체크박스 위젯 + 토글 + ListMark 숨김 통합 | `tools/mdeditor/editor.js` | **고위험(Opus 직접)** | 데코레이션 정렬·이벤트/커서 상호작용·활성줄 전환 등 미묘한 뷰 로직 |
| 5 | CM6 codeLanguages + HighlightStyle 코드 토큰 + deps 추가 | `editor.js`, `package.json`(+lock) | **저위험(Sonnet)** | 공식 API 정형 패턴, 프로브로 빌드 성공·크기 실측 완료 |
| 6 | 번들 재빌드 + 자산 복사 + 드리프트 확인 | `stickypad/Assets/mdeditor/mdeditor.bundle.js` | **저위험(Sonnet)** | 절차 기계적, CI가 검증 |
| 7 | ColorCode 통합(커스텀 Markdig 렌더러, 미리보기+내보내기) | `stickypad/Utils/CodeBlockHighlightRenderer.cs`(신규), `HtmlRenderer.cs`, `stickypad.csproj` | **고위험(Opus 직접)** | 보안 경계(이스케이프·CSP)·신규 공급망·렌더 파이프라인 교체 |
| 8 | C# 단위테스트(하이라이트·이스케이프·폴백) | `stickypad.Tests/Tests.cs` | **저위험(Sonnet)** | 기존 HtmlRendererTests 패턴 답습(어서션 목록은 #7 산출물 기준, Opus가 리뷰) |
| 9 | 단일파일 publish 스모크(§5-4) | (검증만) | **저위험(Sonnet)** | 체크리스트 실행형 — 단 실사용 DB 격리 주의사항 필수 준수 |
| 10 | 문서·CHANGELOG·버전 2.3.0 상향 | `CHANGELOG.md`, `README.md`, `stickypad.csproj` | **저위험(Sonnet)** | 문서/버전 작업 |

권장 순서: 1→2→3 (기능1 완결) → 5→4→6 (에디터측) → 7→8 (렌더측) → 9→10.
4와 7은 서로 독립이라 병렬 가능.

---

## 5. 테스트·검증 계획

### 5-1. Node 셀프테스트 (신규 `tools/mdeditor/test.mjs`, CI 편입)

DOM 없이 `EditorState` + 명령 함수만으로 검증(§0에서 실증한 방식). `node --test` 러너 사용:

- **키맵 회귀:** `state.facet(keymap)`에서 Enter 1순위가 `insertNewlineContinueMarkup`,
  Backspace 1순위가 `deleteMarkupBackward`인지(editor.js와 동일 확장 배열로 구성).
- **이어쓰기 9케이스:** §0-2 표 전체를 입출력 어서션으로.
- **토글 로직:** `toggleTaskAt`의 상태 레벨 코어 — `[ ]`→`[x]`, `[x]`→`[ ]`, `[X]`→`[ ]`,
  본문 속 가짜 `[ ]` 비반응, 토글 후 커서 위치 불변.
- **하이라이트 배선:** 각 지원 별칭(js/ts/py/cs/sh/yml/ps1…)에 대해
  `LanguageDescription.matchLanguageName(codeLanguages, alias)`가 비-null.
- CI: editor-bundle job의 `npm ci` 뒤에 `node --test test.mjs` 스텝 추가(번들 드리프트 검증과 같은 job).

### 5-2. C# 단위테스트 (`stickypad.Tests/Tests.cs`, 기존 HtmlRendererTests에 추가)

1. ```` ```csharp ```` 블록 → 출력에 인라인 `style=` 하이라이트 스팬 존재, 원문 코드 텍스트 보존.
2. ```` ```html ```` 블록에 `<script>alert(1)</script>` → 출력에 **실행 가능한** `<script>` 태그 부재(이스케이프 확인).
3. 미지원 언어(````` ```zig `````)·정보 없음 펜스 → 기존과 동일한 `<pre><code>` 이스케이프 폴백.
4. `RenderDocument`(내보내기)에도 하이라이트 적용 + CSP 메타 불변.
5. 인라인 코드(`` ` ``)는 하이라이트 미적용(기존 스타일 유지).

### 5-3. 헤드리스 Chromium 셀프테스트 (에디터 DOM 상호작용)

`editor.html?selftest=1`(`editor.js:299`)을 확장해 위젯 레벨을 커버:
- 시나리오 추가: 문서 주입 → 커서를 다른 줄에 두고 → `TaskMarker` 위젯 DOM(`.cm-task-toggle`) 존재 확인 →
  프로그래매틱 `toggleTaskAt` 호출 → `getMarkdown()`이 `[x]` 반영 → 결과 JSON을 `#selftest`에 기록.
- 실행: 로컬 정적 서버(`npx http-server` 등)로 `stickypad/Assets/mdeditor/` 서빙 후
  `chrome --headless=new --dump-dom http://127.0.0.1:<port>/editor.html?selftest=1`에서 `#selftest` 내용 어서션.
  (CSP `script-src 'self'`가 file:// 오리진에서 불안정하므로 반드시 http 서빙.)
- CI 편입은 선택(러너 Chrome 가용 시) — 불가하면 릴리즈 전 수동 체크리스트로 유지하고 5-1이 로직을 커버.

### 5-4. 단일 파일 publish 스모크 (과거 회귀 재발 방지 — 필수)

> ⚠️ **실사용 데이터 격리:** 스모크는 실행 중인 StickyPad를 종료하지 말고, 별도 사용자 계정 또는
> `%LOCALAPPDATA%\StickyPad` 백업 후 진행한다(라이브 DB·설정을 건드리기 전 사용자 고지 원칙).

1. `dotnet publish stickypad/stickypad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish`
   (release.yml과 동일 플래그 — Debug 실행으로 대체 검증 금지).
2. `publish/StickyPad.exe` 단독 실행 → 마크다운 노트 생성 → WYSIWYG 토글 ON.
3. 확인: (a) 에디터 정상 로드(과거 회귀 지점: 임베드 자산 추출), (b) `- [ ]` 입력 후 다른 줄로 커서 이동 →
   체크박스 표시·클릭 토글·소스에 `[x]` 반영, (c) 목록에서 Enter 이어쓰기/번호 증가,
   (d) ```` ```js ```` 블록 색상, (e) 미리보기(분할)에서 코드블록 하이라이트, (f) 링크 클릭 시 외부 브라우저(스크립트 OFF 유지 방증).
4. `%LOCALAPPDATA%\StickyPad\mdeditor\mdeditor.bundle.js` 크기가 새 번들(약 754KB±)과 일치 — 구자산 잔존 아님 확인.
5. 볼트 왕복: 노트를 `.md` 내보내기 → 파일 열기 → 토글·이어쓰기 결과가 순수 마크다운으로 저장됐는지 diff.

### 5-5. 회귀 확인

- 기존 107개 테스트 전체 green (`dotnet test -c Release`).
- CI editor-bundle 드리프트 green(lockfile 갱신분 포함 재빌드 일치).

---

## 6. 예상 규모 및 릴리즈

### 변경 규모

| 파일 | 변경 | 규모(줄) |
|---|---|---|
| `tools/mdeditor/editor.js` | 수정 | +150~200 (위젯 80, 언어팩 50, 하이라이트 태그 12, 주석·셀프테스트 확장 30) |
| `tools/mdeditor/test.mjs` | 신규 | ~150 |
| `tools/mdeditor/package.json` + lock | deps 10개 추가 | 소량 + lock 갱신 |
| `stickypad/Assets/mdeditor/mdeditor.bundle.js` | 재빌드 | 514KB → 약 754KB (+257KB 실측) |
| `stickypad/Assets/mdeditor/editor.html` | 수정 | +1 (커서 스타일) |
| `stickypad/Utils/CodeBlockHighlightRenderer.cs` | 신규 | ~80 |
| `stickypad/Utils/HtmlRenderer.cs` | 수정 | ±20 |
| `stickypad/stickypad.csproj` | NuGet 2개 + 버전 | +3 |
| `stickypad.Tests/Tests.cs` | 테스트 ~6개 추가 | +60 |
| `.github/workflows/ci.yml` | 셀프테스트 스텝 | +5 |
| `CHANGELOG.md`, `README.md` | 문서 | +30 |

합계: 소스 파일 약 11개, 순증 약 500~600줄(번들 제외). 신규 공급망: NuGet 2(ColorCode), npm 10(모두 codemirror 공식 스코프·lockfile 핀).

### 릴리즈 제안
- **v2.3.0** (minor — 사용자 가시 기능 3종, 파괴적 변경 없음, 저장 포맷 불변).
- CHANGELOG(Keep a Changelog) Added: 리스트 자동 이어쓰기 회귀 고정·클릭 체크박스·코드 하이라이트(에디터+미리보기),
  Changed: 에디터 번들 크기 +257KB, Dependencies: ColorCode-Universal 추가 고지.
- 태그 전 README 스크린샷 갱신 시 screen-capture 스킬 절차 준수.
