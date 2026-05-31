# Main Scene 네트워크 동기화 — 사용 가이드

> 목표: Main Scene 에서 한 플레이어가 **잡거나 / 돌리거나 / 없애거나 / 클릭하거나 / 가까이 가서 움직인** 결과를
> 상대 플레이어에게 **실시간으로** 보이게 한다. (Photon Fusion 2 Shared Mode)

---

## 1. 진단 — 왜 안 보였나

- Main Scene 은 사실상 게임 전체가 한 씬에 들어 있고, 네트워크 인프라(`RoomLauncher`, `NetworkRunner`,
  아바타 동기화)는 정상 작동한다.
- 그러나 **인터랙터블 대부분이 네트워크 동기화가 안 걸려 있었다.**
  - 그랩 오브젝트 37개 중 29개만 `NetworkGrabbableSync` 적용 → **9개는 구버전**이라 불안정
  - 문/파이프 미니게임/소방/빛반사/절벽 플랫폼/케이지·생물/힌트 퍼즐 등은 **일반 MonoBehaviour** →
    로컬에서만 상태가 바뀌고 상대에겐 전파되지 않음
- 런타임 스크립트 109개가 미동기화 상태였다.

이 패키지는 **소수의 재사용 컴포넌트 + 한 번에 찍어주는 에디터 툴**로 이를 해결한다.

---

## 2. 추가된 것

### 런타임 컴포넌트 (`Assets/Scripts/Network/Sync/`)

| 컴포넌트 | 역할 | 커버하는 상호작용 |
|---|---|---|
| `NetworkAuthorityClaim` | 조작/근접 시 State Authority 를 로컬로 가져옴 | 잡기·클릭·근접이동 (그랩 전용 `GrabAuthorityHandover` 의 일반화) |
| `NetworkActiveSync` | 보임/숨김(삭제) 상태를 `[Networked]` 로 동기화 | 없애기 (불 끄기·생물 포획·소모) |
| `NetworkEventRelay` | 1회성 효과를 RPC 로 전 피어에 복제 | 클릭 효과(사운드·조명·점수·단계 진행) |
| `ProxyDriverGate` | 비권위 측의 로컬 구동 로직을 끔 | 회전체/이동체의 떨림·충돌 방지 |
| `PlayerHeadRegistry` | 모든 플레이어 머리 위치 레지스트리(자동) | 근접 자동문의 결정론적 양쪽-머리 감지 |

### 기존 스크립트 보정 (가산적, 안전)

- `NetworkPlayer.cs` — Spawned/Despawned 에서 머리 앵커를 `PlayerHeadRegistry` 에 등록/해제 (2줄)
- `AutoSlidingDoor.cs` — 로컬뿐 아니라 **양쪽 플레이어 머리**를 모두 감지하도록 보정 → 별도 네트워크 상태 없이 개폐 일치

### 에디터 툴 (`Assets/Scripts/Editor/MainSceneSyncSetupTool.cs`)

메뉴: **`Tools/Network/Auto-Sync/`**

---

## 3. 실행 순서 (Main Scene 을 연 상태에서)

1. **`1) Dry-Run Report (변경 없음)`**
   - 아무것도 바꾸지 않고, 각 인터랙터블이 어떤 카테고리로 분류되는지 Console 에 출력.
   - 의도치 않은 오브젝트(예: UI)가 잡혀 있지 않은지 먼저 확인.

2. **`2) Finish Grab Conversion (전체)`**
   - 구버전 그랩 9개를 `NetworkGrabbableSync` 로 마저 전환.

3. **`4) Apply to Whole Scene`** (또는 오브젝트를 골라 `3) Apply to Selection`)
   - 카테고리별로 컴포넌트를 일괄 부착하고 `AllowStateAuthorityOverride` 를 켠다.
   - 부모에 `NetworkObject` 가 있는 **중첩 후보는 건너뛰고** 리포트한다(베이킹 사고 방지).
   - 모든 변경은 **Ctrl+Z(Undo)** 로 되돌릴 수 있다.

4. **씬 저장 (Ctrl+S)** — *필수.* 저장 시 Fusion 이 `NetworkedBehaviours` 를 재베이킹한다.

5. **2인 플레이로 검증** (아래 6장 체크리스트).

> 카테고리별 적용 내용
> - **Grabbable**: `NetworkObject` + `NetworkTransform` + `NetworkGrabbableSync`
> - **Mover**(밸브·핸들·슬라이더·미러 등 자기 transform 이 회전/이동): `NetworkObject` + `NetworkTransform` + `NetworkAuthorityClaim` + `ProxyDriverGate`
> - **Deletable**(불·생물 등): `NetworkObject` + `NetworkActiveSync(autoMirror)`
> - **Button**(그랩 아닌 XR Interactable): `NetworkObject` + `NetworkAuthorityClaim` + `NetworkEventRelay`

