# 가재코드 폐쇄망 설치 패키지

이 저장소는 Windows 폐쇄망 PC에서 가재코드(GajaeCode)를 설치하기 위한 배포 저장소입니다.

Git 저장소를 Clone할 수 있는 연결만 있으면, Clone 이후에는 외부 패키지 저장소나 인터넷 다운로드 없이 설치하도록 구성되어 있습니다. 대용량 설치 파일은 Git 업로드 제한을 피하기 위해 분할되어 있으며 `install.cmd`가 자동으로 결합하고 무결성을 검사합니다.

## 설치 전 확인

- Windows 10/11 64비트
- 관리자 권한
- Git for Windows 설치 완료
- 서버에서 발급받은 API Key
- 시스템 드라이브 여유 공간 8GB 이상 권장
- WSL2를 처음 활성화하는 PC는 설치 중 재부팅이 필요할 수 있음

API Key는 이 Git 저장소에 저장하지 마십시오. 설치 프로그램 입력창에서만 입력합니다.

## 폐쇄망 PC에서 설치

PowerShell 또는 명령 프롬프트를 열고 다음 명령을 실행합니다.

```powershell
git clone https://github.com/Peace-Min/gajae-code-airgap-installer.git
cd gajae-code-airgap-installer
.\install.cmd
```

`install.cmd`는 다음 작업을 자동으로 수행합니다.

1. `release\parts`의 분할 파일 12개를 하나의 설치 실행 파일로 결합합니다.
2. 결합된 파일의 SHA-256 체크섬을 검증합니다.
3. 검증에 성공하면 관리자 권한으로 설치 프로그램을 실행합니다.
4. 설치 화면에서 서버에서 발급받은 API Key를 입력합니다.
5. 기본 선택된 `WSL2`, `tmux`, `Linux GJC` 항목을 유지한 채 설치를 시작합니다.

설치 중 Windows의 WSL2 기능 활성화가 필요하면 PC가 재부팅될 수 있습니다. 재부팅 후 로그인하면 설치 프로그램이 자동으로 다시 실행되며, 필요한 경우 관리자 권한 승인 창이 다시 표시됩니다.

## 설치 완료 후 실행

바탕 화면에 생성된 다음 바로가기를 사용합니다.

- `GajaeCode tmux.cmd`: 가재코드를 tmux 세션에서 실행
- `GajaeCode diagnostics.cmd`: 설치 상태와 진단 정보를 확인

먼저 `GajaeCode diagnostics.cmd`를 실행해 주요 항목이 정상인지 확인한 다음 `GajaeCode tmux.cmd`를 실행하십시오.

설치 결과 보고서는 바탕 화면의 다음 파일에서 확인할 수 있습니다.

```text
GajaeCode-Installation-Report.html
```

가재코드의 작업 지시 방법, Skill 사용법, tmux 조작법은 [한글 사용 가이드](docs/GAJAE_CODE_GUIDE_KO.html)를 참고하십시오. 폐쇄망 PC에서도 브라우저로 직접 열 수 있는 단일 HTML 파일이며 외부 리소스를 사용하지 않습니다.

## 정상 설치 확인

PowerShell에서 다음 명령으로 기본 상태를 확인할 수 있습니다.

```powershell
wsl -l -v
wsl -d GajaeCode -- bash -lc "gjc --version"
wsl -d GajaeCode -- bash -lc "tmux -V"
```

정상 설치 시 다음 항목을 확인합니다.

- `GajaeCode` WSL 배포판이 존재함
- WSL 버전이 `2`로 표시됨
- `gjc --version`이 버전을 출력함
- `tmux -V`가 버전을 출력함
- 바탕 화면의 `GajaeCode tmux.cmd`가 실행됨
- 내부 모델 서버에 요청을 보내고 응답을 받음

검증용 프로젝트와 최근 검증 결과는 다음 위치에 생성됩니다.

```text
%USERPROFILE%\GajaeCode-Verification
%USERPROFILE%\.gjc\verification\latest.json
```

## 실패 시 복구

설치가 중단되면 먼저 같은 저장소에서 `install.cmd`를 다시 실행하십시오. 이미 완료된 단계는 설치 상태를 기준으로 재사용되며, 실패한 단계의 로그가 남습니다.

진단 자료는 다음 위치에 있습니다.

```text
%LOCALAPPDATA%\GajaeCode\diagnostics\latest-install-state.json
%LOCALAPPDATA%\GajaeCode\diagnostics\install-*.log
%LOCALAPPDATA%\GajaeCode\diagnostics\RECOVERY.md
```

다른 에이전트 세션에서 복구 작업을 이어갈 때는 위 파일과 저장소의 `AGENTS.md`를 함께 확인하도록 지시하십시오. API Key는 로그, HTML 보고서, Git 파일에 기록되지 않습니다.

## 설치 파일을 수동으로 결합하는 방법

자동 실행 없이 설치 파일만 복원하려면 PowerShell에서 다음 명령을 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\assemble-installer.ps1
```

복원된 실행 파일은 다음 위치에 생성됩니다.

```text
release\assembled\GajaeCodeAirgapSetup.exe
```

직접 실행하려면:

```powershell
.\release\assembled\GajaeCodeAirgapSetup.exe
```

## 보안 및 오프라인 정책

- API Key를 저장소, 작업 지시문, 이슈, 로그에 붙여 넣지 않습니다.
- 설치 프로그램은 API Key를 사용자 환경과 WSL 내부 전용 환경 파일에 설정합니다.
- 업데이트 확인, 마켓플레이스, 별점 알림, 웹 검색, 브라우저 연동 등 온라인 기능은 비활성화된 구성으로 배포됩니다.
- 폐쇄망에서 `npm install`, `bun install`, `cargo install` 등 외부 다운로드가 필요한 명령을 임의로 실행하지 않습니다.
- Team 기능은 현재 기본 운영 범위가 아닙니다. 검증 없이 활성화하지 마십시오.

## 배포 파일 정보

- 분할 파일 수: 12개
- 전체 설치 파일 크기: 922,435,609 bytes
- 전체 설치 파일 SHA-256:

```text
90909fd9c3df84e5f8ec3b4b8aaf854d713e4d65195fcf1586a21218432a6224
```

## 유지보수 및 재빌드

이 절은 설치 패키지 유지보수 담당자를 위한 내용입니다. 일반 폐쇄망 사용자는 실행할 필요가 없습니다.

소스 설치 프로그램은 `installer` 디렉터리에 있습니다. Release 빌드와 배포 번들 재생성 전에는 `AGENTS.md`의 보안 규칙과 검증 절차를 반드시 확인하십시오.
