using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz
{
    public enum PipeKind
    {
        /// <summary>고장난 주황색 파이프(초기 RadiatorB 의 PipeSocket 에 꽂혀있다).</summary>
        Broke,
        /// <summary>교체용으로 사용할 정상 파이프.</summary>
        New
    }

    /// <summary>
    /// XRGrabInteractable 로 잡고 옮길 수 있는 파이프.
    /// 손에서 놓는 순간 가장 가까운 EligibleSockets 에 스냅을 시도한다.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PipeGrabbable : MonoBehaviour
    {
        [Tooltip("이 파이프의 종류. Pipe_Broke 인지 Pipe_New 인지 식별한다.")]
        public PipeKind Kind = PipeKind.Broke;

        [Tooltip("이 파이프가 들어갈 수 있는 PipeSocket 후보들. 보통 RadiatorB 의 PipeSocket 하나.")]
        public PipeSocket[] EligibleSockets;

        XRGrabInteractable _grab;
        Rigidbody _rb;
        PipeSocket _currentSocket;

        public PipeSocket CurrentSocket => _currentSocket;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.useGravity = false;

            _grab.selectEntered.AddListener(OnGrabbed);
            _grab.selectExited.AddListener(OnReleased);
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbed);
                _grab.selectExited.RemoveListener(OnReleased);
            }
        }

        public void NotifySnapped(PipeSocket socket)
        {
            _currentSocket = socket;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
        }

        public void NotifyUnsnapped()
        {
            _currentSocket = null;
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (_currentSocket != null)
            {
                _currentSocket.OnPipeRemoved(this);
                _currentSocket = null;
            }
            // XRGrabInteractable 가 잡고 있는 동안엔 isKinematic 을 직접 다루지 않는다.
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (EligibleSockets == null || EligibleSockets.Length == 0)
            {
                // 그냥 떠다니지 않게 정지.
                if (_rb != null) _rb.isKinematic = true;
                return;
            }

            PipeSocket best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < EligibleSockets.Length; i++)
            {
                var s = EligibleSockets[i];
                if (s == null) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }

            if (best != null && bestDist <= best.SnapRadius)
            {
                best.TrySnap(this);
            }
            else
            {
                // 범위 밖이면 그냥 그 자리에 멈춘다(중력/관성 없음).
                if (_rb != null)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    _rb.isKinematic = true;
                }
            }
        }
    }
}
