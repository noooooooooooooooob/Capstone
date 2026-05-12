using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 도착 영역. 매 프레임 Camera.main 의 world 위치가 자기 Collider.bounds 안에 들어왔는지 검사.
    /// 첫 도착 시 OnReached 발행. 이후 호출은 무시 (단방향).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CarpetGoalZone : MonoBehaviour
    {
        public UnityEvent OnReached;

        Collider _col;
        bool _reached;

        public bool IsReached => _reached;

        void Awake()
        {
            _col = GetComponent<Collider>();
        }

        void Update()
        {
            if (_reached || _col == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (_col.bounds.Contains(cam.transform.position))
            {
                _reached = true;
                OnReached?.Invoke();
                Debug.Log("[RoomCarpet] Goal reached!");
            }
        }
    }
}
