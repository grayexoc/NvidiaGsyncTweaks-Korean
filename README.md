# NVIDIA G-SYNC Tweaks — 한국어 소스 빌드

이 저장소는 NVIDIA 디스플레이 드라이버의 숨겨진 VRR/LFC 설정을 조회하고 조정하는 Windows 앱입니다. 이 한국어화 브랜치는 원본 기능 및 레지스트리 제어 로직을 유지하면서 사용자 인터페이스 문자열을 한국어로 번역했습니다.

## 한국어화 범위

- LFC / 프레임 더블링 페이지
- 스무딩 및 깜박임 설정
- VRR 동작 및 모니터 감지
- 커서 및 유휴 상태
- 스마트 감지(실험적)
- 백업, 복원, 관리자 권한 확인 및 오류 메시지
- 설정 설명, 기본값, 상태 표시 및 위험도 표시

NVIDIA, G-SYNC, VRR, LFC, Adaptive-Sync, DisplayPort, HDMI, DLSS, NVAPI, EDID 등 기술명과 드라이버 레지스트리 값 이름은 원문을 유지했습니다.

## 빌드 환경

원본 프로젝트의 대상 프레임워크는 \`.NET 10\` / Windows 10.0.22621.0이며, WinUI 3와 Microsoft.UI.Reactor를 사용합니다. Windows x64에서 빌드하는 것을 권장합니다.

필요 항목:
- Windows 10/11 x64
- .NET 10 SDK
- Windows App SDK / Windows App Runtime 2.x
- 프로젝트 NuGet 패키지 복원 가능 환경

## 빌드

저장소 루트에서 PowerShell 또는 Developer PowerShell을 열고 실행합니다.

\`\`\`powershell
dotnet restore .\NvidiaGsyncTweaks.csproj
dotnet build .\NvidiaGsyncTweaks.csproj -c Debug
\`\`\`

릴리스용 Native AOT 게시:

\`\`\`powershell
dotnet publish .\NvidiaGsyncTweaks.csproj -c Release -r win-x64
\`\`\`

실행 파일은 \`bin\Release\net10.0-windows10.0.22621.0\win-x64\publish\\\` 아래에 생성됩니다(실제 출력 경로는 SDK 설정에 따라 달라질 수 있습니다).

## 테스트

\`\`\`powershell
dotnet test .\tests\NvidiaGsyncTweaks.Tests\NvidiaGsyncTweaks.Tests.csproj -c Release
\`\`\`

## 안전 및 사용 주의

- 이 앱은 NVIDIA가 공식 문서화하지 않은 드라이버 설정을 변경합니다.
- 설정을 적용하거나 복원할 때 관리자 권한으로 레지스트리에 기록하고 디스플레이 드라이버를 다시 시작합니다.
- 드라이버 재시작 중 화면이 잠시 꺼지거나 깜박이고, 실행 중인 게임·동영상·GPU 프로그램이 종료될 수 있습니다.
- 각 변경 전 복원 지점을 자동 생성합니다. 문제가 발생하면 백업 페이지에서 복원하십시오.
- 잘못된 타이밍 또는 실험적 값을 설정하면 화면 신호가 사라질 수 있습니다. 변경 전 기본값과 설명을 확인하십시오.

## 파일

- \`README-en.md\`: 원본 영문 README
- \`docs/DRIVER-ANALYSIS.md\`: 원본 드라이버 분석
- \`docs/DRIVER-ANALYSIS-ko.md\`: 한국어 분석 안내
- \`docs/DEVELOPING.md\`: 원본 개발 및 릴리스 안내

## 원본 프로젝트

https://github.com/Orbmu2k/NvidiaGsyncTweaks
