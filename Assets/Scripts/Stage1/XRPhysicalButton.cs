using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
// Cube 오브젝트에 붙이는 물리 버튼
// XR Simple Interactable 컴포넌트와 함께 사용
[RequireComponent(typeof(XRSimpleInteractable))]
public class XRPhysicalButton : MonoBehaviour
{
    [Header("버튼 눌림 효과")]
    public float pressDepth = 0.005f;       // 눌리는 깊이
    public float returnSpeed = 8f;          // 복귀 속도
    public Color normalColor = Color.gray;
    public Color pressedColor = Color.white;

    [Header("연결할 기능 (하나만 연결)")]
    public BatteryDispenser dispenser;      // 디스펜서 버튼이면 연결
    public MainControlSystem controlSystem; // 메인 컨트롤 버튼이면 연결

    [Header("쿨다운")]
    public float cooldown = 0.5f;

    private Vector3 originalLocalPos;
    private XRSimpleInteractable interactable;
    private Renderer rend;
    private bool isOnCooldown = false;

    void Start()
    {
        originalLocalPos = transform.localPosition;
        rend = GetComponent<Renderer>();
        if (rend) rend.material.color = normalColor;

        interactable = GetComponent<XRSimpleInteractable>();

        // Ray 또는 손으로 Select(잡기/클릭)했을 때
        interactable.selectEntered.AddListener(OnPressed);
    }

    void OnPressed(SelectEnterEventArgs args)
    {
        if (isOnCooldown) return;
        StartCoroutine(PressRoutine());
    }

    IEnumerator PressRoutine()
    {
        isOnCooldown = true;

        // 눌림 효과
        transform.localPosition = originalLocalPos - new Vector3(0, pressDepth, 0);
        if (rend) rend.material.color = pressedColor;

        // 기능 실행
        if (dispenser != null)
            dispenser.OnDispenseButtonPressed();

        if (controlSystem != null)
            controlSystem.OnStabilizeButtonPressed();

        yield return new WaitForSeconds(0.15f);

        // 복귀
        transform.localPosition = originalLocalPos;
        if (rend) rend.material.color = normalColor;

        yield return new WaitForSeconds(cooldown);
        isOnCooldown = false;
    }

    void OnDestroy()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnPressed);
    }
}