---

## 4. 자동으로 끝나는 것 vs 1줄 수동 연결이 필요한 것

### 자동 (툴 실행 + 씬 저장이면 끝)

- **잡기**(Grabbable) — 손 따라 이동/회전이 그대로 동기화.
- **돌리기·직접 미는 이동**(Mover) — 오브젝트 자체 transform 이 움직이면 `NetworkTransform` 이 전파.
- **근접 자동문**(`AutoSlidingDoor`) — 양쪽 머리 감지로 결정론적 동기화.
- **렌더러를 꺼서 숨기는 삭제** — `NetworkActiveSync.autoMirror` 가 자동 전파.

### 수동 연결 1줄 (인스펙터에서 이벤트만 연결)

- **GameObject 통째로 `SetActive(false)` 로 사라지는 오브젝트**(예: 불 `FirefightFire`)
  → `autoMirror` 로는 못 잡는다. 그 스크립트의 제거 UnityEvent(예: `FirefightFire.OnExtinguished`)에
  **`NetworkActiveSync.Hide()`** 를 연결.
  *(NetworkObject 가 붙은 GameObject 자체를 비활성화하면 동기화가 끊기므로, 가능하면 `Hide()`가
  Renderer/Collider 만 끄게 두는 방식을 권장.)*

- **클릭으로 다른 것을 바꾸는 버튼**
  → 버튼의 select/activate(또는 기존 onClick)에 **`NetworkEventRelay.Relay()`** 를 연결하고,
  `NetworkEventRelay.onRelayed` 에 **버튼이 로컬에서 하던 것과 동일한 대상/메서드**를 연결.
  *(단, 효과가 "오브젝트 이동/숨김"이면 그 대상에 `NetworkTransform`/`NetworkActiveSync` 를 두는 편이 더 견고.)*

> 이미 `NetworkBehaviour` 로 직접 네트워크 처리된 버튼(예: `XRPhysicalButton`, `PressureValve`)에는
> 툴이 `NetworkEventRelay` 를 추가하지 않는다 — 중복 불필요.

---

## 5. 권한(비대칭) 게이팅과의 관계

- 기존 `OwnerSide` + `OwnerSelectFilter`(P1/P2 조작 권한 분리)는 **그대로 유효**하다.
- `NetworkAuthorityClaim` 은 "조작이 허용된 다음" 단계의 **트랜스폼 권위 이전**만 담당한다.
  즉, 비소유자는 `OwnerSelectFilter` 에서 select 가 막히므로 Claim 자체가 호출되지 않는다.
- 결론: 비대칭 권한 설계와 충돌하지 않는다.

---

## 6. 검증 체크리스트 (2인 플레이)

- [ ] P1 이 물건을 잡아 옮기면 P2 화면에서도 같은 위치로 움직이는가
- [ ] P1 이 밸브/핸들을 돌리면 P2 화면에서도 회전이 보이는가
- [ ] P1 이 문에 다가가면(또는 P2 가) 양쪽 모두 문이 열리는가
- [ ] P1 이 불을 끄거나 오브젝트를 없애면 P2 화면에서도 사라지는가
- [ ] 잡는 도중 끊김/되감김/멀리 날아감이 없는가
- [ ] 콘솔에 `AllowStateAuthorityOverride 꺼짐` 경고가 없는가

문제가 보이면 해당 컴포넌트의 `verboseLog` 를 켜고 Console 로그로 권위 이전 흐름을 추적.

---

## 7. 범위 / 한계

- 이번 작업 범위는 **Main Scene** 이다. 같은 컴포넌트는 다른 씬에도 재사용 가능(각 씬에서 툴 실행).
- `Stage1SlidingDoor` / `Stage2SlidingDoor` 등 **다른 방식의 문**은 자동 분류에서 제외돼 있다.
  근접식이면 `AutoSlidingDoor` 처럼 `PlayerHeadRegistry` 를 참조하도록 같은 패턴을 적용하거나,
  버튼식이면 `NetworkEventRelay` 로 처리하면 된다.
- `FireHazard`(불에 닿으면 본인만 리스폰), `CliffController`(본인만 낙하 리스폰)처럼 **각자에게만 영향을
  주는 로컬 로직은 동기화 불필요** — 의도적으로 제외했다.
- 퍼즐의 "점진적 수치"(예: 불 강도 게이지)까지 정밀 동기화하려면 해당 스크립트를 `NetworkBehaviour`
  + `[Networked]` 로 바꾸는 퍼즐별 작업이 추가로 필요하다. 이 패키지는 "보이는 결과"를 동기화한다.
