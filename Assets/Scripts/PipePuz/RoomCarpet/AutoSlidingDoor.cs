using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 양쪽 미닫이 자동문.
    ///
    /// 동작:
    ///   - <see cref="DetectionVolume"/> (isTrigger Collider) 안에 카메라(머리) 또는
    ///     <see cref="AdditionalTargets"/> 의 트랜스폼이 있는지 매 프레임 검사.
    ///   - 들어와 있으면 두 패널이 양옆으로 <see cref="SlideDistance"/> 만큼 슬라이드.
    ///   - 빠지면 다시 닫힘.
    ///
    /// 패널 슬라이드 축은 door root local 의 <see cref="SlideAxisLocal"/> (기본 +X).
    /// 패널 초기 localPosition 이 곧 "닫힌 위치" — Awake / OnValidate 에서 캐시.
    ///
    /// 네트워크 동기화 없이 양 클라이언트에서 동일하게 작동 — 트리거 검사는 로컬 카메라 기준이라
    /// 두 플레이어가 각자 자기 시점에서 문을 여닫는다. Fusion 이 붙으면 서버 권위로 전환할 것.
    /// </summary>
    [DisallowMultipleComponent]
    public class AutoSlidingDoor : MonoBehaviour
    {
        [Header("Panels — door root 의 자식. 닫힌 위치가 초기 localPosition.")]
        public Transform LeftPanel;
        public Transform RightPanel;

        [Header("Slide")]
        [Tooltip("각 패널이 닫힌 위치에서 양쪽으로 이동하는 거리(m). 패널 폭의 절반 이상 권장.")]
        public float SlideDistance = 1.0f;

        [Tooltip("열림 속도 (m/s).")]
        public float OpenSpeed = 2.5f;

        [Tooltip("닫힘 속도 (m/s).")]
        public float CloseSpeed = 1.8f;

        [Tooltip("닫힘 직전 이 시간(s) 만큼 지연. 사용자가 잠깐 빠져나가도 즉시 닫히지 않게.")]
        public float CloseDelay = 0.4f;

        [Header("Detection")]
        [Tooltip("이 트리거 볼륨 안에 카메라가 있으면 문 Open. 비워두면 children 의 첫 trigger collider 사용.")]
        public Collider DetectionVolume;

        [Tooltip("Camera.main 외 추가 감지 트랜스폼 (다른 플레이어 머리, AI 등). 비어 있어도 됨.")]
        public Transform[] AdditionalTargets;

        [Header("Slide axis")]
        [Tooltip("패널이 슬라이드할 축 (door root local 기준). " +
                 "Left 패널은 -axis 방향, Right 패널은 +axis 방향으로 이동.")]
        public Vector3 SlideAxisLocal = Vector3.right;

        [Header("Events")]
        public UnityEvent OnOpened;
        public UnityEvent OnClosed;

        Vector3 _leftClosedPos;
        Vector3 _rightClosedPos;
        bool _initialized;
        bool _shouldBeOpen;
        bool _wasOpen;
        bool _wasClosed = true;
        float _lastInsideTime;

        public bool IsOpenRequested => _shouldBeOpen;

        void Awake()
        {
            CacheClosedPositions();
        }

        void OnValidate()
        {
            CacheClosedPositions();
        }

        void CacheClosedPositions()
        {
            if (_initialized) return;
            if (LeftPanel != null) _leftClosedPos = LeftPanel.localPosition;
            if (RightPanel != null) _rightClosedPos = RightPanel.localPosition;
            _initialized = true;
        }

        /// <summary>외부에서 닫힌 위치를 강제 재설정 (Editor 셋업 후 호출용).</summary>
        public void RecacheClosedPositions()
        {
            _initialized = false;
            CacheClosedPositions();
        }

        void Update()
        {
            UpdateDetection();
            AnimatePanels();
        }

        void UpdateDetection()
        {
            bool inside = IsAnyTargetInside();
            if (inside)
            {
                _lastInsideTime = Time.time;
                _shouldBeOpen = true;
            }
            else
            {
                // CloseDelay 동안 추가로 열림 유지 후 닫힘.
                if (_shouldBeOpen && Time.time - _lastInsideTime >= CloseDelay)
                    _shouldBeOpen = false;
            }
        }

        bool IsAnyTargetInside()
        {
            var col = ResolveDetectionVolume();
            if (col == null) return false;

            var cam = Camera.main;
            if (cam != null && col.bounds.Contains(cam.transform.position)) return true;

            if (AdditionalTargets != null)
            {
                for (int i = 0; i < AdditionalTargets.Length; i++)
                {
                    var t = AdditionalTargets[i];
                    if (t != null && col.bounds.Contains(t.position)) return true;
                }
            }
            return false;
        }

        Collider ResolveDetectionVolume()
        {
            if (DetectionVolume != null) return DetectionVolume;
            // 자식 중 isTrigger 인 첫 콜라이더 사용.
            var cols = GetComponentsInChildren<Collider>(includeInactive: false);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].isTrigger)
                {
                    DetectionVolume = cols[i];
                    return cols[i];
                }
            }
            return null;
        }

        void AnimatePanels()
        {
            if (!_initialized) CacheClosedPositions();

            Vector3 axis = SlideAxisLocal.sqrMagnitude > 1e-6f ? SlideAxisLocal.normalized : Vector3.right;
            Vector3 leftOpen  = _leftClosedPos  - axis * SlideDistance;
            Vector3 rightOpen = _rightClosedPos + axis * SlideDistance;

            float speed = _shouldBeOpen ? OpenSpeed : CloseSpeed;

            if (LeftPanel != null)
            {
                Vector3 to = _shouldBeOpen ? leftOpen : _leftClosedPos;
                LeftPanel.localPosition = Vector3.MoveTowards(
                    LeftPanel.localPosition, to, speed * Time.deltaTime);
            }
            if (RightPanel != null)
            {
                Vector3 to = _shouldBeOpen ? rightOpen : _rightClosedPos;
                RightPanel.localPosition = Vector3.MoveTowards(
                    RightPanel.localPosition, to, speed * Time.deltaTime);
            }

            bool fullyOpen   = _shouldBeOpen  && Approx(LeftPanel, leftOpen) && Approx(RightPanel, rightOpen);
            bool fullyClosed = !_shouldBeOpen && Approx(LeftPanel, _leftClosedPos) && Approx(RightPanel, _rightClosedPos);

            if (fullyOpen && !_wasOpen)
            {
                _wasOpen = true;
                _wasClosed = false;
                OnOpened?.Invoke();
            }
            else if (fullyClosed && !_wasClosed)
            {
                _wasClosed = true;
                _wasOpen = false;
                OnClosed?.Invoke();
            }
            else if (!fullyOpen && !fullyClosed)
            {
                _wasOpen = false;
                _wasClosed = false;
            }
        }

        static bool Approx(Transform t, Vector3 target)
        {
            if (t == null) return true; // 패널이 없으면 항상 만족.
            return (t.localPosition - target).sqrMagnitude < 1e-6f;
        }
    }
}
