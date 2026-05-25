using System.Collections.Generic;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// LightOrbSocket 의 OnOrbInserted/Removed 이벤트를 받아 지정 MeshRenderer 들의 머티리얼을
    /// OnMaterial(amber 발광) / OffMaterial(dark) 으로 swap.
    ///
    /// 사용:
    ///   1. LightOrbSocket 과 같은 GameObject 또는 자식에 부착.
    ///   2. Socket 필드 = LightOrbSocket 스크립트
    ///   3. LEDs 리스트 = 변경할 MeshRenderer 들
    ///   4. OffMaterial / OnMaterial 머티리얼 지정
    ///
    /// Awake 에서 자동 구독 + 초기 상태는 Off.
    /// </summary>
    [DisallowMultipleComponent]
    public class OrbDockLEDController : MonoBehaviour
    {
        [Header("Source")]
        public LightOrbSocket Socket;

        [Header("Targets")]
        public List<MeshRenderer> LEDs = new List<MeshRenderer>();

        [Header("Materials")]
        public Material OffMaterial;
        public Material OnMaterial;

        bool _subscribed;

        void Awake()
        {
            TrySubscribe();
            SetOn(false);
        }

        void OnDestroy()
        {
            if (_subscribed && Socket != null)
            {
                Socket.OnOrbInserted.RemoveListener(HandleInserted);
                Socket.OnOrbRemoved.RemoveListener(HandleRemoved);
                _subscribed = false;
            }
        }

        void TrySubscribe()
        {
            if (_subscribed) return;
            if (Socket == null)
            {
                Socket = GetComponentInParent<LightOrbSocket>();
                if (Socket == null) return;
            }
            Socket.OnOrbInserted.AddListener(HandleInserted);
            Socket.OnOrbRemoved.AddListener(HandleRemoved);
            _subscribed = true;
        }

        void HandleInserted() { SetOn(true); }
        void HandleRemoved()  { SetOn(false); }

        public void SetOn(bool on)
        {
            var mat = on ? OnMaterial : OffMaterial;
            if (mat == null) return;
            foreach (var r in LEDs)
            {
                if (r == null) continue;
                r.sharedMaterial = mat;
            }
        }
    }
}
