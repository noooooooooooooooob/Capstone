using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Capstone.Puzzle
{
    /// <summary>
    /// XR Interaction Toolkit 기반 밸브 그랩 핸들러.
    /// (Capstone_P6의 OVRInput 기반 ControllerValveGrabber 대체 버전)
    ///
    /// 동작 흐름:
    ///   1. 핸들에 부착된 XRSimpleInteractable(이 스크립트)이 영역 안 컨트롤러를 인식
    ///   2. XR Direct Interactor / Ray Interactor가 Select 시작 → BeginGrab(인터랙터 Transform)
    ///   3. 손을 돌리면 <see cref="ValveRotationGrab"/>이 회전 델타를 측정해
    ///      <see cref="RadiatorValve.ApplyRotationDelta(float)"/>를 호출 (Photon Fusion 동기화)
    ///   4. Select 종료 → EndGrab()
    ///
    /// 별도 OVRInput / Meta XR SDK 없이, 순수 XR Interaction Toolkit + Input System으로 동작.
    /// 좌/우 컨트롤러 모두 자동 처리된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRControllerValveGrabber : XRSimpleInteractable
    {
        [Header("연결")]
        [Tooltip("회전 처리를 담당하는 ValveRotationGrab 컴포넌트. 비워두면 같은 GameObject에서 자동 탐색.")]
        [SerializeField] ValveRotationGrab valveGrab;

        [Header("옵션")]
        [Tooltip("AttachTransform이 설정되어 있으면 그것을, 아니면 인터랙터 Transform을 그랩 기준점으로 사용한다.")]
        [SerializeField] bool preferAttachTransform = true;

        protected override void Awake()
        {
            base.Awake();
            if (valveGrab == null) valveGrab = GetComponent<ValveRotationGrab>();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (valveGrab == null) return;

            var t = ResolveGrabTransform(args);
            if (t != null) valveGrab.BeginGrab(t);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (valveGrab == null) return;
            valveGrab.EndGrab();
        }

        Transform ResolveGrabTransform(SelectEnterEventArgs args)
        {
            if (args.interactorObject == null) return null;

            // 우선 AttachTransform(컨트롤러의 가상 손 위치)을 사용한다.
            if (preferAttachTransform)
            {
                var attach = args.interactorObject.GetAttachTransform(this);
                if (attach != null) return attach;
            }

            return args.interactorObject.transform;
        }

        // ---------------------------------------------------------------------
        // UnityEvent 직접 연결용 보조 메서드 (XR 외 입력에서 호출 가능)
        // ---------------------------------------------------------------------

        /// <summary>외부 그랩 시스템에서 직접 호출 가능. UnityEvent 시그니처 호환.</summary>
        public void BeginGrabExternal(Transform grabber)
        {
            if (valveGrab != null) valveGrab.BeginGrab(grabber);
        }

        /// <summary>외부 그랩 시스템에서 직접 호출 가능.</summary>
        public void EndGrabExternal()
        {
            if (valveGrab != null) valveGrab.EndGrab();
        }
    }
}
