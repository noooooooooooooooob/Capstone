# Zoo 퍼즐 설계서

생명체 4종을 알맞은 케이지 4개에 짝지어 넣으면 클리어되는 비대칭 협력 퍼즐.
같은 VR 방에서 P1·P2가 직접 음성으로 대화하면서 푼다.

---

## 1. 역할 분배

| 항목 | 소유자 (`OwnerSide`) | 비고 |
|---|---|---|
| 잠자리 (Dragonfly) | P1 | 잡으면 케이지 매칭 힌트 4쌍 공개 |
| 뱀 (Snake) | P1 | 장갑 착용 시에만 잡힘 |
| 도마뱀 (Lizard) | P2 | 빠름. 게가 구멍 막으면 둔화 |
| 게 (Crab) | P2 | 무거움. 강한 임팩트로 셸 모드 토글 |
| 잠자리채 (CatchNet) | 공용 | XRGrabInteractable, 양쪽 모두 잡을 수 있음 |
| 장갑 (Gloves) | 공용 | XR Socket 또는 부착식. 손에 끼면 절연 |
| 케이지 ×4 | 공용 | 종 일치 시 락 |

> 협력성: 잠자리는 P1만 잡을 수 있지만 힌트는 양쪽이 같이 봐도 됨(음성 공유 가능).
> 게(P2)가 셸 모드로 정지해야 도마뱀(P2)을 잡을 수 있으므로 P2 내부에서도 핸드 순서가 강제됨.

---

## 2. 데이터 모델

```csharp
enum CreatureKind { Dragonfly, Lizard, Crab, Snake }
enum CageId       { Red, Blue, Green, Yellow }   // 시각용 색 키
enum CreatureState { Idle, Wander, Fleeing, Stunned, Captured, Caged }
```

**힌트 매핑** — `ZooHintTable : ScriptableObject` 에 `CreatureKind → CageId` 4쌍을 저장.
런타임에 셔플도 옵션(기본은 인스펙터 고정).

**진행 카운터** — `ZooPuzzleController` 가 `[Networked] int CagedCount` 보유, 4가 되면 `OnSolved`.

---

## 3. 생명체 명세

### 3.1 공통 베이스 `ZooCreature`
- `NetworkBehaviour` + 자식 `NetworkTransform` (`NetworkObject` 필수)
- `[Networked] CreatureState State`
- `StateAuthority` 만 AI tick. Shared Mode에서 처음에는 호스트가 권위, 잡힐 때 `RequestStateAuthority()` 로 잡는 쪽이 가져감 (`GrabAuthorityHandover` 패턴 응용)
- 추상 메서드: `TickAI(float dt)`, `OnCapturedBy(Component captor)`, `OnReleased()`

### 3.2 Dragonfly (P1)
- 이동: Y 진폭 sin 보빙 + 무작위 XZ wander, 잠자리채가 근처에 오면 회피 가속
- 캡처: 잠자리채 트리거 콜라이더에 들어온 채로 1 프레임 → `State = Captured`, 부모를 NetTrigger로
- 캡처 직후 `ZooHintDisplay.Reveal(table)` RPC 발행 → 양쪽 피어에서 4쌍 노출
- 케이지에 넣을 필요 없음(잡힌 시점에 일종의 정보 보상) — **단**, 매핑 통일을 위해 잠자리도 자기 케이지에 들어가야 클리어 카운트되도록 설계 권장

### 3.3 Lizard (P2)
- 이동: NavMeshAgent 또는 단순 Raycast wander, 빠른 속도 (예: 3 m/s)
- 도주: P2 손이 `proximityRadius` 안 → 반대 방향 가속
- 도주 경로의 한 지점에 `LizardEscapeHole` 트리거 — 게가 셸 모드로 그 위에 있으면 차단
- 차단 상태에서 손이 닿으면 직접 `OnCapturedBy(hand)` → `Captured`

### 3.4 Crab (P2)
- Rigidbody 사용, mass 큼 (예: 8 kg). XRGrabInteractable 의 `interactionLayerMask` 는 가벼움보다 무거운 클래스로 별도 분리, **잡지 않고 손으로 밀기만** 자연스럽도록 grab 권한 자체를 비활성하거나 매우 무겁게 설정
- 충돌 impulse 누적 → 임계값(예: 4.0) 이상이면 `ShellMode` 토글. 셸 모드는 `[Networked] bool InShell`
- 셸 모드: 자식 모델 교체 + isKinematic=false 유지, 마찰 ↑, AI 정지
- 셸 위치가 `LizardEscapeHole` 트리거 안에 들어가면 hole.Blocked = true

