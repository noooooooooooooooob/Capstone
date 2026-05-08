using UnityEngine;

namespace PipePuz
{
    /// <summary>
    /// Pipe_Broke 가 위치한 자리이자 Pipe_New 가 들어갈 자리.
    /// 이 소켓의 위치/회전이 곧 스냅된 파이프의 최종 포즈가 된다.
    /// 시각은 보통 반투명 큐브.
    /// </summary>
    public class PipeSocket : MonoBehaviour
    {
        [Tooltip("이 소켓이 속한 라디에이터 컨트롤러. 상태 변화를 알린다.")]
        public RadiatorController Radiator;

        [Tooltip("스냅이 발생할 거리(m). 손에서 놓을 때 이 거리 안이면 자석처럼 붙는다.")]
        public float SnapRadius = 0.2f;

        [Tooltip("씬 시작 시 미리 꽂혀있는 파이프(보통 Pipe_Broke).")]
        public PipeGrabbable InitialPipe;

        PipeGrabbable _currentPipe;
        public PipeGrabbable CurrentPipe => _currentPipe;
        public PipeKind? CurrentKind => _currentPipe != null ? _currentPipe.Kind : (PipeKind?)null;

        void Start()
        {
            if (InitialPipe != null)
            {
                TrySnap(InitialPipe, immediate: true);
            }
            else
            {
                NotifyRadiator();
            }
        }

        public bool TrySnap(PipeGrabbable pipe, bool immediate = false)
        {
            if (pipe == null) return false;

            // 이미 다른 파이프가 들어있으면 그건 떠난 상태로 둔다(보통 OnGrabbed 에서 처리됨).
            if (_currentPipe != null && _currentPipe != pipe)
            {
                _currentPipe.NotifyUnsnapped();
                _currentPipe = null;
            }

            pipe.transform.SetParent(transform, worldPositionStays: false);
            pipe.transform.localPosition = Vector3.zero;
            pipe.transform.localRotation = Quaternion.identity;
            pipe.NotifySnapped(this);
            _currentPipe = pipe;

            NotifyRadiator();
            return true;
        }

        public void OnPipeRemoved(PipeGrabbable pipe)
        {
            if (_currentPipe == pipe)
            {
                _currentPipe = null;
                pipe.transform.SetParent(null, worldPositionStays: true);
                NotifyRadiator();
            }
        }

        void NotifyRadiator()
        {
            if (Radiator != null)
            {
                Radiator.OnSocketContentChanged(CurrentKind);
            }
        }
    }
}
