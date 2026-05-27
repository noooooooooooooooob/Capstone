using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1
{
    /// <summary>
    /// Shared 모드 네트워크에서 "잡고 놓으면 사라짐" 보강 컴포넌트.
    /// GrabAuthorityHandover + NetworkObject.AllowStateAuthorityOverride 만으론
    /// 권위 이전이 인스턴트가 아니라서 (~1 RTT) 그 갭 동안 NetworkTransform이
    /// 권위 위치로 되감기 → 손 따라오다 끊김 → 놓으면 throwOnDetach 속도가 더해져 멀리 사라짐.
    ///
    /// 이 컴포넌트는:
    ///   1) Select 진입 시 NetworkTransform을 일시 비활성 (로컬에서 자유롭게 움직임)
    ///   2) throwOnDetach 강제 OFF (놓을 때 속도 안 추가)
    ///   3) Select 종료 시 NetworkTransform 다시 활성 + 다음 권위 측 sync 받음
    /// </summary>
    [RequireComponent(typeof(XRBaseInteractable))]
    [DisallowMultipleComponent]
    public class GrabNetworkSyncPause : MonoBehaviour
    {
        [Tooltip("잡을 때 NetworkTransform 일시 비활성. 권위 이전 RTT 갭에서 위치 되감김 방지.")]
        public bool pauseNetworkTransformWhileHeld = true;

        [Tooltip("놓을 때 XRGrabInteractable의 throwOnDetach 강제 OFF. 던짐 속도로 시야 밖 텔레포트 방지.")]
        public bool forceNoThrowOnDetach = true;

        [Tooltip("진단 로그 출력.")]
        public bool verboseLog = false;

        XRBaseInteractable _interactable;
        NetworkTransform _netTransform;
        XRGrabInteractable _grab;

        void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();
            _netTransform = GetComponent<NetworkTransform>();
            _grab = GetComponent<XRGrabInteractable>();

            if (forceNoThrowOnDetach && _grab != null)
                _grab.throwOnDetach = false;
        }

        void OnEnable()
        {
            if (_interactable == null) return;
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (_interactable == null) return;
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs _)
        {
            if (pauseNetworkTransformWhileHeld && _netTransform != null)
            {
                _netTransform.enabled = false;
                if (verboseLog) Debug.Log($"[GNSP:{name}] NetworkTransform paused.", this);
            }
            if (forceNoThrowOnDetach && _grab != null)
                _grab.throwOnDetach = false;
        }

        void OnSelectExited(SelectExitEventArgs _)
        {
            if (pauseNetworkTransformWhileHeld && _netTransform != null)
            {
                _netTransform.enabled = true;
                if (verboseLog) Debug.Log($"[GNSP:{name}] NetworkTransform resumed.", this);
            }
        }
    }
}