### 3.5 Snake (P1)
- 이동: 사인파 슬리더 (`Vector3 forward * speed + perp * sin(t*freq)*amp`)
- 손이 닿으면:
  - 절연 안 됨 → 손 컴포넌트에 `OnElectrocuted()` 콜백 + 손 햅틱 강한 펄스, 손이 잠시 비활성
  - 절연 됨 (장갑 부착) → `OnCapturedBy(hand)` → `Captured`
- 손의 절연 여부는 `HandInsulation` 컴포넌트(장갑 attach 여부) 로 판정

---

## 4. 도구

### 4.1 CatchNet (잠자리채)
- 양손 grab 가능한 XRGrabInteractable
- 그물 헤드 끝에 트리거 콜라이더(자식). `OnTriggerEnter` 에서 `ZooCreature` 검사 후 Kind==Dragonfly 면 캡처
- 잠자리 외 생명체에는 효과 없음(콜백 무시)
- 캡처 후 net 끝에 잠자리 부착, 다시 케이지 위에서 release 액션(grip 풀거나 trigger 한 번 더) → 케이지 시도

### 4.2 Gloves
- 두 옵션:
  - **A. XR Socket 부착** — 손목 근처에 `XRSocketInteractor`, 장갑을 가져다 대면 socket 에 잠금. socket 자식이라는 사실이 `HandInsulation.IsInsulated` true 신호
  - **B. 단일 부착 토글** — 장갑을 손에 직접 grab 한 채 트리거로 끌어당기면 `Hand.AttachGlove(this)`. 더 단순하지만 손에 동시에 잠자리채 못 듦
- 1차는 **A 권장**, 코드는 양쪽 다 지원 가능하게 인터페이스로 분리

### 4.3 손(Hand) 컴포넌트
- 기존 `LocalPlayerSide` 와 별개. 각 손 GameObject 에 `HandInsulation` + 트리거 콜라이더 부착
- 손이 직접 `ZooCreature` 를 잡을 때: Lizard 는 항상 OK, Snake 는 IsInsulated 검사, Dragonfly/Crab 은 별도 도구 우선

---

## 5. 케이지 매칭

`CreatureCage` 는 트리거 콜라이더 + 시각 색 마커.
```
OnTriggerEnter(other):
  var c = other.GetComponentInParent<ZooCreature>();
  if (c == null || c.State != Captured) return;
  if (cage.AcceptedKind != c.Kind) { reject feedback; return; }
  c.NotifyCaged(this);
  controller.NotifyOneCaged();
  if (controller.CagedCount == 4) controller.RaiseSolved();
```

오답 케이지 거부 시 부저 + 잠깐 색 깜빡임 피드백. 거부 후 생명체는 잠시 도주 모드로 복귀(P1·P2 모두 다시 잡아야 함).

> 잠자리는 4쌍 힌트를 모두 노출했더라도 자기 케이지에 들어가야 클리어 카운트가 4가 되도록 한다. 4 카운터 통일이 단순함.

---

## 6. 게–도마뱀 결합 (`LizardEscapeHole`)

```
LizardEscapeHole(트리거)
  Blocked = false (default)
  매 FixedUpdateNetwork:
    Blocked = (게.InShell && 게.transform within hole bounds)
도마뱀 AI:
  fleePath 가 hole 을 지나가도록 디자인
  hole.Blocked == true 면 fleeSpeed *= 0.2 + 손에 잡힘 허용
```

씬 디자인: 도마뱀의 주된 도주 루트가 hole 하나를 반드시 통과하도록 콜라이더 벽을 배치. hole 은 충분히 좁아서 게가 셸 모드일 때만 정확히 막힘.

---

## 7. 네트워크 동기화 전략

- 모든 생명체·도구·케이지 GameObject 는 `NetworkObject` 보유 (`AllowStateAuthorityOverride` ON)
- 생명체 트랜스폼은 자식 `NetworkTransform` 으로 — `NetworkPlayer` 와 동일
- AI 시뮬레이션은 `FixedUpdateNetwork` 안에서 `HasStateAuthority` 일 때만 실행
- 권한 이양:
  - 잠자리채/장갑이 잡힐 때 → `GrabAuthorityHandover` (기존)
  - 생명체가 잡힐 때 → 잡힌 도구/손의 `StateAuthority` 로 권한 이양
