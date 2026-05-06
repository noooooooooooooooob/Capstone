# Pipe Scene — RadiatorA / RadiatorB 셋업 가이드

## ⚡ 가장 빠른 방법: 메뉴 한 번 클릭

`Editor/PipePuzzleSetupTool.cs` 가 자동 셋업을 합니다.

1. Unity Hub에서 Capstone 프로젝트를 열어 컴파일이 끝나길 기다립니다.
2. `Pipe Scene.unity` 를 엽니다.
3. 상단 메뉴 → **Tools / Capstone / Setup Pipe Puzzle (RadiatorA & B)** 클릭.
4. Console에 "셋업 완료" 로그가 뜨고 Hierarchy에 다음이 자동 추가/수정됩니다:
   - `VirtualWall`, `MirrorController` (루트)
   - RadiatorA의 Valve에 `RadiatorValve`(없을 시), ValveHandle에 `ValveRotationGrab` + `XRControllerValveGrabber` + SphereCollider
   - RadiatorB의 Valve에 `RadiatorValveLink`(invertAxis=true), ValveHandle에 동일한 그랩 셋업
   - RadiatorA에 시각용 `Pipe_Extra_A`
   - RadiatorB에 `PipeSocket_B` (XRSocketInteractor + RadiatorPipeSocket), `Pipe_Broke`, `Pipe_New` (XRGrabInteractable + Pipe + Rigidbody + CapsuleCollider)
   - RadiatorA에 `LeakFog_A` (Translucent), RadiatorB에 `LeakFog_B` (Opaque)
   - 양 라디에이터에 `NetworkObject` 보장
   - 기존 `RadiatorFogVisual` 비활성화 (반대 동작이므로)
5. 인스펙터에서 위치/색/반경 등 미세 조정 후 씬 저장.
6. Ctrl+Z 로 한 번에 되돌릴 수 있습니다.

> 메뉴를 다시 눌러도 멱등하게 동작 — 이미 있는 컴포넌트는 다시 추가하지 않습니다.

생성된 위치는 임의의 기본값이라 두 라디에이터의 메시 모양에 맞게 약간 옮겨야 자연스럽습니다. `MirrorController` 인스펙터의 우상단 ⋮ → **Apply Mirror Now** 로 RadiatorB를 RadiatorA의 거울상으로 한번 더 정리할 수 있습니다.

---

## 🔧 수동 셋업 (자동 메뉴를 쓰지 않는 경우)

이 문서는 새로 추가된 5개 스크립트를 Pipe Scene 안의 RadiatorA, RadiatorB에 어떻게 붙이고 인스펙터를 어떻게 채우는지 단계별로 설명합니다. 이 가이드대로 따라가면 다음이 동작합니다.

- 가상벽 기준으로 RadiatorA / RadiatorB가 좌우 대칭
- 한쪽 ValveHandle을 돌리면 다른 쪽도 같이 회전
- RadiatorB의 Pipe broke를 손으로 집어서 떼어낼 수 있음
- 일정 거리에 있는 Pipe new를 RadiatorB의 소켓에 끼우면 색이 변함
- Pipe broke가 끼워져 있는 동안 RadiatorB 주변엔 시야를 가리는 진한 연기, RadiatorA 주변엔 반투명 연기
- 아무 쪽에서 ValveHandle을 잠그는 방향으로 돌리면 연기가 점점 줄어 0이 되고, 풀면 다시 늘어남
- Pipe new가 끼워지면 밸브와 무관하게 연기 0

추가된 스크립트는 모두 `Assets/Scripts/Puzzle/`에 있습니다.

| 파일 | 역할 |
| --- | --- |
| `RadiatorMirror.cs` | 가상벽을 기준으로 한 트리를 다른 트리로 좌우반전 |
| `RadiatorValveLink.cs` | 마스터 RadiatorValve의 회전 상태를 두 번째 핸들에도 적용 |
| `Pipe.cs` | Pipe broke / Pipe new 마커 + 색 변경 헬퍼 |
| `RadiatorPipeSocket.cs` | RadiatorB의 파이프 소켓 네트워크 상태(`Networked`) |
| `PipeLeakFog.cs` | broke + 밸브 상태에 따른 ParticleSystem 자동 생성 |

