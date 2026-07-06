# Spec 5 — 볼트 완성 (실시간 폴더 감시 · 충돌 사본 안내 · 외부 앱으로 열기)

- 대상 버전: v2.3.0 (현재 v2.2.2)
- 상태: 스펙 확정 대기 (구현 전)
- 관련 코드 기준 커밋: `1a9dd0d` (main)

v2.0 라이브 볼트 모드(노트 = 폴더의 `.md` + 사이드카 `.stickypad-index.json`)는
외부 변경을 **재시작 시에만** 반영한다(단일 작성자 가정, 위키에 문서화됨).
이 스펙은 그 갭을 메우는 3개 기능 조합의 구현 세부를 정의한다.

---

## 1. 목표 / 비목표

### 목표

| # | 기능 | 요약 |
|---|------|------|
| F1 | 실시간 폴더 감시 | 볼트 폴더의 `.md` 외부 변경(클라우드 동기화, Obsidian/VS Code 편집)을 실행 중 자동 반영. 자기 자신이 쓴 변경(에코)은 무시. 열린 창에 미저장 편집이 있으면 덮어쓰기 전 프롬프트. |
| F2 | 충돌 사본 안내 | OneDrive/Dropbox/Syncthing 충돌 파일 패턴 감지 → 트레이 알림으로 안내(자동 병합·삭제는 하지 않음). 충돌 사본이 원본 노트를 캐시에서 가리는(id 중복) 기존 위험도 함께 차단. |
| F3 | 외부 앱으로 볼트 열기 | 트레이 볼트 메뉴에 "폴더 열기(탐색기)" / "Obsidian에서 열기" / "VS Code에서 열기" 추가. 미설치 앱 항목은 숨김. |

### 비목표 (이번 릴리즈에서 하지 않는 것)

- **3-way 병합 / 자동 충돌 해결** — 외부 변경 vs 로컬 미저장 편집은 "파일 우선 / 내 편집 유지" 양자택일 프롬프트까지만. 충돌 사본 파일도 안내만 하고 자동 병합·삭제하지 않는다.
- **`.stickypad-index.json` 외부 편집 감시** — 사이드카는 StickyPad 전용 파일. 외부 도구가 수정할 일이 없고, SaveOne 이 항상 다시 쓰므로(에코 소음원) 감시 대상에서 제외한다.
- **볼트 경로 변경 핫스왑** — 저장소 전환·경로 변경은 기존과 동일하게 재시작 후 적용(`AppSettings.StorageMode` 주석 정책 유지).
- **LiteDB 모드에서의 감시** — 감시 서비스는 볼트 모드에서만 기동.
- **FileSystemWatcher 실패 시 폴링 폴백** — 네트워크 드라이브 등에서 FSW 가 실패하면 로그만 남기고 기능을 조용히 끈다(감시는 부가 기능 — `NoteWindow.StartFileWatcherIfLinked` 의 기존 태도와 동일).
- **연동 파일(LinkedFilePath) 감시 개편** — 창 단위 감시(`NoteWindow.xaml.cs:694-794`)는 그대로 둔다. 볼트 감시는 별도 중앙 서비스.

---

## 2. UX 정의

### 2.1 외부 변경 반영 (F1)

| 상황 | 동작 |
|------|------|
| 외부에서 `.md` **수정**, 해당 노트 창에 미저장 편집 없음 | **조용히 반영** (Obsidian 스타일). 에디터 텍스트·제목·미리보기 즉시 교체. 별도 팝업/배지 없음 — 스티키노트는 항상 화면에 떠 있으므로 내용이 바뀌는 것 자체가 피드백이다. Serilog 정보 로그 1줄. |
| 외부에서 수정, 해당 노트 창에 **미저장 편집 있음** | 그 창을 owner 로 Yes/No 프롬프트(기존 리소스 재사용): `Strings.Note_FileChangedPrompt` / `Strings.Note_FileChangedTitle`, 기본 버튼 **No**(내 편집 유지). Yes → 파일 내용으로 교체, No → 내 편집 유지(다음 디바운스 저장이 파일을 다시 덮어씀 = 단일 작성자 복귀). `NoteWindow.ReloadLinkedFileAsync`(NoteWindow.xaml.cs:725-761)와 동일한 문구·동일한 기본값. |
| 외부에서 `.md` **추가** (1~3개) | 새 노트 창을 만들되 **Show 하지 않는다**(깜짝 팝업 방지). 트레이 풍선 알림 1건: "볼트에 새 노트 N개가 추가되었습니다". 노트 목록 창이 열려 있으면 목록 갱신. |
| 외부에서 `.md` **대량 추가** (한 디바운스 배치에 4개 이상 — 클라우드 초기 동기화 등) | 위와 동일하되 알림 문구만 개수 요약. 창 폭탄 금지(App.xaml.cs:197 의 `MaxFilesToOpen` 과 같은 취지). |
| 외부에서 `.md` **삭제**, 창에 미저장 편집 없음 | 해당 노트 창을 조용히 닫고 캐시·창 목록에서 제거(휴지통 아님 — 파일이 곧 원본). |
| 외부에서 삭제, 창에 **미저장 편집 있음** | 프롬프트: "이 노트의 파일이 볼트에서 삭제되었습니다. 지금 편집 중인 내용을 새 파일로 다시 저장할까요?" — **Yes(기본)**: 유지, 다음 저장이 파일 재생성. No: 창 닫고 제거. |
| 외부에서 **이름 변경** (frontmatter id 동일) | id 기준으로 같은 노트로 인식 — 창 유지, 파일명 매핑만 갱신. 제목이 frontmatter 에서 바뀌었으면 제목 반영. |
| 노트 목록 창(`NotesListWindow`)이 열려 있는 동안 반영 발생 | `WindowManager.DeleteAsync`(WindowManager.cs:322-325)와 같은 패턴으로 `ShowAndReloadAsync()` 재호출. |

