using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 카펫을 무한 공급하는 디스펜서.
    /// Start 에서 첫 카펫을 SpawnPoint 위에 띄워두고,
    /// 사용자가 그 카펫을 잡으면 <see cref="OnCarpetTaken"/> 콜백을 받아 즉시 다음 카펫을 spawn.
    ///
    /// 카펫 GO 는 코드로 직접 만든다 — Editor 빌드와 runtime 양쪽에서 일관된 셋업.
    /// </summary>
    public class CarpetDispenser : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("카펫이 떠 있을 위치/회전. 비워두면 디스펜서 자기 transform.")]
        public Transform SpawnPoint;

        [Tooltip("생성된 카펫들이 들어갈 부모. 비워두면 디스펜서 자기 transform.")]
        public Transform ActiveCarpetsRoot;

        [Header("Carpet config")]
        public Material CarpetMaterial;
        public Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        public float CarpetThickness = 0.02f;
        public float CarpetLifetime = 5f;
        public float CarpetWarningSeconds = 1.5f;

        [Header("Floating mode (Cliff variant)")]
        [Tooltip("켜면 이 디스펜서가 생성하는 모든 카펫이 floating mode 로 동작 — y=FloatingY 에 anchor.")]
        public bool UseFloatingMode = false;
        public float FloatingY = 0.05f;

        DisappearingCarpet _nextCarpet;

        void Start()
        {
            if (SpawnPoint == null) SpawnPoint = transform;
            if (ActiveCarpetsRoot == null) ActiveCarpetsRoot = transform;
            SpawnNextCarpet();
        }

        public DisappearingCarpet SpawnNextCarpet()
        {
            var go = CreateCarpetInstance();
            go.transform.SetParent(ActiveCarpetsRoot, false);
            go.transform.SetPositionAndRotation(SpawnPoint.position, SpawnPoint.rotation);
            var carpet = go.GetComponent<DisappearingCarpet>();
            carpet.Dispenser = this;
            // Floating mode 전파.
            carpet.UseFloatingMode = UseFloatingMode;
            carpet.FloatingY = FloatingY;
            _nextCarpet = carpet;
            return carpet;
        }

        public void OnCarpetTaken(DisappearingCarpet taken)
        {
            if (taken != _nextCarpet) return;
            SpawnNextCarpet();
        }

        GameObject CreateCarpetInstance()
        {
            return BuildCarpetGameObject(
                name: "Carpet",
                material: CarpetMaterial,
                size: CarpetSize,
                thickness: CarpetThickness,
                lifetime: CarpetLifetime,
                warningSeconds: CarpetWarningSeconds);
        }

        /// <summary>
        /// 디스펜서·런처가 공통으로 사용하는 카펫 빌더.
        /// 시각/콜라이더/Rigidbody/XRGrabInteractable/DisappearingCarpet 컴포넌트를 일관되게 부착.
        /// 초기 상태는 kinematic + no gravity — 호출 측이 위치 배치 후 사용 (디스펜서는 그대로 두고
        /// 런처는 곧바로 <see cref="DisappearingCarpet.Launch"/> 로 비활성/중력 활성).
        /// </summary>
        public static GameObject BuildCarpetGameObject(
            string name,
            Material material,
            Vector2 size,
            float thickness,
            float lifetime,
            float warningSeconds)
        {
            var go = new GameObject(name);

            // 시각 — 얇은 큐브.
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            var visCol = vis.GetComponent<Collider>();
            if (visCol != null) Destroy(visCol);
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = new Vector3(size.x, thickness, size.y);
            if (material != null)
                vis.GetComponent<Renderer>().sharedMaterial = material;

            // 충돌/잡기 콜라이더 (시각 자식이 아니라 카펫 root 에).
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, thickness, size.y);

            // Rigidbody — 시작은 kinematic. 호출 측이 풀어줘야 비행 가능.
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 잡기 — throwOnDetach 켬으로 던지는 손맛 (런처 발사 후에도 공중 캐치 가능).
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = true;
            grab.smoothPosition = false;
            grab.smoothRotation = false;

            // 라이프사이클.
            var carpet = go.AddComponent<DisappearingCarpet>();
            carpet.VisualRenderer = vis.GetComponent<Renderer>();
            carpet.Lifetime = lifetime;
            carpet.WarningSeconds = warningSeconds;

            return go;
        }
    }
}
