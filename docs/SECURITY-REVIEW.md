# StickyPad 보안 검토 (2026-07-04)

프로젝트 전반 보안 검토. 병렬 리뷰 2건 + 자동 업데이트 경로 직접 검토 + XAML RCE 실증 테스트로
확인했다. 아래 취약점을 v1.6.1 에서 하드닝했다. 앱은 asInvoker(보통 무결성)로 동작하며 모든 OS
쓰기는 HKCU/%LocalAppData% 에 한정된다.

## 발견 사항

| 심각도 | 항목 | 상태 |
|---|---|---|
| 🔴 CRITICAL | 악성 백업(.json) 가져오기 → 원격 코드 실행(RCE). `BackupService.ImportInteractiveAsync` 가 공격자 JSON을 `Note`(Content·Format)로 역직렬화 → `NoteWindow.LoadEditorContent` 가 `RichTextXaml` 을 `TextRange.Load(DataFormats.Xaml)` 로 로드. 이 경로는 WPF의 비제한 XAML 파서라 `ObjectDataProvider` 가젯이 실행됨(실측: 마커 파일 생성 확인). | **수정됨(v1.6.1)** — 가져오기 시 RichTextXaml→PlainText 강등(`SanitizeImportedNote`), 추가로 두 XAML 싱크(`NoteWindow.LoadEditorContent`, `TextExtraction.ToPlainText`)에 위험 마커 가드(`ContainsDangerousXaml`) 적용. |
| 🟠 HIGH | 가져온 `LinkedFilePath` 로 임의 파일 무단 덮어쓰기. 가져온 노트가 임의 경로를 담고 편집되면 노트 본문이 그 경로에 조용히 기록됨(`SyncToLinkedFileAsync`). | **수정됨** — 가져오기 시 `LinkedFilePath`/`LinkedFileSyncedUtc` 제거. |
| 🟡 MEDIUM | WebView2 `NewWindowRequested` 스킴 미검증 → 노트 마크업의 `file:`/UNC/프로토콜 링크 클릭 시 `ShellExecute` 로 로컬 실행. | **수정됨** — http/https 만 허용. |
| 🟡 MEDIUM | 내보내기/미리보기 CSP 가 원격(http/https) 이미지 허용 → 조작된 노트가 미리보기·PDF 생성·공유 `.html` 열람 시 자동 원격 요청(추적/SSRF/평문). | **부분 수정됨** — 내보내기 문서(`RenderDocument`: HTML/PDF/인쇄)는 img/media/font-src 를 `data:` 로 제한. 앱 내 라이브 미리보기(`Render`)는 원격 이미지 유지(사용자 기기 내 열람 — 잔여 위험으로 문서화). |
| 🟡 MEDIUM | 명명 파이프(`StickyPad.OpenFile.v1`) 입력이 `IsSupported` 허용목록 우회 + 개수 무제한(같은 세션 프로세스가 임의 파일 열기/창 폭탄). 기본 DACL 이 타 사용자·저무결성은 이미 차단. | **수정됨** — 파이프 입력을 커맨드라인과 동일하게 `File.Exists`+`IsSupported` 검증 + 최대 20개 제한. |
| 🟡 MEDIUM | 자동 업데이트: 다운로드 exe 서명/해시 미검증 + `release.Tag`(GitHub 값) 미검증으로 임시경로·`.cmd` 삽입 + `%TEMP%` TOCTOU. | **부분 수정됨** — 태그 정규식 검증(`IsSafeReleaseTag`), 다운로드 URL https+GitHub 호스트 제한(`IsTrustedDownloadUrl`), 예측 불가 GUID 스테이징 폴더 + 파일명에 태그 미포함. **잔여/권장: 릴리즈에 SHA-256 게시 후 대조하거나 Authenticode 코드서명 검증 추가**(현재 CI 산출물 미서명). |
| 🟢 LOW | 가져오기가 공격자 Id 신뢰 → 같은 GUID 기존 노트 덮어씀. | **미수정(수용)** — Id 재발급은 정상 백업 복원을 깨뜨림(복원이 중복 생성). 공격자가 특정 GUID를 알아야 하는 낮은 실효성으로 수용. |
| 🟢 LOW | 인쇄/PDF 임시 html(노트 전문) `%TEMP%` 잔류, 로그에 파일 경로(내용 아님) 기록, UNC 자동열기 NTLM 유출, 가져오기 크기 무제한. | **문서화(권장 하드닝)**. |

## 안전 확인됨 (오탐 방지)

- 자동시작·파일연결 레지스트리 따옴표 처리 정상(HKCU, 관리자 불필요, 인젝션 없음)
- 삭제 시 연동파일 보존 가드
- 연동 저장은 Markdown만
- 데이터 `%LocalAppData%` 사용자 ACL
- WebView2 스크립트 차단(`IsScriptEnabled=false`)+CSP `default-src 'none'`(JS 실행 불가)
- System.Text.Json 폴리모픽 가젯 없음

## 권장 후속

1. 업데이트 exe 서명/해시 검증(CI에 SHA-256 게시 추가)
2. 라이브 미리보기 원격 이미지 옵트인
3. 임시 파일 즉시 정리·시작 시 청소
4. 가져오기 크기·개수 상한
