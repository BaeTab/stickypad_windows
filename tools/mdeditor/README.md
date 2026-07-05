# WYSIWYG 마크다운 에디터 번들 (CodeMirror 6)

StickyPad의 마크다운 노트 WYSIWYG 편집기(오프라인)를 만드는 dev 소스입니다.
빌드 산출물은 `stickypad/Assets/mdeditor/`로 복사되어 앱에 포함됩니다.

## 구성
- `editor.js` — CodeMirror 6 설정 + 라이브 프리뷰(마커 숨김) + WebView2 브리지
- `build.mjs` — esbuild로 단일 IIFE 번들 생성
- `package.json` — 의존성(CodeMirror 6 모듈들, esbuild)

## 재빌드
```bash
cd tools/mdeditor
npm install
npm run build          # → dist/mdeditor.bundle.js
# 산출물을 앱 자산으로 복사
cp dist/mdeditor.bundle.js ../../stickypad/Assets/mdeditor/
cp dist/editor.html        ../../stickypad/Assets/mdeditor/   # 호스트 HTML은 이 폴더에 유지
```

> `editor.html`(엄격 CSP 호스트 페이지)은 `stickypad/Assets/mdeditor/`에 직접 두고 관리합니다.
> `node_modules/`, `dist/`는 커밋하지 않습니다(`.gitignore`).

## 보안 메모
- 편집 전용 WebView2에만 스크립트/웹메시지를 허용하고, 렌더 미리보기는 스크립트 OFF 유지.
- 네트워크 전면 차단: 페이지 CSP `default-src 'none'`(+`connect-src 'none'`), 가상 호스트 `DenyCors`,
  페이지 밖 이동·새 창 차단.
- 노트 내용은 HTML 주입이 아니라 문자열 데이터(setMarkdown/getMarkdown)로만 주고받아 코드 실행 경로가 없음.
