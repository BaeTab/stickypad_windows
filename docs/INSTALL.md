# 설치 및 무결성 확인 (Install & verify)

## 설치

[**Releases**](https://github.com/BaeTab/stickypad_windows/releases) 페이지에서 최신 `StickyPad-vX.Y.Z-win-x64.exe` 를 받아 실행하세요. 자체 포함(self-contained) 빌드라 별도 .NET 런타임 설치가 필요 없습니다.

## Windows SmartScreen 경고

### 왜 뜨나요

StickyPad는 (유료) 코드서명 인증서를 아직 쓰지 않는 오픈소스 앱입니다. 그래서 실행 파일을 내려받아 처음 실행하면 Windows가 "Windows에서 PC를 보호했습니다" / "알 수 없는 게시자" 경고를 띄울 수 있습니다. **이것은 바이러스라는 뜻이 아닙니다** — 배포자가 서명 인증서를 구매하지 않았다는 뜻일 뿐입니다.

### 안전한 이유

- **오픈소스** — 소스코드가 전부 공개되어 있어 무엇이 실행되는지 누구나 확인할 수 있습니다.
- **로컬 우선** — 계정도, 클라우드 전송도, 사용 분석(텔레메트리) 수집도 없습니다. 노트는 내 PC의 `%LocalAppData%\StickyPad\` 에만 저장됩니다.
- **검증된 자동 업데이트** — 자동 업데이트는 받은 파일을 게시된 **SHA‑256 체크섬**과 대조한 뒤 일치할 때만 적용합니다(불일치·누락 시 적용을 거부).

### 진행 방법

SmartScreen 창이 뜨면:

1. **"추가 정보(More info)"** 를 클릭합니다.
2. **"실행(Run anyway)"** 버튼을 클릭합니다.

## 다운로드 무결성 직접 확인

각 릴리즈에는 실행 파일과 함께 `...exe.sha256` 자산이 게시됩니다. 받은 exe가 손상되거나 변조되지 않았는지 직접 확인하려면, 파일의 해시를 계산해 게시된 값과 비교하세요.

**PowerShell**
```powershell
Get-FileHash .\StickyPad-vX.Y.Z-win-x64.exe -Algorithm SHA256
```

**cmd**
```cmd
certutil -hashfile StickyPad-vX.Y.Z-win-x64.exe SHA256
```

출력된 해시 값이 같은 릴리즈의 `.sha256` 파일에 적힌 값과 **동일하면** 정품이며 손상되지 않은 파일입니다.

---

## English (summary)

StickyPad isn't code-signed with a (paid) certificate yet, so Windows SmartScreen may flag the downloaded exe as coming from an "unknown publisher" on first run. **This does not mean it's malware** — it just means the publisher hasn't purchased a signing certificate. StickyPad is open source, local-first (no accounts, no cloud upload, no telemetry), and its auto-updater only applies an update after verifying it against a published SHA-256 checksum. To proceed, click **"More info" → "Run anyway"** in the SmartScreen dialog. To verify the download yourself, compare its hash against the published `.sha256` file:

```powershell
Get-FileHash .\StickyPad-vX.Y.Z-win-x64.exe -Algorithm SHA256
```
```cmd
certutil -hashfile StickyPad-vX.Y.Z-win-x64.exe SHA256
```