프롬프트는 노트 창당 동시에 1개만(`_reloadPromptOpen` 패턴, NoteWindow.xaml.cs:51). 프롬프트가 떠 있는 동안 도착한 후속 변경은 다음 디바운스 사이클에서 처리.

### 2.2 충돌 사본 안내 (F2)

- **알림 형태**: 트레이 풍선 알림(H.NotifyIcon `TaskbarIcon.ShowNotification`, best-effort — 실패 시 로그만).
  - 제목: `볼트 충돌 사본 발견` (`Strings.Vault_ConflictTitle`)
  - 본문: `"{파일명}" — 클라우드 동기화 충돌로 보입니다. 내용을 확인한 뒤 정리하세요.` (`Strings.Vault_ConflictBodyFormat`)
  - 여러 건이면 1건으로 묶어 "충돌 사본 {N}개 발견: {첫 파일명} 외" 요약.
- **알림 시점**: (a) 앱 시작 직후 최초 스캔, (b) 감시자 Created/Renamed 이벤트로 새 충돌 사본이 나타났을 때.
- **세션 내 중복 알림 금지**: 알림한 파일명 집합(대소문자 무시)을 세션 동안 기억.
- **충돌 사본 파일 자체의 취급**: 일반 `.md` 노트로 로드는 되지만(사용자가 내용을 봐야 하므로), **원본 노트의 id 를 가로채지 않는다**(§3.6). 자동 삭제/병합 없음.

### 2.3 외부 앱으로 볼트 열기 (F3)

트레이 볼트 메뉴(`TrayService.BuildVaultMenu`, TrayService.cs:148-162) 확장 — 볼트 모드(`StorageMode == "vault"` && `VaultPath` 유효)일 때만 아래 항목 표시:

| 메뉴 | 표시 조건 | 동작 |
|------|-----------|------|
| 폴더 열기(탐색기) | 볼트 모드면 항상 | `explorer.exe "<VaultPath>"` |
| Obsidian에서 열기 | `obsidian://` URL 프로토콜이 레지스트리에 등록된 경우만 표시(미설치 → **숨김**) | `obsidian://open?path=<EscapeDataString(VaultPath)>` |
| VS Code에서 열기 | Code 실행 파일 발견 또는 `vscode://` 프로토콜 등록 시 표시(미설치 → **숨김**) | Code.exe 직접 실행 `"<code>" "<VaultPath>"` 우선, 없으면 `vscode://file/<경로>` |

- 정책은 **숨김**(비활성 아님) — 미설치 앱 메뉴가 회색으로 남아 지저분해지는 것보다 깔끔. 설치 감지는 메뉴 `Opened` 이벤트에서 매번 재평가(이미 `_autoStartMenu` 갱신에 쓰는 패턴, TrayService.cs:115-121)하므로 앱 재시작 없이도 설치/제거가 반영된다.
- LiteDB 모드에서는 기존 "볼트로 내보내기/가져오기"만 보인다(현행 유지).

---

## 3. 기술 설계

### 3.1 신규 구성요소 개요

```
Services/IVaultWatcher.cs        // 인터페이스 (Start/Dispose + 테스트용 진입점)
Services/VaultWatcherService.cs  // FSW + 디바운스 + 에코 억제 + diff 적용 오케스트레이션
Utils/ConflictCopyDetector.cs    // 순수 함수: 파일명 → 충돌 사본 여부 (static)
Utils/ExternalAppLocator.cs      // 순수(레지스트리/파일 존재) 감지: Obsidian/VS Code
```

