namespace PipePuz.Zoo
{
    /// <summary>
    /// 잡아 케이지에 넣어야 하는 생명체의 종류.
    /// 인덱스 정렬은 ZooHintTable 의 mappings 배열과 1:1 대응시켜 사용한다.
    /// </summary>
    public enum CreatureKind
    {
        Dragonfly = 0, // 날아다님 — 잠자리채로 잡음, 잡으면 힌트 노출
        Lizard    = 1, // 빠르게 기어다님 — 손으로 직접 잡음
        Crab      = 2, // 무겁고 느림 — 강한 임팩트로 셸 모드 토글
        Snake     = 3, // 전기 — 장갑 낀 손에만 잡힘
    }

    /// <summary>
    /// 케이지 식별자. 시각 색과 1:1 대응한다고 가정(인스펙터에서 색 자체는 머티리얼/이미터로 표현).
    /// </summary>
    public enum CageId
    {
        Red    = 0,
        Blue   = 1,
        Green  = 2,
        Yellow = 3,
    }

    /// <summary>
    /// 생명체의 런타임 상태.
    /// 권위(State Authority) 측의 AI 가 이 값을 갱신하고 NetworkBehaviour 의 [Networked] 로 동기화한다.
    /// </summary>
    public enum CreatureState
    {
        Idle      = 0, // 방금 스폰됨 / 비활성
        Wander    = 1, // 평상시 거동
        Fleeing   = 2, // 위협 감지하고 도주
        Stunned   = 3, // 일시 정지(예: 게의 셸 모드, hole 차단 등 외부 요인)
        Captured  = 4, // 도구나 손에 의해 잡힘 — 케이지에 넣기 직전 단계
        Caged     = 5, // 올바른 케이지에 안착 — 최종 상태
    }
}
