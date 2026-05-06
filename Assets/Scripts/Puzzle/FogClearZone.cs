using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 자기 콜라이더를 씬 안의 모든 <see cref="PipeLeakFog"/> 의 트리거(=킬) 영역으로 등록한다.
    ///
    /// MirrorSphere 같은 "광원 + 시야 확보 구체" 위에 부착하면, 그 구체 영역 안의 연기 입자가
    /// 자동으로 죽어 안개 속에서도 그 영역 내부는 또렷하게 보인다.
    ///
    /// - 부착 위치는 PipeLeakFog 보다 늦게 활성화되어도 OK (PipeLeakFog.Awake → FogClearZone.Start).
    /// - 콜라이더가 비어있으면 자기 자신/자식에서 자동 탐색.
    /// - 비활성/제거 시 등록 해제하여 lingering reference 방지.
    /// </summary>
    [DisallowMultipleComponent]
    public class FogClearZone : MonoBehaviour
    {
        [Tooltip("연기 입자가 들어왔을 때 죽을 영역을 정의하는 콜라이더. 비워두면 자동 탐색.")]
        [SerializeField] Collider zoneCollider;

        [Tooltip("등록 후에도 매 프레임 새 PipeLeakFog 가 생기는지 확인할지. 보통 끔.")]
        [SerializeField] bool keepPolling = false;

        bool _registered;

        void Reset()
        {
            if (zoneCollider == null) zoneCollider = GetComponent<Collider>();
            if (zoneCollider == null) zoneCollider = GetComponentInChildren<Collider>();
        }

        void Start()
        {
            ResolveCollider();
            RegisterToAllFogs();
        }

        void Update()
        {
            if (!keepPolling) return;
            if (!_registered) RegisterToAllFogs();
        }

        void OnDestroy()
        {
            UnregisterFromAllFogs();
        }

        void ResolveCollider()
        {
            if (zoneCollider != null) return;
            zoneCollider = GetComponent<Collider>();
            if (zoneCollider == null) zoneCollider = GetComponentInChildren<Collider>();
        }

        void RegisterToAllFogs()
        {
            if (zoneCollider == null) return;

#if UNITY_2023_1_OR_NEWER
            var fogs = Object.FindObjectsByType<PipeLeakFog>(FindObjectsSortMode.None);
#else
            var fogs = Object.FindObjectsOfType<PipeLeakFog>();
#endif
            if (fogs == null || fogs.Length == 0) return;

            foreach (var fog in fogs)
            {
                fog.AddFogClearCollider(zoneCollider);
            }
            _registered = true;
        }

        void UnregisterFromAllFogs()
        {
            if (zoneCollider == null) return;
#if UNITY_2023_1_OR_NEWER
            var fogs = Object.FindObjectsByType<PipeLeakFog>(FindObjectsSortMode.None);
#else
            var fogs = Object.FindObjectsOfType<PipeLeakFog>();
#endif
            if (fogs == null) return;
            foreach (var fog in fogs)
            {
                if (fog != null) fog.RemoveFogClearCollider(zoneCollider);
            }
            _registered = false;
        }
    }
}
