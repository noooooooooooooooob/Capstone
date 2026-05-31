using UnityEngine;
using UnityEngine.UI;
using Fusion;

namespace Stage1
{
    /// <summary>
    /// Dispenses a frozen battery pre-tagged with a specific color.
    /// A world-space canvas on the dispenser shows a colored swatch hinting
    /// which LightBall the player needs to bring to the thawing machine.
    /// </summary>
    public class BatteryDispenser : NetworkBehaviour
    {
        [Header("Battery Settings")]
        public GameObject batteryPrefab; // Changed back to GameObject to preserve Unity serialization
        public Transform spawnPoint;

        [Tooltip("Color of the ball this battery must be paired with in the thawing machine.")]
        public LightBallColor batteryColor = LightBallColor.Red;

        [Header("Settings")]
        public float spawnCooldown = 1f;

        [Header("Hint Canvas")]
        [Tooltip("Assign an existing Image to use as the color swatch. Leave empty to auto-create a canvas.")]
        public Image hintSwatchImage;

        [Tooltip("Size of the auto-created canvas in world units.")]
        public Vector2 autoCanvasWorldSize = new Vector2(0.15f, 0.15f);

        [Tooltip("Local position offset of the auto-created canvas relative to the dispenser.")]
        public Vector3 autoCanvasOffset = new Vector3(0f, 0.1f, 0.05f);

        // Maps LightBallColor enum index to a visible Unity Color.
        static readonly Color[] BallColors =
        {
            new Color(1f,  0.2f, 0.2f), // Red
            new Color(1f,  0.9f, 0.1f), // Yellow
            new Color(0.2f, 0.5f, 1f),  // Blue
        };

        [Networked]
        NetworkId _currentBatteryId { get; set; }

        [Networked]
        TickTimer _spawnCooldownTimer { get; set; }

        /// <summary>Set by MultiBatterySlotPanel once a battery of this color is permanently inserted.</summary>
        [Networked]
        public NetworkBool Locked { get; set; }

        public override void Spawned()
        {
            SetupHintCanvas();
        }

        void SetupHintCanvas()
        {
            if (hintSwatchImage == null)
            {
                // ── Auto-create a world-space canvas ──────────────────
                var canvasGo = new GameObject("DispenserHintCanvas");
                canvasGo.transform.SetParent(transform, false);
                canvasGo.transform.localPosition = autoCanvasOffset;
                canvasGo.transform.localRotation = Quaternion.identity;

                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;

                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 100f;

                canvasGo.AddComponent<GraphicRaycaster>();

                var rt = (RectTransform)canvasGo.transform;
                float ppu = 100f;
                rt.sizeDelta = new Vector2(ppu, ppu);
                float worldToCanvasScale = autoCanvasWorldSize.x / ppu;
                canvasGo.transform.localScale = Vector3.one * worldToCanvasScale;

                var swatchGo = new GameObject("ColorSwatch", typeof(RectTransform), typeof(Image));
                swatchGo.transform.SetParent(canvasGo.transform, false);

                var swatchRect = (RectTransform)swatchGo.transform;
                swatchRect.anchorMin = Vector2.zero;
                swatchRect.anchorMax = Vector2.one;
                swatchRect.offsetMin = Vector2.zero;
                swatchRect.offsetMax = Vector2.zero;

                hintSwatchImage = swatchGo.GetComponent<Image>();
            }

            int idx = (int)batteryColor;
            hintSwatchImage.color = (idx >= 0 && idx < BallColors.Length) ? BallColors[idx] : Color.white;
        }

        /// <summary>Permanently disables this dispenser. Called by MultiBatterySlotPanel on successful insertion.</summary>
        public void Lock()
        {
            if (Object.HasStateAuthority) Locked = true;
            else RpcLock();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RpcLock() => Locked = true;

        public void OnDispenseButtonPressed()
        {
            if (Locked) return;
            if (!_spawnCooldownTimer.ExpiredOrNotRunning(Runner)) return;

            if (Object.HasStateAuthority)
            {
                SpawnBattery();
            }
            else
            {
                RpcRequestSpawn();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RpcRequestSpawn()
        {
            if (!_spawnCooldownTimer.ExpiredOrNotRunning(Runner)) return;
            SpawnBattery();
        }

        void SpawnBattery()
        {
            if (batteryPrefab == null)
            {
                Debug.LogError("[BatteryDispenser] batteryPrefab is null!");
                return;
            }

            NetworkObject netPrefab = batteryPrefab.GetComponent<NetworkObject>();
            if (netPrefab == null)
            {
                Debug.LogError("[BatteryDispenser] batteryPrefab does not have a NetworkObject!");
                return;
            }

            // Despawn the previous battery if one still exists
            if (_currentBatteryId != default &&
                Runner.TryFindObject(_currentBatteryId, out NetworkObject existing))
            {
                Runner.Despawn(existing);
            }
            _currentBatteryId = default;

            Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 0.2f;
            Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            Runner.Spawn(netPrefab, spawnPos, spawnRot, Runner.LocalPlayer, (runner, obj) =>
            {
                var state = obj.GetComponent<BatteryState>();
                if (state != null)
                {
                    state.Color = batteryColor;
                    state.IsMelted = false;
                }

                // Legacy support if needed
                var colorTag = obj.GetComponent<BatteryColorTag>();
                if (colorTag != null) colorTag.color = batteryColor;

                // ── 양쪽 플레이어가 모두 잡을 수 있게 보장 ──────────────────────────
                // Battery.prefab 에는 권위 이전 컴포넌트(NetworkGrabbableSync)가 없어서,
                // 스폰한 권한자만 실제로 움직일 수 있고 상대가 잡으면 NetworkTransform 이
                // 위치를 되돌려 "안 잡히는" 것처럼 보였다. GrabAuthorityHandover 는
                // MonoBehaviour 라 런타임 AddComponent 로 즉시 동작하며, 잡는 순간
                // RequestStateAuthority 로 권위를 끌어와 그때부터 위치가 상대에게 전파된다.
                // (NetworkObject.AllowStateAuthorityOverride 플래그가 이미 켜져 있어 동작함.)
                if (obj.GetComponent<GrabAuthorityHandover>() == null &&
                    obj.GetComponent<NetworkGrabbableSync>() == null)
                {
                    obj.gameObject.AddComponent<GrabAuthorityHandover>();
                }

                _currentBatteryId = obj.Id;
            });

            _spawnCooldownTimer = TickTimer.CreateFromSeconds(Runner, spawnCooldown);
        }
    }
}

