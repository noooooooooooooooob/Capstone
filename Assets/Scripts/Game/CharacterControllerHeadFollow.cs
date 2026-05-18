using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// VR에서 사용자가 실제로 걸어 머리(헤드셋)가 벽을 뚫고 들어가는 것을 막는다.
/// - 매 LateUpdate에 CharacterController.Move로 CC를 카메라 XZ 위치로 따라가게 함 → 벽이 있으면 CC가 그 자리에 정지
/// - cc.Move는 부모 GameObject(XR Origin) 자체를 이동시키므로, 자식인 카메라도 같은 양만큼 끌려가서 "이중 이동"이 일어남.
///   이를 상쇄하려고 Camera Floor Offset을 horizontalDelta만큼 반대로 밀어 카메라 월드 위치를 head-track 그대로(벽 막힘 시엔 벽 앞)로 클램프.
/// 결과적으로 조이스틱 이동과 물리 워킹 모두 CC를 거쳐 벽을 통과하지 않는다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(XROrigin))]
public class CharacterControllerHeadFollow : MonoBehaviour
{
    [Tooltip("머리 카메라. 비워두면 XROrigin.Camera 자동 사용.")]
    [SerializeField] Transform headCameraOverride;

    [Tooltip("Camera Floor Offset 트랜스폼. 비워두면 XROrigin.CameraFloorOffsetObject 자동 사용.")]
    [SerializeField] Transform cameraOffsetOverride;

    CharacterController cc;
    XROrigin origin;
    Transform headCamera;
    Transform cameraOffset;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        origin = GetComponent<XROrigin>();

        headCamera = headCameraOverride != null
            ? headCameraOverride
            : (origin.Camera != null ? origin.Camera.transform : null);

        cameraOffset = cameraOffsetOverride != null
            ? cameraOffsetOverride
            : (origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : null);
    }

    void LateUpdate()
    {
        if (headCamera == null || cameraOffset == null) return;

        Vector3 ccCenterWorld = transform.TransformPoint(cc.center);
        Vector3 cameraWorld = headCamera.position;

        Vector3 horizontalDelta = new Vector3(
            cameraWorld.x - ccCenterWorld.x,
            0f,
            cameraWorld.z - ccCenterWorld.z);

        if (horizontalDelta.sqrMagnitude < 1e-8f) return;

        cc.Move(horizontalDelta);
        cameraOffset.position -= horizontalDelta;
    }
}
