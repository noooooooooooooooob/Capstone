using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

// 물리 버튼에 붙이는 스크립트
// XR Simple Interactable 또는 Collider 터치 방식
public class PhysicalButton : MonoBehaviour
{
    [Header("연결할 시스템")]
    public MainControlSystem controlSystem;

    [Header("버튼 눌림 설정")]
    public float pressDepth = 0.01f;        // 버튼이 눌리는 깊이
    public float returnSpeed = 5f;          // 원위치 복귀 속도
    public AudioClip pressSound;

    private Vector3 originalPosition;
    private bool isPressed = false;
    private AudioSource audioSource;

    void Start()
    {
        originalPosition = transform.localPosition;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        // 눌리지 않은 상태면 원위치로 복귀
        if (!isPressed)
        {
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                originalPosition,
                Time.deltaTime * returnSpeed
            );
        }
    }

    // Collider의 OnTriggerEnter로 손 감지
    void OnTriggerEnter(Collider other)
    {
        // XR 손 레이어이거나 태그가 Hand인 경우
        if (other.CompareTag("Hand") || other.gameObject.layer == LayerMask.NameToLayer("XRHand"))
        {
            PressButton();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Hand") || other.gameObject.layer == LayerMask.NameToLayer("XRHand"))
        {
            isPressed = false;
        }
    }

    void PressButton()
    {
        if (isPressed) return;
        isPressed = true;

        // 버튼 눌림 시각 효과
        transform.localPosition = originalPosition - new Vector3(0, pressDepth, 0);

        // 사운드
        if (pressSound && audioSource)
            audioSource.PlayOneShot(pressSound);

        // 시스템 호출
        if (controlSystem)
            controlSystem.OnStabilizeButtonPressed();

        Debug.Log("버튼 눌림!");
    }
}