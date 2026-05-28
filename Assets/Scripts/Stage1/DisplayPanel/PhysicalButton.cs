using System.Collections;
using UnityEngine;
using Fusion;

// 물리 버튼 — 손 콜라이더 터치로 눌림.
// 눌림 위치/사운드를 네트워크로 동기화해 모든 피어에서 동일하게 보이고 들린다.
// (예전엔 누른 사람 클라이언트에서만 눌림/사운드가 났음. 기능 호출은 RPC라 동작은 동기화됐었다.)
// 요구: 이 GameObject 에 NetworkObject 컴포넌트가 있어야 한다.
public class PhysicalButton : NetworkBehaviour
{
    [Header("연결할 시스템")]
    public MainControlSystem controlSystem;

    [Header("버튼 눌림 설정")]
    public float pressDepth = 0.01f;        // 버튼이 눌리는 깊이
    public float returnSpeed = 5f;          // 원위치 복귀 속도
    public AudioClip pressSound;

    [Header("타이밍")]
    [Tooltip("눌린 상태 유지 시간(초)")]
    public float pressedDuration = 0.15f;
    [Tooltip("연속 입력 방지 쿨다운(초)")]
    public float cooldown = 0.5f;

    [Networked, OnChangedRender(nameof(OnPressedChanged))]
    public NetworkBool IsPressed { get; set; }

    private Vector3 originalPosition;
    private AudioSource audioSource;
    private bool isOnCooldown;

    public override void Spawned()
    {
        originalPosition = transform.localPosition;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    public override void Render()
    {
        // 눌림 상태(네트워크)에 따라 위치 보간 — 모든 피어에서 동일.
        Vector3 target = IsPressed
            ? originalPosition - new Vector3(0, pressDepth, 0)
            : originalPosition;

        if ((transform.localPosition - target).sqrMagnitude > 0.0000001f)
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * returnSpeed);
    }

    // Collider의 OnTriggerEnter로 손 감지
    void OnTriggerEnter(Collider other)
    {
        if (!IsHand(other)) return;
        TryPress();
    }

    bool IsHand(Collider other)
    {
        return other.CompareTag("Hand") || other.gameObject.layer == LayerMask.NameToLayer("XRHand");
    }

    void TryPress()
    {
        if (isOnCooldown) return;
        if (Object == null || !Object.IsValid) return;

        // 누른 사람이 권한자면 직접, 아니면 권한자에 요청 — [Networked] 는 권한자만 쓸 수 있음.
        if (Object.HasStateAuthority)
            StartCoroutine(PressRoutine());
        else
            RpcRequestPress();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestPress()
    {
        if (isOnCooldown) return;
        StartCoroutine(PressRoutine());
    }

    IEnumerator PressRoutine()
    {
        isOnCooldown = true;
        IsPressed = true;

        if (controlSystem != null)
            controlSystem.OnStabilizeButtonPressed();

        yield return new WaitForSeconds(pressedDuration);
        IsPressed = false;

        yield return new WaitForSeconds(cooldown);
        isOnCooldown = false;
    }

    // IsPressed 가 false→true 로 동기화될 때 모든 피어에서 실행 — 클릭 사운드 재생.
    void OnPressedChanged()
    {
        if (!IsPressed) return;
        if (pressSound != null && audioSource != null)
            audioSource.PlayOneShot(pressSound);
    }
}
