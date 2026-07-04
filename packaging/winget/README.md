# winget 패키지 매니페스트 (BaeTab.StickyPad)

이 디렉터리는 [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) 저장소에
제출할 StickyPad용 winget 매니페스트를 버전별로 보관한다.

```
packaging/winget/
└── 1.7.0/
    ├── BaeTab.StickyPad.yaml               # version manifest
    ├── BaeTab.StickyPad.installer.yaml      # installer manifest
    └── BaeTab.StickyPad.locale.en-US.yaml   # defaultLocale manifest
```

- PackageIdentifier: `BaeTab.StickyPad`
- InstallerType: `portable` (StickyPad는 설치 프로그램 없이 단일 exe로 배포되는
  포터블 앱이므로, winget이 별도 폴더에 배치하고 PATH에 `StickyPad` 명령으로
  연결해주는 portable 타입을 사용한다)
- 스키마 버전: `1.6.0`

이 파일들은 **현재 저장소 안에서만 관리되는 사본**이며, winget-pkgs 저장소에
실제로 제출(PR)하는 작업은 별도로 수행해야 한다. 이 커밋은 매니페스트 준비까지만
포함하고, PR 제출은 하지 않았다.

## 제출 전 체크리스트

- [ ] 해당 버전의 GitHub Release(`v1.7.0`)가 실제로 게시되어 있고, 자산
      (`StickyPad-vX.Y.Z-win-x64.exe`, `.sha256`)이 정상 다운로드되는지 확인
- [ ] `.sha256` 파일 내용과 `InstallerSha256` 값이 일치하는지 재확인
      (release 산출물이 재빌드되면 해시가 바뀌므로 매번 다시 계산해야 한다)
- [ ] `PackageVersion`, `InstallerUrl`, `ReleaseNotesUrl`이 새 버전에 맞게 갱신됐는지 확인

## 방법 A (권장): wingetcreate 사용

`wingetcreate`는 winget 공식 CLI 도구로, 릴리스 URL만 주면 SHA-256 계산과
매니페스트 생성, PR 오픈까지 자동으로 처리해준다.

```powershell
# 최초 1회 설치
winget install wingetcreate

# 신규 패키지 최초 제출 (버전 최초 등록 시)
wingetcreate new https://github.com/BaeTab/stickypad_windows/releases/download/v1.7.0/StickyPad-v1.7.0-win-x64.exe

# 기존 패키지의 새 버전 업데이트 (이후 릴리스마다 이 명령 사용)
wingetcreate update BaeTab.StickyPad `
  --version 1.7.0 `
  --urls https://github.com/BaeTab/stickypad_windows/releases/download/v1.7.0/StickyPad-v1.7.0-win-x64.exe `
  --submit
```

- `--submit` 옵션을 주면 GitHub 로그인(최초 1회 `gh auth` 또는 wingetcreate
  자체 인증) 후 자동으로 fork → 브랜치 생성 → PR 오픈까지 진행된다.
- `wingetcreate`가 URL에서 exe를 내려받아 해시를 직접 계산하므로, 이 저장소에서
  미리 계산해 둔 `.sha256` 값과 대조해 검증하는 용도로만 쓰면 된다.

## 방법 B: 수동 제출

1. [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)를 자신의
   계정으로 fork한다.
2. fork한 저장소에서 아래 경로에 이 디렉터리의 YAML 3종을 그대로 복사한다.

   ```
   manifests/b/BaeTab/StickyPad/1.7.0/
   ├── BaeTab.StickyPad.yaml
   ├── BaeTab.StickyPad.installer.yaml
   └── BaeTab.StickyPad.locale.en-US.yaml
   ```

   (winget-pkgs 규칙상 `manifests/<PackageIdentifier 첫 글자 소문자>/<Publisher>/<PackageName>/<Version>/` 구조를 따른다.)

3. winget CLI로 로컬 검증한다.

   ```powershell
   winget validate --manifest manifests/b/BaeTab/StickyPad/1.7.0
   ```

4. 실제 설치까지 확인하고 싶다면 로컬에서 테스트한다.

   ```powershell
   winget install --manifest manifests/b/BaeTab/StickyPad/1.7.0
   ```

5. 커밋 후 PR을 올린다. (winget에 CLI로 바로 PR을 만드는 기능도 있다: `winget submit`,
   단 이 역시 내부적으로 fork/브랜치/PR 생성을 대행해주는 것이므로 방법 A의
   `wingetcreate --submit`과 거의 동일한 효과다.)

## 주의사항 / 반복 작업 안내

- **이 작업은 릴리스할 때마다 매번 다시 해야 한다.** 새 버전이 나올 때마다
  `packaging/winget/<새버전>/` 디렉터리를 만들고, 새 해시·URL로 매니페스트를
  갱신한 뒤 winget-pkgs에 별도로 제출해야 한다. (winget-pkgs 쪽 버전 디렉터리는
  이전 버전과 별개로 계속 누적되는 구조이며, 이 저장소가 아니라 winget-pkgs
  저장소의 것이 winget 사용자에게 실제로 노출된다.)
- 향후 GitHub Actions 등으로 릴리스 시 `wingetcreate update --submit`을 자동
  실행하도록 CI에 편입하면 이 수작업을 없앨 수 있다. (예: release 워크플로우
  마지막 단계에 wingetcreate 설치 + update 커맨드 추가)
- winget-pkgs 저장소의 PR은 자동 검증 봇(azure pipeline)이 매니페스트 스키마와
  URL 유효성, 바이너리 서명 등을 점검한다. 검증 실패 시 PR 코멘트로 안내되므로
  안내에 따라 매니페스트를 수정하면 된다.