기존 `RadiatorValve`, `ValveRotationGrab`, `XRControllerValveGrabber`, `RadiatorFogVisual`은 그대로 활용합니다.

---

## 0. 준비 — Pipe Scene 열기

1. `Assets/Scenes/Scenes/Pipe Scene.unity` 더블클릭하여 엽니다.
2. Hierarchy에서 RadiatorA와 RadiatorB를 찾고, 둘 다 같은 자식 구조(`RadiatorBody`, `Pipes/Pipe_Vertical`, `Pipes/Pipe_Stub`, `Valve/ValveHub/ValveHandle/...`)인지 확인합니다.

## 1. 가상벽(Virtual Wall) 만들기

1. Hierarchy에서 우클릭 → Create Empty, 이름을 `VirtualWall`로 지정.
2. RadiatorA와 RadiatorB의 정확히 가운데 위치로 이동시키고, **forward(파란 축)**가 RadiatorA → RadiatorB 방향을 향하도록 회전.
3. 아무 곳에 `RadiatorMirror` 컴포넌트를 부착합니다(예: 새로 만든 `MirrorController` GameObject).
   - **Source Root**: `RadiatorA` Transform
   - **Mirror Root**: `RadiatorB` Transform
   - **Virtual Wall**: 방금 만든 `VirtualWall`
   - **Live Update**: 에디터 작업 중엔 켜두면 편함. 최종 빌드 전 한 번만 적용하고 싶으면 끄세요.
4. 인스펙터의 컴포넌트 우상단 ⋮ 메뉴 → **Apply Mirror Now**로 즉시 한 번 적용해 보고 RadiatorB가 거울상이 되는지 확인.

> 자식 이름이 정확히 같아야 매칭됩니다. RadiatorA와 RadiatorB의 자식 구조가 다르면 미러링이 부분적으로만 적용됩니다. 둘 다 같은 프리팹/하이어라키에서 시작하세요.

## 2. ValveHandle 연동 — 한쪽 돌리면 양쪽 돌아가기

기존 `RadiatorValve`(NetworkBehaviour) 한 개를 **마스터**로, 다른 한쪽엔 `RadiatorValveLink`만 부착합니다.

1. **마스터 (RadiatorA)**
   - `RadiatorA/Valve` 또는 `RadiatorA/Valve/ValveHandle`에 이미 `RadiatorValve` 컴포넌트가 있다면 그대로 사용. 없으면 추가.
   - `Handle Transform` = RadiatorA의 `ValveHandle`
   - `Rotation Axis Local`은 ValveHandle 기준 회전축(보통 (0,0,1))

2. **종속 (RadiatorB)**
   - RadiatorB 측 같은 위치(예: `RadiatorB/Valve`)에 **`RadiatorValveLink` 컴포넌트만** 부착(중복 RadiatorValve 추가 금지).
   - **Master**: RadiatorA의 `RadiatorValve` 드래그
   - **Follower Handle**: RadiatorB의 `ValveHandle` Transform
   - **Rotation Axis Local**: RadiatorB의 ValveHandle 기준 축
   - **Invert Axis**: RadiatorB가 RadiatorA의 거울상이라면 회전 방향이 반대로 보일 수 있음. 보면서 조정.

3. **양쪽 ValveHandle에 그랩 셋업**
   - RadiatorA/ValveHandle: 기존 셋업(아마도 이미 적용됨)
     - `XRControllerValveGrabber` (XRSimpleInteractable 상속) + `ValveRotationGrab`
     - `ValveRotationGrab.valve` = RadiatorA의 RadiatorValve
   - RadiatorB/ValveHandle: 동일하게 `XRControllerValveGrabber` + `ValveRotationGrab` 추가
     - **`ValveRotationGrab.valve`도 RadiatorA의 RadiatorValve를 가리키게** 하세요.
     - 이렇게 해두면 어디서 잡고 돌리든 같은 [Networked] ValveAngle이 변하고, RadiatorValveLink가 RadiatorB 핸들도 같이 돌립니다.
   - ValveHandle GameObject에는 Collider(보통 SphereCollider/Capsule)이 있어야 컨트롤러로 잡힙니다.

