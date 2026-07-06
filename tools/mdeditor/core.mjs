// 순수 로직 코어 — editor.js(esbuild 번들)와 test.mjs(Node 셀프테스트)가 공유한다.
// DOM 비의존: EditorState 와 위치만 받아 change spec 을 돌려준다.
import { syntaxTree } from "@codemirror/language";

/// pos(TaskMarker 시작 위치 부근)의 `[ ]`/`[x]`/`[X]` 3문자를 등길이 토글하는 change spec.
/// TaskMarker 가 아니면 null — 본문 속 가짜 "[ ]" 텍스트에는 반응하지 않는다(구문 트리 기준).
export function taskToggleChange(state, pos) {
  let spec = null;
  syntaxTree(state).iterate({
    from: pos,
    to: Math.min(pos + 3, state.doc.length),
    enter: (n) => {
      if (spec || n.name !== "TaskMarker") return;
      const cur = state.doc.sliceString(n.from, n.to);
      spec = { from: n.from, to: n.to, insert: /x/i.test(cur) ? "[ ]" : "[x]" };
    },
  });
  return spec;
}