수정 파일: `App.xaml.cs`(DI·수명), `WindowManager.cs`(+`IWindowManager`) — diff 적용 API,
`NoteWindow.xaml.cs` — 외부 갱신 적용 메서드, `VaultRepository.cs` — 스냅샷/diff 지원,
`VaultStore.cs` — id 중복 정책 + 자기쓰기 원장, `TrayService.cs`(+`ITrayService`) — 메뉴·알림,
`Resources/Strings.resx`·`Strings.ko.resx` — 신규 문자열.

### 3.2 감시 서비스 수명주기

- **등록**: `App.xaml.cs` DI 블록(App.xaml.cs:92-123)에 `services.AddSingleton<IVaultWatcher, VaultWatcherService>()`.
- **기동**: `OnStartup` 에서 `RestoreAllAsync()`·`TrayService.Initialize()` 이후(App.xaml.cs:146-148 부근):

  ```csharp
  if (_host.Services.GetRequiredService<INoteRepository>() is VaultRepository)
      _host.Services.GetRequiredService<IVaultWatcher>().Start(settings.VaultPath!);
  ```

  볼트 모드가 아니거나 `VaultRepository` 초기화가 LiteDB 로 폴백(App.xaml.cs:105-110)했으면 기동하지 않는다.
- **기동 직후 1회 정합**: `Start()` 는 FSW 활성화 **후** 즉시 리로드 사이클 1회를 실행한다 — 생성자 `Load()`(앱 시작) 시점과 감시 시작 시점 사이에 낀 외부 변경을 회수(레이스 봉합). 이 첫 사이클이 §2.2 의 "시작 직후 충돌 사본 최초 스캔"도 겸한다.
- **종료**: `OnExit` 의 정리 순서에서 트레이보다 먼저 `GetService<IVaultWatcher>()?.Dispose()` (App.xaml.cs:216-225). Dispose 는 FSW 이벤트 해제 → 디바운서 정지 → 진행 중 리로드 완료 대기 없이 반환(리로드는 `_gate` 로 저장과 직렬화되므로 안전).

### 3.3 이벤트 흐름과 디바운스

```
FSW(*.md, 하위 폴더 제외) ──Created/Changed/Deleted/Renamed──▶ 이벤트 큐(스레드풀)
        │  NotifyFilter = LastWrite | FileName | Size   (NoteWindow.xaml.cs:705 와 동일)
        ▼
  경로 누적(ConcurrentDictionary<string, byte>) + DebounceAction(700ms) Trigger
        ▼  (조용해진 뒤 1회)
  ① 에코 판정: 배치의 모든 경로가 자기쓰기 원장에 매칭 → 전체 스킵
  ② 충돌 사본 검사: 배치 경로 중 ConflictCopyDetector 매치 → 트레이 알림(중복 억제)
  ③ 스냅샷 diff 리로드: VaultRepository.ReloadWithDiffAsync()
        ▼
  ④ UI 적용: Dispatcher.InvokeAsync(() => WindowManager.ApplyVaultDiffAsync(diff))
```

- **디바운스 700ms**, `Utils/DebounceAction`(기존, DebounceAction.cs:8) 재사용. 클라우드 동기화는 파일당 여러 이벤트(쓰기+속성)를 쏟아내므로 경로는 집합으로 누적하고 리로드는 배치당 1회만.
- FSW `Error` 이벤트(버퍼 오버플로): 경로 집합에 특수 마커를 넣어 "전체 리로드 + 에코 판정 생략"으로 처리. `InternalBufferSize = 64KB` 로 상향.
- 리로드는 폴더 전체 재적재(`VaultStore.Load`)라 개별 경로를 신뢰할 필요가 없다 — 이벤트 경로는 **에코 판정·충돌 감지에만** 쓰고, 반영 내용은 항상 스냅샷 diff 로 계산한다(이벤트 유실에 강함).

### 3.4 에코 억제 — 자기쓰기 원장(Self-Write Ledger)

에코 소음원 2가지: ① `VaultStore.SaveOne` 은 항상 `.md` 를 다시 쓴다(VaultStore.cs:71-78) — 창 이동/리사이즈만 해도 디바운스 저장이 파일을 건드린다. ② SaveOne 이 인덱스도 다시 쓰지만 인덱스는 FSW 필터(`*.md`)에 걸리지 않으므로 무시된다.

- **원장 자료구조**: `VaultStore` 에 `ConcurrentDictionary<string /*파일명 소문자*/, DateTime /*기록 시각 UTC*/>`.
  `SaveOne`/`Save` 는 파일 쓰기 직후, `DeleteFile` 은 삭제 직후 원장에 기록한다.
