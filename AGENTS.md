# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## 프로젝트 개요

**비대칭 협력 멀티플레이어 VR 방탈출 게임** — Meta Quest 3 전용 캡스톤 프로젝트.

- Player A와 Player B는 **같은 VR 방을 공유**하고 서로의 아바타·위치를 볼 수 있음 — 직접 음성 대화 가능
- 비대칭은 **상호작용 권한**으로만 구현: 각 오브젝트는 Player1 또는 Player2 중 한쪽만 조작 가능
- 자기 권한이 아닌 오브젝트는 **시각 단서**로 구분 (예: 색상 차이, 글리치 셰이더 효과) — "이건 내가 못 만진다"는 즉각적 피드백 제공
- 퍼즐은 두 플레이어가 서로 정보·행동을 교환해야만 풀리도록 권한 분리 기반으로 설계
- **Product name:** Capstone (`ProjectSettings/ProjectSettings.asset`)
- **Unity:** 6000.3.11f1 (Unity 6)
- **Target platform:** Android (ARM64), Meta Quest 3
- **Render pipeline:** URP 17.3.0, Mobile/PC 렌더러 분리 (`Assets/Settings/Project Configuration/`)

> **Note (피벗 이력):**
> - 2026-04-28 — MR(Passthrough + Scene API) → 순수 VR로 전환. AR Foundation / Meta-OpenXR Passthrough·Scene 기능은 dependency에 남아있지만 사용하지 않음. 다시 활성화하지 말 것.
> - 2026-05-08 — **분리된 비대칭 환경 → 단일 공유 방으로 전환.** 두 플레이어가 동일한 씬·좌표계의 같은 방에서 플레이하며, 비대칭성은 오브젝트별 인터랙션 권한 + 시각 단서로만 표현. 스테이지별 통신 채널 제약(유선전화/음성/시각/워키토키)도 함께 폐기 — 같은 방이라 직접 대화로 충분.

## 핵심 패키지 (Packages/manifest.json)

XR 스택은 **Unity 공식 OpenXR + Meta-OpenXR** 조합 — 별도의 Meta XR SDK(`com.meta.xr.sdk.*`)는 사용하지 않음.

- `com.unity.xr.openxr` 1.16.1 — OpenXR 백엔드
- `com.unity.xr.meta-openxr` 2.5.0 — Meta Quest용 OpenXR feature
- `com.unity.xr.androidxr-openxr` 1.2.0 — Android XR OpenXR feature
- `com.unity.xr.interaction.toolkit` 3.4.1 — XRI (Ray/Direct Interactor, Locomotion 등)
- `com.unity.xr.hands` 1.7.3 — Hand Tracking
- `com.unity.xr.management` 4.5.4, `com.unity.xr.core-utils` 2.5.3
- `com.unity.xr.compositionlayers` 2.4.0
- `com.unity.inputsystem` 1.19.0 — New Input System
- `com.unity.render-pipelines.universal` 17.3.0 — URP

**Photon (Assets/Photon/ 내 임포트)** — 패키지 매니저가 아닌 Asset 임포트 형태로 포함:
- **Photon Fusion 2** — 네트워크 동기화 (`Assets/Photon/Fusion/`)
- **Photon Voice 2** — 음성 채널 (`Assets/Photon/PhotonVoice/`, Fusion 연동: `PhotonVoice/Code/Fusion/`)
- PhotonRealtime / PhotonChat / PhotonUnityNetworking도 함께 임포트되어 있음

## 프로젝트 구조

```
Assets/
  Scripts/
    Fusion/           # 커스텀 Fusion 네트워크 스크립트 자리 (현재 비어있음)
  Scenes/
    BasicScene.unity
    SampleScene.unity
  Prefab/             # 게임 프리팹
  Photon/             # Photon Fusion 2 / Voice 2 SDK (임포트된 에셋)
  Settings/
    Project Configuration/  # URP Asset (Mobile/PC), Quality 설정
  XR/
    Settings/         # OpenXRPackageSettings.asset, XRSimulationSettings.asset
    Loaders/          # XR Loader 설정
    AndroidXR/        # Android XR feature 설정
  CompositionLayers/  # XR Composition Layer 에셋
  XRI/                # XR Interaction Toolkit 프리셋
  VRTemplateAssets/   # Unity VR 템플릿 잔여 에셋
  Samples/            # XR Hands / XR Interaction Toolkit 샘플
  TextMesh Pro/
ProjectSettings/      # Editor 설정 (직접 편집 금지, Editor UI로 변경)
Packages/             # manifest.json — 패키지 의존성
```

루트의 수많은 `*.csproj`와 `Capstone.slnx`는 Unity가 IDE용으로 자동 생성하므로 직접 편집 금지 (`.gitignore`에서 무시).

## 개발 워크플로우

빌드/테스트/배포는 모두 **Unity Editor**에서 수행 — 별도 CLI 빌드 스크립트 없음.

