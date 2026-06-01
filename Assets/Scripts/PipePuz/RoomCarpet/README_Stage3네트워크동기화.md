# Stage3 (RoomCarpet) 네트워크 동기화 — 사용 가이드

> 목표: Main Scene 의 **Stage3**(사라지는 카펫 / 단서공 퍼즐)에서 한 플레이어가 한 행동
> — 카펫 발사·디스펜서에서 꺼내기·던지기·밟기, 단서공 던지기·자석 캡처·슬롯 채우기, 도착·클리어 —
> 을 상대 플레이어 화면에서도 **실시간으로** 보이고 함께 풀 수 있게 한다. (Photon Fusion 2 Shared Mode)
>
> Stage1 / Main Scene 동기화(`Assets/Scripts/Network/Sync/README_네트워크동기화.md`)와 같은 철학이며,
> Stage3 특유의 **런타임 스폰 카펫**과 **단서공 퍼즐 상태**를 추가로 다룬다.

---

## 1. 왜 별도 작업이 필요했나

Main Scene 의 범용 셋업 툴(`Tools/Network/Auto-Sync`)은 "씬에 미리 놓인 그랩/무버/버튼"을 동기화한다.
하지만 Stage3 는 두 가지를 그 방식으로 못 잡는다.

1. **카펫이 런타임에 생성된다.** `CarpetDispenser` / `CarpetLauncher` 는 `new GameObject` 로 카펫을
   즉석에서 만든다. 로컬에서만 생긴 GameObject 는 상대 피어에 존재하지 않는다 → Stage1 의
   `BatteryDispenser` 처럼 `Runner.Spawn(프리팹)` 으로 띄워야 복제된다.
2. **단서공 퍼즐은 "결과 상태"가 있다.** 자석 캡처 → 슬롯 안착 → 보드 클리어는 위치(NetworkTransform)
   만으로는 부족하고, "어느 슬롯이 찼는지 / 풀렸는지"를 실어야 양쪽이 일치한다.

---

## 2. 추가된 것

### 런타임 컴포넌트 (`Assets/Scripts/PipePuz/RoomCarpet/Network/`)

| 컴포넌트 | 붙는 곳 | 역할 |
|---|---|---|
| `Stage3CarpetNetwork` | 독립 루트 GO 1개 | 카펫 네트워크 스폰 매니저. 권위 측이 디스펜서마다 대기 카펫 1개를 유지(`Runner.Spawn`). 런처 발사도 여기로 위임. |
| `CarpetNetworkSync` | Carpet_Net 프리팹 | 카펫 상태[Networked] 전파, 프록시 물리 정지, 삭제를 `Runner.Despawn` 으로 처리, 발사 속도 적용. |
| `HintBallNetworkSync` | 각 HintBall | 단서공 상태 + 안착 슬롯 인덱스[Networked] 전파 → 양쪽이 동일하게 슬롯을 채우고 클리어. |

### 기존 스크립트 보정 (가산적·안전 — 비네트워크 씬에서는 평소대로 동작)

- `DisappearingCarpet` — 전역 레지스트리(`Active`), 프록시 시뮬레이션 정지(`SuspendSimulation`),
  삭제 훅(`NetworkRemovalHandler`), 프록시 상태 반영(`ApplyNetworkState`). `Object` 가 없으면 전부 무시.
- `CarpetDispenser` / `CarpetLauncher` — 씬에 `Stage3CarpetNetwork` 가 있으면 카펫 공급/발사를
  네트워크 매니저로 위임. 없으면(예: 단독 Stage3.unity, Cliff 씬) **원래대로 로컬 생성**.
- `HintBall` — 프록시 플래그(`NetworkProxy`), 프록시 상태 반영(`SetStateExternal`).
- `HintCatcher` — 그 공의 **권위 피어만** 캡처를 구동(양쪽 중복 캡처 방지). 비네트워크면 그대로.
- `DisappearingCarpetController` — 안착 카펫 안전 검사를 `ActiveCarpetsRoot` 자식이 아니라
  전역 레지스트리로 순회(네트워크 카펫은 그랩 동기화가 부모를 떼므로 자식이 아님).

### 에디터 툴 (`Assets/Scripts/Editor/Stage3SyncSetupTool.cs`)

메뉴: **`Tools/Network/Stage3/`**

---

## 3. 실행 순서 (Main Scene 을 연 상태에서)

1. **`1) Dry-Run Report (변경 없음)`** — 무엇이 처리될지 Console 로 확인. 의도치 않은 오브젝트/중첩 경고 점검.
2. **`2) Build Carpet Prefab`** — `Assets/Prefab/Stage3/Carpet_Net.prefab` + 머티리얼 생성.
   (이미 있으면 갱신. 3) 에서 없으면 자동 생성하므로 건너뛰어도 됨)