- **판정 규칙**: 이벤트 경로의 파일명이 원장에 있고 `now - 기록시각 <= 3초` 면 에코. 배치의 **모든** 경로가 에코면 사이클 전체 스킵, 하나라도 아니면 정상 진행(정확성은 ③의 diff 가 보증 — 아래).
- **정확성 백스톱**: 에코 판정이 틀려서 리로드가 돌아도, 캐시 갱신(`_notes[...] = note`)과 파일 쓰기(`_store.SaveOne`)가 같은 `_gate` 임계구역 안에서 일어나므로(VaultRepository.cs:66-82) 리로드 결과는 캐시와 동일 → diff 가 공집합 → 창을 건드리지 않는 무해한 no-op. **즉 에코 억제는 IO 절약용 최적화이고, 오동작(잘못된 프롬프트·덮어쓰기)은 diff 무결성이 구조적으로 막는다.** 해시 비교까지는 불필요(과설계)하다.
- 원장 항목은 판정 시 3초 초과분을 게으르게 청소(별도 타이머 불요).

### 3.5 스냅샷 diff — `VaultRepository.ReloadWithDiffAsync()`

기존 `ReloadAsync()`(VaultRepository.cs:27-36)를 유지하되, 감시자용으로 diff 를 돌려주는 변형을 추가한다.

```csharp
public sealed record VaultDiff(
    IReadOnlyList<Note> Added,                    // 새 id
    IReadOnlyList<Guid> Removed,                  // 사라진 id
    IReadOnlyList<(Note Fresh, string OldContent)> Changed); // 내용/제목/포맷 변경

public async Task<VaultDiff> ReloadWithDiffAsync()
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        var old = _notes.Values.ToDictionary(n => n.Id, n => (n.Content, n.Title, n.Format));
        _notes.Clear();
        foreach (var n in _store.Load()) _notes[n.Id] = n;
        // old vs _notes 비교로 Added/Removed/Changed 계산 (Ordinal 비교)
    }
    finally { _gate.Release(); }
}
```

- **Changed 판정**: `Content`(Ordinal) 또는 `Title` 또는 `Format` 이 다를 때. `ModifiedAt` 은 판정에 쓰지 않는다(우리 쓰기마다 갱신되는 값이라 소음).
- **`OldContent` 를 함께 반환하는 이유**: 창의 "미저장 편집" 판정에 필요(§3.7). diff 계산과 캐시 교체가 같은 임계구역이므로 스냅샷이 원자적이다.
- 위치·색상 등 표시 상태는 사이드카에서 오므로 외부 `.md` 편집으로는 변하지 않는다 — diff 는 내용 축만 본다. 휴지통 플래그도 사이드카 소관이므로 diff 대상 아님.

### 3.6 id 중복 정책 — 충돌 사본이 원본을 가리지 않게 (기존 위험 수정)

현행 `VaultStore.Load` 는 `_files[note.Id] = 파일명` / `result.Add` 를 열거 순서대로 덮어써서(VaultStore.cs:48-50, 그리고 `VaultRepository` 생성자·`ReloadAsync` 의 `_notes[n.Id] = n`), **frontmatter id 가 같은 두 파일**(= 충돌 사본의 전형)이 있으면 알파벳순 나중 파일이 원본을 가린다. 예: `쇼핑목록 (conflicted copy).md` 가 `쇼핑목록.md` 을 캐시에서 밀어내고, 이후 SaveOne 이 **충돌 사본 파일에** 기록되는 사고 경로.

**정책** (Load 내부에서 해결, 파일은 절대 다시 쓰지 않는 읽기 전용 패스):

1. 같은 id 의 두 번째 파일을 만나면, **사이드카 인덱스의 `File` 항목과 파일명이 일치하는 쪽**을 정본으로 유지.
2. 인덱스로 판정 불가(양쪽 다 불일치 등)면 `ConflictCopyDetector` 비매치 쪽 → 그래도 모호하면 먼저 읽힌 쪽을 정본으로.
3. 밀려난 파일은 **새 Guid 를 부여해 별도 노트로 로드**(제목 그대로 → 사용자가 목록에서 "○○ (충돌 사본)"으로 식별). 새 id 는 인메모리 전용 — 파일을 다시 쓰지 않으므로 리로드마다 id 가 달라질 수 있는데, 사용자가 그 노트를 **편집하는 순간** SaveOne 경로로 frontmatter 에 새 id 가 영속된다. 허용 가능한 트레이드오프(충돌 사본은 확인 후 정리가 전제).