## 3. 추가 파이프(Pipe broke / Pipe new) 만들기

기존 `Pipes/Pipe_Vertical`이나 `Pipe_Stub` 옆에 추가 슬롯이 들어갑니다.

### 3-1. RadiatorA 측 — 시각용

1. 기존 파이프 메시(예: Pipe_Stub)를 복제하여 `Pipe_Extra_A`로 이름 변경.
2. RadiatorA의 자식으로 두고 원하는 위치/회전 조정.
3. 별도 인터랙션은 없음. (시각 대칭만 만족하면 됨)

### 3-2. RadiatorB 측 — 인터랙티브

1. RadiatorB의 자식으로 같은 메시 복제, 이름을 `PipeSocket_B`로 지정. 이건 *소켓*입니다.
2. `PipeSocket_B`에 다음을 추가:
   - `XRSocketInteractor` (XRI)
     - **Interaction Layer Mask**: pipe 전용 레이어를 만들고 거기로 한정하면 다른 잡힐 만한 오브젝트와 충돌 줄어듦
     - **Socket Active**: ON
     - **Show Interactable Hover Meshes**: 디버그용 ON 권장
     - **Recycle Delay Time**: 0
   - `RadiatorPipeSocket` (이번에 추가한 스크립트)
     - 자동으로 위 XRSocketInteractor를 찾음
     - **Broke Color / New Color** 인스펙터에서 원하는 색으로 조정
3. 같은 부모 어딘가에 `NetworkObject`가 있어야 [Networked] 프로퍼티가 동작합니다. RadiatorB 루트에 NetworkObject가 이미 있다면 OK. 없으면 추가.

### 3-3. Pipe broke 오브젝트

1. `Pipe_Extra_A`와 같은 메시를 또 복제, 이름 `Pipe_Broke`로 짓고 RadiatorB 안 또는 씬 루트 어디든 배치(시작 위치는 PipeSocket_B 안에 들어가 있으면 깔끔).
2. `Pipe_Broke`에 다음 추가:
   - `Rigidbody` (Use Gravity = false 권장, Is Kinematic은 그랩 시 자동 토글됨)
   - `Collider` (Mesh/Box/Capsule)
   - `XRGrabInteractable`
     - **Movement Type**: Kinematic 또는 Velocity Tracking
     - **Throw On Detach**: 취향대로
   - `Pipe` (이번에 추가한 마커)
     - **Kind**: `Broke`
     - **Colored Renderers**: 색을 바꿀 자식 Renderer (Reset 버튼으로 자동 채움 가능)
3. PipeSocket_B의 `XRSocketInteractor` 인스펙터 하단의 **Starting Selected Interactable**에 `Pipe_Broke`의 `XRGrabInteractable`을 드래그해서 시작부터 끼워져 있도록.

### 3-4. Pipe new 오브젝트

1. `Pipe_Broke`를 복제, 이름 `Pipe_New`. 위치는 RadiatorB에서 손이 닿을 만한 거리(예: 1m 떨어진 테이블, 바닥 등).
2. `Pipe`(마커)의 **Kind**를 `New`로 변경.
3. 그 외 컴포넌트는 동일 (`Rigidbody`, `Collider`, `XRGrabInteractable`).

> 두 파이프는 동일한 XRSocketInteractor에 둘 다 끼울 수 있어야 합니다. 기본 XRSocketInteractor 설정은 모든 XRGrabInteractable을 받으므로 별도 작업 필요 없음.

## 4. 연기(Smoke) 셋업

`PipeLeakFog`는 ParticleSystem을 자동으로 생성합니다. 빈 GameObject 두 개에 컴포넌트만 붙이면 됩니다.

### 4-1. RadiatorB 측 — Opaque

