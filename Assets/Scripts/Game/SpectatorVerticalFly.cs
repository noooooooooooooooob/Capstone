using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 관전자 전용 — 오른쪽 컨트롤러 thumbstick의 상하(Y) 입력으로 XR Origin을 수직 이동시킨다.
/// SpectatorRig.Apply()가 관전자 XR Origin에 런타임으로 부착한다.
///
/// 왼쪽 조이스틱(수평 이동)은 ContinuousMoveProvider가 담당하므로 여기선 Y축만 처리한다.
/// 오른쪽 thumbstick의 좌우(X)는 기존 ContinuousTurnProvider(회전)가 그대로 사용 — 충돌 없음.
/// 중력/충돌은 이미 비활성이라 그냥 트랜스폼을 직접 올리고 내리면 벽도 통과한다.
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class SpectatorVerticalFly : MonoBehaviour
{
    [Tooltip("상하 비행 속도 (m/s).")]
    [SerializeField] float speed = 2f;

    [Tooltip("스틱 미세 입력 무시용 데드존.")]
    [SerializeField] float deadzone = 0.1f;

    [Tooltip("수직 입력으로 쓸 오른쪽 컨트롤러 thumbstick 바인딩 경로.")]
    [SerializeField] string thumbstickBinding = "<XRController>{RightHand}/thumbstick";

    XROrigin _origin;
    InputAction _action;

    void Awake()
    {
        _origin = GetComponent<XROrigin>();
    }

    void OnEnable()
    {
        _action = new InputAction("SpectatorVertical", InputActionType.Value, expectedControlType: "Vector2");
        _action.AddBinding(thumbstickBinding);
        _action.Enable();
    }

    void OnDisable()
    {
        if (_action != null)
        {
            _action.Disable();
            _action.Dispose();
            _action = null;
        }
    }

    void Update()
    {
        if (_action == null) return;

        float y = _action.ReadValue<Vector2>().y;
        if (Mathf.Abs(y) < deadzone) return;

        var rig = _origin != null && _origin.Origin != null ? _origin.Origin.transform : transform;
        rig.position += Vector3.up * (y * speed * Time.deltaTime);
    }
}
