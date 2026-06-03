using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// Stage3 LightOrb 게이트.
    ///
    /// Stage2가 클리어되어 Stage3(LightBeam 퍼즐, <see cref="RevealAtPuzzleIndex"/>)가 시작되기
    /// 전까지 orb를 완전히 숨긴다 — 렌더러/콜라이더/XRGrab 비활성 + Rigidbody kinematic 정지로
    /// "보이지도, 상호작용 되지도, 떨어지지도" 않게 만든다.
    ///
    /// 공개 시점(= GameManager.CurrentPuzzleIndex 가 RevealAtPuzzleIndex 도달)에는
    /// orb가 현재 위치에 그대로 '공중에 떠 있는' 상태(kinematic)로 나타난다.
    /// 이후 플레이어가 처음으로 잡았다 놓으면 기존 <see cref="LightOrb"/> 로직에 따라 낙하한다.
    ///
    /// 네트워크: GameManager.CurrentPuzzleIndex 는 [Networked] 라 모든 피어에 동기화되므로,
    /// 각 피어가 독립적으로 같은 시점에 공개한다(늦게 합류한 피어 포함).
    /// </summary>
    [DisallowMultipleComponent]
    public class Stage3OrbGate : MonoBehaviour
    {
        [Tooltip("이 퍼즐 인덱스가 활성화되면 orb 공개. LightBeam(Stage3) = 2 (= Stage2 클리어 직후).")]
        public int RevealAtPuzzleIndex = 2;

        [Tooltip("공개 후에도 orb를 공중에 띄워 둔다(kinematic). 플레이어가 첫 grab 후 놓으면 LightOrb가 낙하 처리.")]
        public bool FloatAfterReveal = true;

        public bool IsRevealed { get; private set; }

        Renderer[] _renderers;
        Collider[] _colliders;
        XRGrabInteractable _grab;
        Rigidbody _rb;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            Hide();
        }

        void Update()
        {
            if (IsRevealed) return;
            var gm = GameManager.Instance;
            if (gm != null && gm.CurrentPuzzleIndex >= RevealAtPuzzleIndex)
                Reveal();
        }

        /// <summary>orb 를 숨기고 물리를 정지(보이지도/상호작용도/낙하도 안 함).</summary>
        public void Hide()
        {
            IsRevealed = false;
            SetRenderers(false);
            SetColliders(false);
            if (_grab != null) _grab.enabled = false;
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>orb 를 현재 위치에 공개. FloatAfterReveal 이면 공중에 떠 있는 상태로 나타난다.</summary>
        public void Reveal()
        {
            if (IsRevealed) return;
            IsRevealed = true;

            SetRenderers(true);
            SetColliders(true);
            if (_grab != null) _grab.enabled = true;
            if (_rb != null)
            {
                if (FloatAfterReveal)
                {
                    // 공중에 떠 있는 상태 — 잡기 전까지 정지. 첫 grab/release 후 LightOrb 가 낙하 처리.
                    _rb.isKinematic = true;
                    _rb.useGravity = false;
                }
                else
                {
                    _rb.isKinematic = false;
                    _rb.useGravity = true;
                }
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        void SetRenderers(bool on)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = on;
        }

        void SetColliders(bool on)
        {
            if (_colliders == null) return;
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = on;
        }
    }
}