1. `RadiatorB`의 자식으로 빈 GO `LeakFog_B` 추가. 위치는 `PipeSocket_B` 근처(증기가 새어 나오는 지점).
2. `PipeLeakFog` 컴포넌트 추가:
   - **Socket**: `RadiatorPipeSocket` (PipeSocket_B의 것)
   - **Valve**: 마스터 `RadiatorValve` (RadiatorA의 것)
   - **Fog Origin**: 비워두면 자기 Transform 사용
   - **Style**: `Opaque`
   - 알파/반경/방출량은 시야가 적당히 가려지도록 조정 (`opaqueAlpha` 0.7~0.9 권장)

### 4-2. RadiatorA 측 — Translucent

1. `RadiatorA`의 자식으로 빈 GO `LeakFog_A` 추가. 위치는 RadiatorA의 대칭 지점.
2. `PipeLeakFog` 컴포넌트 추가:
   - **Socket**: 동일하게 RadiatorB의 `RadiatorPipeSocket` (한 개의 [Networked] 상태에서 양쪽 다 읽음)
   - **Valve**: 동일하게 마스터 `RadiatorValve`
   - **Style**: `Translucent`
   - `translucentAlpha` 0.2~0.35 권장

### 4-3. 기존 `RadiatorFogVisual` 처리

`RadiatorFogVisual`은 "밸브를 잠그면 안개가 늘어나는" 반대 동작이므로, 이 퍼즐엔 사용하지 않습니다. 두 라디에이터에 이미 붙어 있다면 비활성화하거나 제거하세요.

## 5. 동작 검증 체크리스트

Editor 또는 Quest에서:

- [ ] VirtualWall에 Apply Mirror Now 누르면 RadiatorB 모양/위치가 RadiatorA의 거울상으로 변함
- [ ] RadiatorA 핸들을 잡고 돌리면 RadiatorB 핸들도 같이 돌고, ValveAngle이 변함
- [ ] RadiatorB 핸들을 잡고 돌려도 같이 동작
- [ ] Pipe_Broke를 컨트롤러로 잡으면 소켓에서 빠지고 ConnectedKind가 `None`이 됨 (연기 사라짐)
- [ ] Pipe_New를 잡고 PipeSocket_B 근처로 가져가면 스냅, 색상이 newColor로 바뀜, 연기는 안 나옴
- [ ] 다시 Pipe_Broke를 끼우면 연기 복귀, 색상은 brokeColor
- [ ] 밸브를 끝까지 잠그면 두 쪽 연기가 0으로 페이드 아웃, 다시 풀면 페이드 인

## 6. 자주 만나는 문제

**Q. RadiatorB에서 핸들을 돌렸는데 RadiatorA만 돌아간다 (또는 그 반대).**
A. RadiatorB의 `ValveRotationGrab.valve`가 RadiatorB의 RadiatorValve를 가리키고 있을 가능성. 마스터(RadiatorA의 RadiatorValve)를 가리켜야 합니다. 또한 RadiatorValveLink의 invertAxis 토글도 확인.

**Q. 거울상이 되지 않는다.**
A. RadiatorA / RadiatorB의 자식 이름이 정확히 일치하는지 확인. 다르면 RadiatorMirror.swapLeftRightNames를 켜거나 이름을 맞추세요.

**Q. 소켓에 끼워지지 않는다.**
A. Pipe 오브젝트에 Collider가 없거나 너무 작을 수 있음. XRSocketInteractor의 Socket Snapping Radius / Layer Mask 확인. Pipe의 XRGrabInteractable.Interaction Layer Mask가 Socket과 일치하는지도.

**Q. 연기가 너무 짙어 시야 완전 차단.**
A. `PipeLeakFog (Opaque)`의 `opaqueAlpha`, `maxEmissionRate`, `maxRadius`를 줄이세요. 모바일(Quest) 성능을 위해 `maxParticles`도 적당히.

**Q. NetworkBehaviour가 Spawn되지 않는다는 경고.**
A. `RadiatorPipeSocket`이 들어 있는 GameObject 또는 그 부모에 `NetworkObject`가 있어야 합니다. RadiatorB 루트에 추가하세요.

---

이상의 셋업 후 한 번만 적용하면 두 라디에이터의 미러/연동/연기 동작이 자동으로 [Networked] 상태로 두 플레이어 사이에 동기화됩니다.
