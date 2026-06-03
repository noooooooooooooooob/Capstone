# NPC 연구원(이 박사) 대사 트리거 — 설정 안내

Main Scene에서 게임 이벤트가 발생하면 연구원 NPC 오디오 + 자막이 양쪽 헤드셋에 동시·1회 재생되도록 연결했습니다.

## 이미 준비돼 있던 것 (수정 안 함)
- `Assets/Scriptable Object/Dialogue.asset` — A1~C3 9개 대사가 텍스트·voiceClip(`Assets/Audio/NPC Audio/*.mp3`)·화자("Reasearcher Lee")까지 모두 채워져 있고, 씬의 GameManager에 연결돼 있습니다.
- 자막 표시(`SubtitleHUD`)와 네트워크 브로드캐스트(`GameManager.PlayDialogueRpc`) 경로.

## 트리거 매핑

| id | 언제 | 연결 방식 |
|----|------|-----------|
| A1, A2 | 시작 후 메인 디스플레이의 안정화 버튼을 **처음** 누를 때 (순서대로) | `MainControlSystem` 코드 (자동) |
| A3 | 충전 배터리 3개 설치 후 버튼으로 복구(안정화) 성공 시 | `MainControlSystem` 코드 (자동) |
| A4 | Stage1 `PipeSmokePuz/MiniGame2` 완성 | `PipeMiniGame2Board.OnSolved` |
| B1 | Stage2 CageRoom 진입 | 트리거 볼륨 (위치 직접 지정) |
| B2 | Stage2 케이지 퍼즐 클리어 | `ClearSoundMaker.OnSolved` (+ `ZooPuzzleController.OnSolved`) |
| C1 | Stage3 `RoomSeen/Room (Stage1 Modular)`의 문 열림 | 문 `OnOpened` |
| C2 | Stage3 `RoomSeen/RoomCliff (Stage1 Skin)/LightOrbSocket`에 구체 도킹 | `LightOrbSocket.OnOrbInserted` |
| C3 | Stage3 광선 퍼즐 클리어 (모든 거울 순서대로 → Receiver 도달) | `LightBeamController.OnAllReceiversHit` |

## 적용 방법 (Unity Editor에서 1회)

1. **Main Scene**을 연다.
2. 메뉴 **Capstone ▸ NPC ▸ Setup Dialogue Triggers (현재 씬)** 실행.
   - A4 / B2 / C1 / C2 / C3 대상 오브젝트에 `NpcCueBinder`가 자동 부착됩니다 (Stage3는 `RoomSeen` 하위로 스코프).
   - B1용 트리거 볼륨 `NPC_CueTrigger_B1_CageRoom`(BoxCollider, isTrigger)이 생성됩니다.
   - Console에 연결 결과 로그가 출력됩니다 — 못 찾은 항목이 있으면 여기서 확인.
3. **B1 트리거 볼륨을 CageRoom 입구 위치/크기에 맞게 배치**한다. (씬 원점에 생성되므로 직접 옮겨야 함)
4. 씬을 **저장**한다.

> A1/A2/A3는 코드에서 직접 호출하므로 별도 작업이 필요 없습니다.

## 네트워크 동작
- 모든 트리거는 `GameManager.TriggerNpcCue(...)`를 통합니다.
- 권한이 없는 피어(P2 등)에서 호출돼도 RPC로 **StateAuthority(호스트)**에 라우팅됩니다.
- 호스트가 **중복 방지(1회)** 후 기존 `PlayDialogueRpc`로 전 피어에 동기 재생 → 양쪽 헤드셋에 같은 자막·음성이 한 번만 표시됩니다.
- 양쪽 피어에서 동시에 같은 이벤트가 발생해도(문 열림, 구체 도킹 등) 한 번만 재생됩니다.

## 자막 표시
- `SubtitleHUD.splitLongTextIntoSentences`(기본 ON): A1처럼 긴 대사는 문장(`. ! ? …`/개행) 단위로 나뉘어 **클립 길이 안에서 순서대로** 표시됩니다. 보이스 클립은 1회만 재생되고 자막만 전환됩니다.
- 끄면 기존처럼 전체 텍스트를 한 번에 표시합니다.

## 변경/추가된 파일
- 추가: `Assets/Scripts/Game/NpcCueBinder.cs`, `Assets/Scripts/Editor/NpcDialogueSetupTool.cs`
- 수정: `Assets/Scripts/Game/GameManager.cs` (이벤트 cue API), `Assets/Scripts/UI/SubtitleHUD.cs` (문장 분할), `Assets/Scripts/Stage1/DisplayPanel/MainControlSystem.cs` (A1/A2/A3)

## 미세 조정
- 트리거별 대사를 바꾸려면 해당 오브젝트의 `NpcCueBinder.cueIds`를 수정.
- A1/A2/A3 대사는 `MainControlSystem`의 `introCueIds`, `pipeBurstCueIds` 필드에서 변경.
- C3에서 `LightBeamController`가 여러 개 연결됐다면(로그 확인) Stage3 외 것의 `NpcCueBinder`는 제거하세요.
