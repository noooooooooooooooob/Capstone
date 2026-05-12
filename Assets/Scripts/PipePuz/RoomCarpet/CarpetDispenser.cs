using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

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
            var go = new GameObject("Carpet");

            // 시각 — 얇은 큐브 (카펫이지만 단순화).
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            var visCol = vis.GetComponent<Collider>();
            if (visCol != null) Destroy(visCol);
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = new Vector3(CarpetSize.x, CarpetThickness, CarpetSize.y);
            if (CarpetMaterial != null)
                vis.GetComponent<Renderer>().sharedMaterial = CarpetMaterial;

            // 충돌/잡기 콜라이더 (시각 자식이 아니라 카펫 root 에).
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(CarpetSize.x, CarpetThickness, CarpetSize.y);

            // Rigidbody — 디스펜서 위에 떠 있도록 kinematic + no gravity 로 시작.
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 잡기 — throwOnDetach 켬으로 던지는 손맛.
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = true;
            grab.smoothPosition = false;
            grab.smoothRotation = false;

            // 텔레포트 — 처음엔 비활성, 안착 후 활성.
            var tele = go.AddComponent<TeleportationArea>();
            tele.enabled = false;

            // 라이프사이클 컴포넌트.
            var carpet = go.AddComponent<DisappearingCarpet>();
            carpet.VisualRenderer = vis.GetComponent<Renderer>();
            carpet.TeleportArea = tele;
            carpet.Lifetime = CarpetLifetime;
            carpet.WarningSeconds = CarpetWarningSeconds;

            return go;
        }
    }
}