- **프로젝트 열기:** Unity Hub에서 루트 폴더 열기 (Unity 6000.3.11f1 필요)
- **Quest 빌드:** File > Build Settings > Android > Build / Build and Run (디바이스 USB 연결)
- **XR 설정:** Edit > Project Settings > XR Plug-in Management — Android 탭에서 OpenXR 활성, Feature Set으로 Meta Quest 선택
- **OpenXR 기능 토글:** `Assets/XR/Settings/OpenXRPackageSettings.asset` (Editor의 OpenXR 페이지에서 편집 — 직접 YAML 수정 금지)

## 스크립팅 규칙

- 커스텀 스크립트는 `Assets/Scripts/` 하위에 작성 (네트워크 관련은 `Assets/Scripts/Fusion/`)
- **New Input System** 사용 — `Input.GetKey` 등 레거시 API 사용 금지
- **URP 셰이더만** 사용 — Built-in 렌더 파이프라인 셰이더 사용 금지
- **XR Interaction Toolkit (XRI 3.4)** 의 Interactor/Interactable과 Locomotion 컴포넌트를 우선 활용 — 커스텀 입력 처리 최소화
- **Hand Tracking은 `com.unity.xr.hands`** 사용 (Meta XR SDK가 아닌 Unity 공식 패키지)
- **퍼즐/스테이지는 모듈화** — 추가 콘텐츠 삽입이 용이한 구조 유지
- **네트워크 상태**는 Photon Fusion 2의 `NetworkBehaviour` / `NetworkObject` / `[Networked]` 프로퍼티로 동기화, 일회성 이벤트는 RPC
- **상호작용 권한 분리** — 인터랙터블 오브젝트는 소유자(P1/P2) 태그/필드를 가지며, `XRGrabInteractable` 등의 `selectFilters` 또는 커스텀 게이트로 비소유자의 select·activate를 차단. 비소유자에게는 색상/글리치 셰이더로 시각 차이 표시 (URP 셰이더 그래프 활용)

## VR / XR 핵심 사항

- **VR-only** — Passthrough / Scene Understanding / AR Foundation 카메라 기능은 사용 안 함. AR Foundation 패키지가 dependency에 남아 있어도 카메라 백그라운드 / Scene 앵커는 활성화하지 말 것.
- **Hand Tracking 활성** — XR Hands subsystem 사용
- **Meta-OpenXR feature**에서 활성/비활성 변경 시 모바일 빌드의 매니페스트와 권한이 자동 갱신됨 — Android 매니페스트를 수동으로 추가하지 말고 OpenXR feature 토글로 제어

## 게임 아키텍처 방향 (설계 기준)

```
[Player 1 Device]                          [Player 2 Device]
        \                                         /
         \                                       /
          \─────── 동일한 VR 방 (공유 씬) ───────/
                  · 두 아바타가 같은 좌표계에 존재
                  · 모든 오브젝트는 양쪽 모두 시각적으로 보임
                  · 인터랙션 권한만 P1/P2로 분리 (비권한자에게 글리치/색상 표시)

  ↑↓ Photon Fusion 2: NetworkObject / [Networked] / RPC
  ↑↓ Photon Voice 2: 항시 양방향 (스테이지별 제약 없음)
```

- 두 플레이어는 **하나의 룸/씬**에서 같은 좌표계를 공유 — 별개의 씬 인스턴스를 사용하지 않음
- 퍼즐 오브젝트 상태(위치, 활성화 여부 등)는 Fusion `NetworkObject`로 동기화
- 인터랙터블에는 `OwnerSide` (P1/P2) 메타가 부여되며, 비소유자의 `XRBaseInteractable` select/activate는 게이팅
- 권한 표시는 **셰이더 단**에서 처리 (소유자별 머티리얼 프로퍼티 토글) — 매 프레임 게임오브젝트 활성/비활성 토글 회피
- 협동성은 환경 분리가 아니라 **퍼즐 단계 순서와 정보 비대칭**으로 보장 (예: P1만 보이는 단서 → P2만 조작 가능한 메커니즘)
- Photon Voice 2는 항시 양방향 음성 — 스테이지별 채널 제약은 폐기됨

## 비기능적 목표

- **프레임:** Quest 3 기준 72fps 이상 안정
- **네트워크:** RTT ≤ 100ms, 동기화 손실 < 0.1%
- **VR Comfort:** IPD 조정 지원, 텔레포트/스무스 로코모션 옵션 제공
- **확장성:** 퍼즐/스테이지 모듈화

## UX 화면 흐름

```
앱 실행(Splash) → 메인 메뉴 → 매칭 로비 → 스테이지 시작 → 게임 플레이 → 결과 화면
```

- 메인 메뉴: 서버 생성 / 서버 찾기
- 게임 플레이 HUD: 타이머, 퍼즐 진행률
- 결과 화면: 클리어 시간 표시
