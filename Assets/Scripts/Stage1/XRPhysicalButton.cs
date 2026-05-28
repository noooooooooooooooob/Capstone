using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Fusion;

namespace Stage1
{
    /// <summary>
    /// A networked physical button using XR Interaction Toolkit.
    /// Synchronizes the visual press state across the network.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class XRPhysicalButton : NetworkBehaviour
    {
        [Header("버튼 눌림 효과")]
        public float pressDepth = 0.005f;       // 눌리는 깊이
        public float returnSpeed = 8f;          // 복귀 속도
        public Color normalColor = Color.gray;
        public Color pressedColor = Color.white;

        [Header("연결할 기능 (하나만 연결)")]
        public BatteryDispenser dispenser;      // 디스펜서 버튼이면 연결
        public MainControlSystem controlSystem; // 메인 컨트롤 버튼이면 연결

        [Header("쿨다운")]
        public float cooldown = 0.5f;

        [Networked]
        public NetworkBool IsPressed { get; set; }

        private Vector3 originalLocalPos;
        private XRSimpleInteractable interactable;
        private Renderer rend;
        private bool isOnCooldown = false;

        public override void Spawned()
        {
            originalLocalPos = transform.localPosition;
            rend = GetComponent<Renderer>();
            interactable = GetComponent<XRSimpleInteractable>();

            if (rend) rend.material.color = IsPressed ? pressedColor : normalColor;
            
            // Register interaction listener only locally
            interactable.selectEntered.AddListener(OnPressed);
        }

        public override void Render()
        {
            // Update visual position and color based on IsPressed state
            Vector3 targetPos = IsPressed ? originalLocalPos - (transform.up * pressDepth) : originalLocalPos;
            
            if (Vector3.Distance(transform.localPosition, targetPos) > 0.0001f)
            {
                transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * returnSpeed);
            }

            if (rend)
            {
                Color targetColor = IsPressed ? pressedColor : normalColor;
                rend.material.color = Color.Lerp(rend.material.color, targetColor, Time.deltaTime * returnSpeed);
            }
        }

        void OnPressed(SelectEnterEventArgs args)
        {
            if (isOnCooldown) return;
            
            if (Object.HasStateAuthority)
            {
                StartCoroutine(PressRoutine());
            }
            else
            {
                RpcRequestPress();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RpcRequestPress()
        {
            if (isOnCooldown) return;
            StartCoroutine(PressRoutine());
        }

        IEnumerator PressRoutine()
        {
            isOnCooldown = true;
            IsPressed = true;

            // 기능 실행
            if (dispenser != null)
                dispenser.OnDispenseButtonPressed();

            if (controlSystem != null)
                controlSystem.OnStabilizeButtonPressed();

            yield return new WaitForSeconds(0.15f);

            IsPressed = false;

            yield return new WaitForSeconds(cooldown);
            isOnCooldown = false;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnPressed);
        }
    }
}