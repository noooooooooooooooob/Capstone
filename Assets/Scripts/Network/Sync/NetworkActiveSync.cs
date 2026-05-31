using Fusion;
using UnityEngine;
using UnityEngine.Events;

namespace Capstone.Network.Sync
{
    /// <summary>
    /// "없애기 / 숨기기 / 다시 보이기"를 네트워크로 동기화 (Fusion Shared Mode).
    ///
    /// 한쪽 플레이어가 오브젝트를 제거(예: 불 끄기, 생물 포획, 배터리 소모)하면
    /// 상대 화면에서도 동일하게 사라지게 한다. NetworkTransform(위치)과 달리
    /// 이 컴포넌트는 "보임/안 보임" 상태만 [Networked] 로 동기화한다.
    ///
    /// 중요: NetworkObject가 붙은 이 GameObject 자체를 SetActive(false) 하면 NetworkBehaviour가
    ///       멈춰 동기화가 끊긴다. 따라서 두 가지 안전한 방식만 사용한다.
    ///         (A) visualTarget = 별도 자식 GameObject  → 그 자식을 SetActive 토글
    ///         (B) visualTarget 비움                    → 이 오브젝트의 Renderer(+Collider) enabled 토글
    ///
    /// 사용:
    ///   - 자동: autoMirror = true 면 권위 측이 현재 로컬 표시 상태를 매 틱 읽어 자동 전파.
    ///           (기존 퍼즐 스크립트가 Renderer 끄기/자식 SetActive 로 숨기면 코드 수정 없이 동기화)
    ///   - 수동: 코드/UnityEvent 에서 Hide() / Show() / SetVisible(bool) 호출.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class NetworkActiveSync : NetworkBehaviour
    {
        [Header("대상")]
        [Tooltip("숨김/표시할 시각 루트(이 오브젝트의 자식 권장). " +
                 "비워두면 이 GameObject의 Renderer/Collider enabled 를 토글한다.")]
        public GameObject visualTarget;

        [Header("동작")]
        [Tooltip("권위 측에서 로컬 표시 상태(자식 active 또는 Renderer enabled)를 매 틱 읽어 자동 동기화. " +
                 "기존 스크립트가 코드 수정 없이 숨김을 전파하게 해준다.")]
        public bool autoMirror = true;

        [Tooltip("숨길 때 Collider도 함께 끈다(visualTarget 비운 Renderer 토글 방식에서만 적용).")]
        public bool alsoToggleColliders = true;

        [Header("이벤트")]
        public UnityEvent onShown = new UnityEvent();
        public UnityEvent onHidden = new UnityEvent();

        [Networked, OnChangedRender(nameof(OnVisibleChanged))]
        public NetworkBool Visible { get; set; }

        Renderer[] _renderers;
        Collider[] _colliders;
        bool _started;

        bool TargetIsChildGO => visualTarget != null && visualTarget != gameObject;

        public override void Spawned()
        {
            CacheTargets();
            // 초기값: 현재 로컬 표시 상태를 권위가 기록. 프록시는 받은 값을 적용.
            if (HasStateAuthority) Visible = ReadLocalVisible();
            ApplyVisible(Visible);
            _started = true;
        }

        void CacheTargets()
        {
            if (TargetIsChildGO)
            {
                _renderers = null;
                _colliders = null;
            }
            else
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
                _colliders = alsoToggleColliders ? GetComponentsInChildren<Collider>(true) : null;
            }
        }

        bool ReadLocalVisible()
        {
            if (TargetIsChildGO) return visualTarget.activeSelf;
            if (_renderers != null)
                foreach (var r in _renderers)
                    if (r != null && r.enabled) return true;
            return false;
        }

        public override void FixedUpdateNetwork()
        {
            if (!autoMirror || !HasStateAuthority) return;
            bool cur = ReadLocalVisible();
            if (cur != (bool)Visible) Visible = cur;
        }

        void OnVisibleChanged() => ApplyVisible(Visible);

        void ApplyVisible(bool visible)
        {
            if (TargetIsChildGO)
            {
                if (visualTarget.activeSelf != visible) visualTarget.SetActive(visible);
            }
            else
            {
                if (_renderers != null)
                    foreach (var r in _renderers)
                        if (r != null) r.enabled = visible;
                if (_colliders != null)
                    foreach (var c in _colliders)
                        if (c != null) c.enabled = visible;
            }

            if (!_started) return;
            if (visible) onShown?.Invoke(); else onHidden?.Invoke();
        }

        // ---- 외부 호출 API ----

        /// <summary>이 오브젝트를 모두에게서 숨긴다(없앤다).</summary>
        public void Hide() => SetVisible(false);

        /// <summary>이 오브젝트를 모두에게 다시 표시한다.</summary>
        public void Show() => SetVisible(true);

        /// <summary>표시/숨김을 네트워크로 설정한다. 비권위면 먼저 권위를 요청한다.</summary>
        public void SetVisible(bool v)
        {
            if (Object == null || !Object.IsValid)
            {
                // 네트워크 미초기화(에디터 단독) — 로컬만 적용.
                ApplyVisible(v);
                return;
            }
            if (!HasStateAuthority) Object.RequestStateAuthority();
            Visible = v;
            ApplyVisible(v);
        }
    }
}