### 3.7 diff 의 UI 적용 — `WindowManager.ApplyVaultDiffAsync(VaultDiff)`

`ReloadAsync()`(WindowManager.cs:52-58, 전 창 폐기 후 재구성)는 백업 가져오기용으로 남기고, 세밀 갱신 메서드를 신설한다. **UI 스레드에서만 호출**(감시자가 `Dispatcher.InvokeAsync` 로 넘김).

- **Changed**: `_windows` 에서 `w.ViewModel.Id == fresh.Id` 인 창을 찾는다(모든 노트는 시작 시 창이 만들어지므로 대부분 존재).
  - 창의 현재 내용(`vm.Content`)이 `OldContent` 와 같으면 → 미저장 편집 없음 → `window.ApplyExternalContent(fresh)` 조용히 적용.
  - 다르면 → 미저장 편집 있음 → §2.1 프롬프트. Yes 일 때만 적용.
  - 적용 = `ReplaceEditorText` + `vm.UpdateContent(fresh.Content, fresh.Format)` + 미리보기 재렌더 — `NoteWindow.ReloadLinkedFileAsync`(NoteWindow.xaml.cs:757-760)의 몸통을 `internal void ApplyExternalContent(string text, NoteContentFormat fmt)` 로 추출해 두 경로가 공유한다. WYSIWYG 편집 중(`_wysiwygOn`)이면 "미저장 편집 있음"으로 취급해 무조건 프롬프트(에디터 내부 상태를 임의 교체하지 않는다).
  - **주의**: 적용 직후 VM 의 디바운스 저장이 같은 내용을 다시 파일에 쓰지 않도록, `UpdateContent` 후 저장 스케줄을 유발하지 않는 경로가 필요하다. `ApplyExternalContent` 는 마지막에 `vm.MarkSynced(fresh.Content)` 를 부르고, `NoteViewModel.SaveAsync` 는 볼트 모드에서 내용이 캐시와 동일하면 어차피 같은 값을 덮어쓸 뿐이라 기능상 무해 — 다만 불필요한 SaveOne(=새 에코) 1회를 막기 위해 `_suppressEditorSync` 로 에디터 교체 중 `UpdateContent` 재진입을 차단하는 기존 방식(NoteWindow.xaml.cs:763-778)을 그대로 쓰고, VM 갱신은 hydrate 성격의 전용 메서드(`vm.HydrateExternal(content, format)`: `_hydrating` 토글로 Schedule 억제, NoteViewModel.cs:25·134-142 참고)로 한다.
- **Added**: `BuildWindow(note)` 로 창만 만들고 `Show()` 하지 않는다(§2.1 정책). 배치 크기와 무관하게 트레이 알림 1건.
- **Removed**: 해당 창에 미저장 편집 없으면 `vm.Dispose()` 대상 표시 후 `RequestClose()`, `_windows` 에서 제거. 미저장 편집 있으면 §2.1 프롬프트(Yes=유지: 다음 저장이 파일 재생성 — `SaveContentAsync` 는 캐시에 없는 id 를 무시하므로(VaultRepository.cs:76) **유지 선택 시에는 `UpsertAsync` 로 캐시에 재등록**해야 한다).
  - 창을 닫을 때 늦은 flush(`OnClosing` → `FlushAsync`, NoteWindow.xaml.cs:796-812)가 파일을 되살리지 않는 것은 `SaveContentAsync` 의 "없는 id 무시" 규약이 이미 보장한다.
- 마지막에 노트 목록 창 갱신(§2.1) 및 Serilog 요약 로그 1줄(`Vault sync: +{a} ~{c} -{r}`).

### 3.8 충돌 사본 감지기 — `ConflictCopyDetector`

순수 static 함수 `bool IsConflictCopy(string fileName)` + `string Describe(...)`. 패턴(대소문자 무시):

| 출처 | 패턴(파일명, 확장자 제외 기준) | 예 |
|------|------|-----|
| Dropbox (EN) | `\(.+ conflicted copy( \d{4}-\d{2}-\d{2})?( \(\d+\))?\)$` | `notes (DESKTOP's conflicted copy 2026-07-01).md` |
| Dropbox (KO) | `\(충돌[^)]*사본[^)]*\)$` | `노트 (충돌이 발생한 사본 2026-07-01).md`, `이름 (충돌 사본).md` |
| OneDrive | `-<Environment.MachineName>(-\d+)?$` **이고** 접미사 제거한 이름의 `.md` 가 볼트에 실존할 때만 | `todo-DESKTOP-ABC123.md` (`todo.md` 존재 시) |
| Syncthing | `\.sync-conflict-\d{8}-\d{6}(-\w+)?` 포함 | `plan.sync-conflict-20260701-093012-ABCDEF.md` |