- Solved/CagedCount 등 핵심 진행 상태는 `ZooPuzzleController.[Networked]` 로 보유
- 힌트 노출은 `RPC_RevealHint(seed)` — 양쪽 모두 같은 ScriptableObject 룩업으로 동기

---

## 8. 클래스 구성도

```
ZooPuzzleController (NetworkBehaviour)
  ├─ ZooHintTable (ScriptableObject ref)
  ├─ ZooHintDisplay (UI 후크)
  ├─ CreatureCage[4]
  ├─ ZooCreature[N]              (abstract)
  │    ├─ DragonflyCreature
  │    ├─ LizardCreature
  │    ├─ CrabCreature
  │    └─ SnakeCreature
  ├─ CatchNet (도구)
  ├─ Gloves   (도구)             — HandInsulation 와 페어
  └─ LizardEscapeHole (월드 트리거)
```

---

## 9. 씬 셋업 단계 (체크리스트)

1. `Assets/Scripts/PipePuz/Zoo/` 에 스크립트 배치 (이미 진행됨)
2. 빈 GO `ZooRoot` 아래에:
   - `ZooPuzzleController`
   - `ZooHintDisplay` (TMP 텍스트 4줄)
   - Cages: `CreatureCage_Red/Blue/Green/Yellow`
   - LizardEscapeHole
3. 생명체 프리팹 4종 (NetworkObject + 자식 NetworkTransform + AI 스크립트)
4. 잠자리채/장갑 프리팹 (NetworkObject + XRGrabInteractable + GrabAuthorityHandover + OwnerSide)
5. `RoomLauncher` 의 `userPrefab.NetworkPrefabsList` 처럼 모든 NetworkObject 프리팹을 NetworkProjectConfig 에 등록
6. NavMeshSurface bake (도마뱀용)
7. Build Settings 에 Zoo 씬 등록

---

## 10. 마일스톤

| 단계 | 산출물 | 검증 |
|---|---|---|
| **M0 골격** | 본 문서의 스크립트 13개 컴파일 통과 | Unity 콘솔 오류 0 |
| **M1 로컬 단일 플레이** | Network 무시, 4 생명체·도구·케이지로 1인 풀이 가능 | 도마뱀 잡고 케이지 넣기까지 한 사이클 |
| **M2 게-도마뱀 결합** | 게 셸 모드 토글, hole 차단 → 도마뱀 둔화 | 1인 풀이 시간 X분 → Y분으로 단축 |
| **M3 잠자리 힌트** | 잠자리채 캡처 → 4쌍 UI 노출 | 매번 다른 매핑이면 보너스 |
| **M4 멀티 동기화** | Shared Mode 2 피어, P1/P2 OwnerSide 게이트, NetworkTransform | 두 헤드셋에서 같은 결과 |
| **M5 폴리시** | 햅틱·VFX·SFX·실패 피드백·잠자리 힌트 셔플 | UX 통과 |

---

## 11. 리스크 / 결정 포인트

- **NavMesh 사용 여부** — 빠르지만 동적 막힘(hole.Blocked) 표현이 까다로움. Off-mesh link / NavMesh Obstacle 로 hole 을 동적 막을 수 있음. 1차는 단순 Raycast wander 권장 → M2 에서 NavMesh 로 업그레이드.
- **게 grab 가능 여부** — "밀기만"이 핵심. XRGrabInteractable 을 안 붙이고 손·도구 콜라이더의 PhysX 임펄스만으로 미는 방향 권장. 이렇게 하면 셸 토글 임펄스 임계값 튜닝이 그대로 살아남.
- **잠자리 캡처 후 거동** — 잠자리채 헤드에 자식으로 붙고 케이지에 들어갈 때 release. release 트리거(grip 풀기 vs 별도 버튼)는 UX 테스트 후 결정.
- **권한 이양 타이밍** — 잡힌 순간 잡은 사람의 StateAuthority. release/캐이지 진입 직후에는 다시 그 위치를 가진 사람(P1·P2 중 누구) 의 권위로? 1차는 캐이지 안에 들어간 후 권위 회수하지 않음.
- **장갑 socket 보관** — 장갑을 안 쓸 때 어디 둘지. 벽 socket 또는 작업대.

---

## 12. 다음 행동

이 문서에서 결정 후, 본 디렉토리(`Assets/Scripts/PipePuz/Zoo/`)의 골격 스크립트를 따라 씬에 프리팹을 만들고 M1 부터 한 단계씩 채워나간다.