3. **`3) Apply to Scene (full)`**
   - 독립 루트 `Stage3CarpetNetwork` 생성 + 프리팹 할당
   - 각 `HintBall` → NetworkObject + NetworkTransform + NetworkGrabbableSync + HintBallNetworkSync
   - 각 `CarpetLauncher` → NetworkObject + NetworkTransform + NetworkGrabbableSync (잡기 동기화)
   - `CarpetGoalZone` → NetworkObject + NetworkEventRelay (도착 시 양쪽 클리어 전파)
   - 추가된 NetworkObject 에 `AllowStateAuthorityOverride` ON
   - 부모에 NetworkObject 가 있는 **중첩 후보는 건너뛰고** 리포트.
   - 모든 변경은 **Ctrl+Z** 로 되돌릴 수 있다.
4. **씬 저장 (Ctrl+S)** — *필수.* 저장 시 Fusion 이 NetworkedBehaviours 와 **NetworkPrefab 테이블**을
   재베이킹한다(카펫 프리팹 등록 포함).
5. **2인 플레이로 검증** (아래 6장).

---

## 4. 자동으로 끝나는 것 vs 확인이 필요한 것

### 자동 (툴 실행 + 씬 저장이면 끝)
- 카펫 발사/디스펜서 공급 → `Runner.Spawn` 으로 양쪽에 생성, 비행·안착·5초 수명·삭제까지 동기화.
- 카펫 잡기/던지기 → NetworkGrabbableSync + NetworkTransform.
- 단서공 잡기/던지기/자석 캡처/슬롯 안착/보드 클리어 → HintBallNetworkSync + 보드 로직(각 피어 재생).
- 자동 미닫이문(`AutoSlidingDoor`) → 이미 `PlayerHeadRegistry` 양쪽-머리 감지로 동기화(Main Scene 작업분).

### 확인 포인트
- `Stage3CarpetNetwork.carpetPrefab` 이 `Carpet_Net` 으로 채워졌는지(툴이 자동 할당, Inspector 에서 확인).
- 기존에 구버전 그랩 컴포넌트(`GrabAuthorityHandover`, `GrabNetworkSyncPause`)가 HintBall/Launcher 에
  남아 있다면 제거 권장 — `NetworkGrabbableSync` 와 역할이 겹친다.
- 카펫 발사 시 총 본체에 부딪혀 튀면 `CarpetLauncher.SpawnAhead` 를 약간 키운다(기본 0.05).

---

## 5. 권한(비대칭) 게이팅과의 관계

- 기존 `OwnerSide` + `OwnerSelectFilter`(P1/P2 조작 권한)는 그대로 유효하다.
- 네트워크 권위 이전(`NetworkGrabbableSync` / 캡처 권위)은 "조작이 허용된 다음" 단계만 담당하므로
  비대칭 설계와 충돌하지 않는다.

---

## 6. 검증 체크리스트 (2인 플레이)

- [ ] P1 이 카펫총으로 카펫을 쏘면 P2 화면에도 같은 카펫이 날아가 위험 바닥에 안착하는가
- [ ] 디스펜서에서 카펫을 꺼내면 다음 카펫이 양쪽 모두에서 새로 채워지는가
- [ ] 안착한 카펫을 밟으면(머리가 그 위) 리스폰되지 않고 발판으로 인정되는가 (양쪽 각자 안전 판정)
- [ ] 카펫이 5초 뒤 양쪽에서 동시에 사라지는가(되감김/유령 카펫 없음)
- [ ] P2 가 단서공을 던져 P1 쪽 자석 캐처에 빨려 슬롯에 꽂히는 과정이 양쪽에서 보이는가
- [ ] 슬롯이 다 차면 양쪽 모두 클리어(OnSolved)가 발동하는가
- [ ] 누구든 GoalZone 에 도착하면 양쪽 모두 클리어되는가
- [ ] 콘솔에 `AllowStateAuthorityOverride 꺼짐` / `carpetPrefab null` 경고가 없는가

문제가 보이면 해당 컴포넌트의 `verboseLog`(NetworkGrabbableSync 등)를 켜고 로그로 권위 흐름을 추적.

---

## 7. 범위 / 한계

- 작업 범위는 **Main Scene 의 Stage3**. 단독 `Stage3.unity` / `DH-Pipe` / Cliff 씬에는
  `Stage3CarpetNetwork` 를 두지 않으므로 스크립트가 **로컬 모드로 평소대로** 동작한다(무해).
- **리스폰**(위험 바닥에서 떨어짐)은 의도적으로 로컬 — 각자 자기 머리 위치로만 판정/이동한다
  (Stage1 의 CliffController/FireHazard 와 동일 철학).
- 카펫 비행은 NetworkTransform 기반이라 권위 측 물리를 프록시가 보간한다. 정밀한 물리 일치보다
  "보이는 결과 일치"를 목표로 한다.
- 카펫 프리팹은 표준 크기(0.9 × 0.02 × 1.2) 고정. 디스펜서별 크기 변형이 필요해지면
  `CarpetNetworkSync` 에 [Networked] 크기/머티리얼을 추가해 확장한다.