- OneDrive 패턴은 하이픈 파일명 오탐이 잦으므로 **기계명 일치 + 원본 실존** 이중 조건. 나머지는 파일명만으로 판정(원본이 이미 정리됐을 수도 있으므로).
- 시그니처를 `IsConflictCopy(string fileName, Func<string,bool> siblingExists)` 로 두어 파일시스템 없이 단위 테스트 가능하게 한다.

### 3.9 외부 앱 감지·실행 — `ExternalAppLocator`

- **Obsidian**: `Registry.ClassesRoot` 또는 `HKCU\Software\Classes` 에서 `obsidian` 키의 `URL Protocol` 값 존재 여부(읽기 전용, 예외는 false). 실행: `Process.Start(new ProcessStartInfo($"obsidian://open?path={Uri.EscapeDataString(vaultPath)}") { UseShellExecute = true })` — `NoteWindow.OpenExternal`(NoteWindow.xaml.cs:637-641) 패턴. 볼트가 Obsidian 에 등록 안 된 폴더면 Obsidian 이 자체 안내를 띄운다(우리 소관 아님).
- **VS Code**: 탐색 순서 ① `%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe` ② `%ProgramFiles%\Microsoft VS Code\Code.exe` ③ PATH 의 `code.cmd`(`where` 없이 PATH 분할 검색) ④ `vscode://` 프로토콜 등록. ①~③이면 `Process.Start(codePath, $"\"{vaultPath}\"")`(폴더 인자), ④면 `vscode://file/{경로}` URI. 슬래시 정규화(`\`→`/`) 및 인용에 주의 — 경로는 사용자가 고른 볼트 경로 그대로이며 셸 문자열 연결을 하지 않는다(인자 배열 사용).
- 탐색기: `Process.Start("explorer.exe", $"\"{vaultPath}\"")`. 폴더가 사라진 경우(클라우드 재배치 등) `Directory.Exists` 선확인 후 경고 MessageBox.
- 감지 함수는 트레이 메뉴 `Opened` 시마다 호출되므로 가볍게(레지스트리 1~2회, 파일 존재 3회 이내) 유지.

### 3.10 트레이 알림 — `ITrayService.ShowNotification`

`ITrayService` 에 `void ShowNotification(string title, string message)` 추가. 구현은 `_icon?.ShowNotification(title, message, NotificationIcon.Info)`(H.NotifyIcon 2.1.4) 를 try/catch 로 감싼 best-effort — 트레이 초기화 실패 시(TrayService.cs:61-64 경로) 로그만. 감시 서비스는 `ITrayService` 를 직접 참조하지 않고 `Action<string,string>` 알림 콜백을 주입받는다(App.xaml.cs 조립 시 연결) — 순환 의존 방지.

### 3.11 스레딩 요약

| 구간 | 스레드 |
|------|--------|
| FSW 콜백 → 경로 누적 → DebounceAction | 스레드풀 (lock-free 집합 누적) |
| 에코 판정·충돌 감지·`ReloadWithDiffAsync` | 스레드풀 (`_gate` 로 저장 경로와 직렬화) |
| `ApplyVaultDiffAsync`(창 조작·프롬프트) | `Application.Current.Dispatcher.InvokeAsync` |
| 트레이 알림 | Dispatcher (H.NotifyIcon 요구) |

재진입 방어: 감시 사이클은 `SemaphoreSlim(1,1)` 로 동시에 1개만. 사이클 중 새 이벤트가 오면 경로 집합에 누적되어 다음 디바운스로 넘어간다(놓침 없음).

### 3.12 신규 리소스 문자열 (en/ko)

`Strings.resx` / `Strings.ko.resx`:

| 키 | ko 예시 |
|----|---------|
| `Vault_OpenInExplorer` | 폴더 열기(탐색기) |
| `Vault_OpenInObsidian` | Obsidian에서 열기 |
| `Vault_OpenInVsCode` | VS Code에서 열기 |
| `Vault_ConflictTitle` | 볼트 충돌 사본 발견 |
| `Vault_ConflictBodyFormat` | "{0}" — 클라우드 동기화 충돌로 보입니다. 내용을 확인한 뒤 정리하세요. |
| `Vault_ConflictManyFormat` | 충돌 사본 {0}개 발견: {1} 외 |
| `Vault_NotesAddedFormat` | 볼트에 새 노트 {0}개가 추가되었습니다. |
| `Vault_FileDeletedPrompt` | 이 노트의 파일이 볼트에서 삭제되었습니다. 편집 중인 내용을 새 파일로 다시 저장할까요? |
| `Vault_FolderMissingFormat` | 볼트 폴더를 찾을 수 없습니다: {0} |

외부 수정 프롬프트는 기존 `Note_FileChangedPrompt`/`Note_FileChangedTitle` 재사용.

---

## 4. 작업 분해 + 위험도 분류

| # | 작업 | 파일 | 위험도 | 근거 |
|---|------|------|--------|------|
| T1 | `VaultStore` id 중복 정책 + 자기쓰기 원장 | `VaultStore.cs` | **고위험 (Opus 직접)** | 저장 엔진의 로드 의미론 변경 — 실수 시 원본 노트가 충돌 사본 파일로 저장되는 데이터 사고 경로. 기존 107개 테스트와의 상호작용 검증 필요. |
| T2 | `VaultRepository.ReloadWithDiffAsync` + `VaultDiff` | `VaultRepository.cs` | **고위험 (Opus 직접)** | `_gate` 임계구역 내 원자적 스냅샷·diff — 동시성 실수 시 저장 유실/유령 프롬프트. |
| T3 | `VaultWatcherService`(FSW·디바운스·에코 억제·사이클 직렬화) | 신규 | **고위험 (Opus 직접)** | 스레딩(스레드풀↔디스패처)·이벤트 폭주·버퍼 오버플로·수명주기 — 이 조합의 기술적 핵심. |
| T4 | `WindowManager.ApplyVaultDiffAsync` + `NoteWindow.ApplyExternalContent`/`NoteViewModel.HydrateExternal` | `WindowManager.cs`, `NoteWindow.xaml.cs`, `NoteViewModel.cs` | **고위험 (Opus 직접)** | 열린 창·미저장 편집·WYSIWYG·삭제 프롬프트의 상태기계. 잘못되면 사용자 편집 내용 소실. |
| T5 | `ConflictCopyDetector` + 단위 테스트 | 신규 `Utils/` | **저위험 (Sonnet 위임)** | 순수 함수 + 정규식 — 파일시스템·UI 무관, 테스트로 완결. |
| T6 | `ExternalAppLocator` + 트레이 메뉴 3종 | 신규 `Utils/`, `TrayService.cs` | **저위험 (Sonnet 위임)** | 읽기 전용 감지 + Process.Start — 실패해도 메뉴 숨김/경고로 국소화. 기존 `BuildVaultMenu`·`Opened` 패턴 답습. |
| T7 | `ITrayService.ShowNotification` + 알림 배선 | `TrayService.cs`, `ITrayService.cs` | **저위험 (Sonnet 위임)** | best-effort 1메서드 추가. |
| T8 | 리소스 문자열(en/ko) 추가 | `Strings.resx`, `Strings.ko.resx`, `Strings.cs` | **저위험 (Sonnet 위임)** | 기계적 추가. |
| T9 | DI 등록·기동/종료 배선 | `App.xaml.cs` | **고위험 (Opus 직접)** | 소량이지만 수명주기 순서(Restore→Tray→Watcher 기동, 종료 역순)가 T3 설계와 결합. T3 과 한 몸으로 진행. |
| T10 | 테스트(§5) 중 T1·T2·T3 대상 | `stickypad.Tests/Tests.cs` | **고위험 (Opus 직접)** | 동시성·시나리오 설계가 곧 스펙 검증. |
| T11 | 문서(CHANGELOG, README 볼트 절, 위키 "재시작 시 반영" 문구 교체) | `docs/`, `CHANGELOG.md` | **저위험 (Sonnet 위임)** | 문서 작업. |

권장 순서: T1 → T2 → T5(병렬) → T3+T9 → T4 → T6/T7/T8(병렬) → T10 → T11.

---

## 5. 테스트·검증 계획

### 5.1 단위 테스트 (`stickypad.Tests/Tests.cs`, 기존 107개에 추가)

**VaultStore / VaultRepository (T1·T2):**
1. id 중복: 인덱스가 가리키는 파일이 정본으로 로드되고, 다른 파일은 새 Guid 로 별도 노트가 된다(파일 내용 불변 확인 — 읽기 전용 패스).
2. id 중복 + 인덱스 판정 불가: 충돌 패턴 파일이 밀려나는 쪽이 된다.
3. `ReloadWithDiffAsync`: 외부 파일 추가 → Added / 삭제 → Removed / 내용 수정 → Changed(OldContent 정확) / 무변경 → 3집합 모두 공집합.
4. diff 원자성: `ReloadWithDiffAsync` 와 `SaveContentAsync` 동시 호출 반복(Task.WhenAll 루프) — 예외·유실 없음.
5. SaveOne 직후 리로드 → diff 공집합(에코 백스톱 규약).
6. 자기쓰기 원장: SaveOne/DeleteFile 직후 해당 파일명 조회는 true, 3초 경과(시각 주입) 후 false.

**ConflictCopyDetector (T5):** Dropbox EN/KO·OneDrive(기계명 일치/불일치·원본 유무)·Syncthing·정상 파일명(하이픈 포함) 오탐 없음 — 표 기반 12+ 케이스.

**ExternalAppLocator (T6):** 경로 후보 탐색 순서·미존재 시 null — 파일시스템 루트를 주입 가능하게 해 테스트(레지스트리 의존 분기는 수동 검증으로 위임).

**VaultWatcherService (T3):** FSW 실이벤트 테스트는 타이밍 취약 — 코어 로직(경로 누적→에코 판정→사이클 실행)을 `internal Task ProcessBatchForTestAsync(IEnumerable<string>)` 로 노출해 결정적으로 검증: 전량 에코 배치 → 리로드 미호출 / 혼합 배치 → 1회 호출 / 사이클 중 재진입 → 직렬화. 실제 FSW 결합은 임시 폴더 + 넉넉한 폴링 대기(5초) 스모크 1개만.

### 5.2 수동 하네스 시나리오 (릴리즈 전 체크리스트 — 임시 볼트 폴더로 수행, 실사용 볼트 금지)

| # | 시나리오 | 기대 |
|---|----------|------|
| H1 | 실행 중 메모장으로 볼트의 `.md` 수정·저장 | ~1초 내 해당 노트 창 내용 교체, 팝업 없음 |
| H2 | 노트 창에 타이핑 직후(0.5초 내) 외부 수정 | 프롬프트 표시, No → 내 편집 유지·재저장, Yes → 파일 내용 |
| H3 | 볼트에 새 `.md` 5개 복사 | 창 안 뜸, 트레이 알림 "5개", 목록에 5개 표시 |
| H4 | 열린 노트의 `.md` 외부 삭제 | 창 자동 닫힘(편집 중이면 프롬프트) |
| H5 | `이름 (충돌 사본).md` 생성(원본과 같은 frontmatter id) | 트레이 충돌 알림 1회, 원본 노트 창 정상 유지, 목록에 사본 별도 표시 |
| H6 | 앱에서 노트 이동/리사이즈/타이핑 연타 | 트레이 알림·프롬프트·창 깜빡임 전무(에코 무시), 로그에 sync 사이클 스팸 없음 |
| H7 | Obsidian 으로 같은 볼트 열고 양쪽 교차 편집 | 마지막 저장 우선으로 수렴, 프롬프트는 미저장 편집 시에만 |
| H8 | 트레이 메뉴: 탐색기/Obsidian/VS Code 열기 (설치·미설치 각각) | 설치 시 열림, 미설치 시 항목 숨김; LiteDB 모드에선 3항목 모두 비표시 |
| H9 | 앱 재시작(감시 없던 동안 파일 추가/삭제) | 시작 직후 1회 정합 사이클로 반영 |
| H10 | 단일 파일 배포 산출물(`dotnet publish` 결과)로 H1·H5·H8 재확인 | Debug 와 동일 동작 |

H10 은 이 저장소의 기존 사고 이력(단일 파일 배포에서 자산 누락, `1a9dd0d`) 재발 방지 관례. 수동 검증 시 실행 중인 실사용 인스턴스를 종료하거나 라이브 볼트를 건드리지 말 것(임시 폴더 + 별도 settings 환경).

---

## 6. 예상 규모와 릴리즈 버전 제안

| 항목 | 추정 |
|------|------|
| 신규 파일 | 4개 (`IVaultWatcher`, `VaultWatcherService`, `ConflictCopyDetector`, `ExternalAppLocator`) ≈ 450~550 LOC |
| 수정 파일 | 8개 (`VaultStore`, `VaultRepository`, `WindowManager`+`IWindowManager`, `NoteWindow.xaml.cs`, `NoteViewModel`, `TrayService`+`ITrayService`, `App.xaml.cs`, `Strings.*`) ≈ 250~350 LOC |
| 테스트 | +25~30개 (107 → 130대) |
| 작업량 체감 | 고위험 축(T1~T4·T9·T10)이 전체의 70% — 병렬화 여지는 저위험 축(T5~T8·T11)에 한정 |

**버전: v2.3.0** — 사용자 가시 기능 3종 추가(하위 호환, 스키마 변경 없음)로 minor 상향이 적절.
`.csproj` `Version/AssemblyVersion/FileVersion`(stickypad.csproj:12-14)과 InnoSetup `AppVersion` 동기화,
릴리즈 전 README·CHANGELOG·위키의 "외부 변경은 재시작 시 반영" 문구 갱신을 선행한다.
