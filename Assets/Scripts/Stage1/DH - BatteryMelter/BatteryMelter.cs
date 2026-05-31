using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Stage1;
using Fusion;

public class BatteryMelter : NetworkBehaviour
{
    [Header("References")]
    public Transform glassHinge;
    public Transform glassButton;
    public Transform activateButton;
    public Transform lightBallHole;
    public Transform batterySlot;

    public Material frozenBatteryCore;
    public Material meltedBatteryCore;

    [Header("Settings")]
    public float snapDistance = 0.55f;
    public float glassOpenAngle = 90f;
    public float buttonPressDepth = 0.015f;
    public float animSpeed = 2.5f;

    [Header("Battery Snap Alignment")]
    [Tooltip("batterySlot(BatterySnapLocation) 기준 추가 위치 오프셋(슬롯 로컬 방향, 미터). 0이면 슬롯 위치 그대로.")]
    public Vector3 batterySnapPositionOffset = Vector3.zero;
    [Tooltip("batterySlot 회전 기준 추가 Euler 오프셋(도). 바닥 평면 기준 90° 더 눕히려고 Y축 90°.")]
    public Vector3 batterySnapRotationOffset = new Vector3(0f, 90f, 0f);

    [Header("Light Ball Snap Alignment")]
    [Tooltip("Local position offset applied on top of lightBallHole so the ball sits snug.")]
    public Vector3 lightBallSnapPositionOffset = Vector3.zero;

    [Networked]
    public NetworkBool IsOpen { get; set; }

    bool isAnimating = false;

    GameObject snappedBattery = null;
    GameObject snappedLightBall = null;

    Quaternion hingeClosedRot;
    Quaternion hingeOpenedRot;
    Vector3 glassButtonOrigin;
    Vector3 activateButtonOrigin;

    public override void Spawned()
    {
        hingeClosedRot = glassHinge.localRotation;
        hingeOpenedRot = Quaternion.Euler(
            hingeClosedRot.eulerAngles.x + glassOpenAngle,
            hingeClosedRot.eulerAngles.y,
            hingeClosedRot.eulerAngles.z
        );
        glassButtonOrigin    = glassButton.localPosition;
        activateButtonOrigin = activateButton.localPosition;

        // Initial state
        glassHinge.localRotation = IsOpen ? hingeOpenedRot : hingeClosedRot;

        // Button colors: glass/open = red, activate/thaw = green
        SetButtonColor(glassButton,    new Color(0.85f, 0.12f, 0.12f));
        SetButtonColor(activateButton, new Color(0.12f, 0.75f, 0.22f));
    }

    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    void SetButtonColor(Transform btn, Color color)
    {
        if (btn == null) return;
        var rend = btn.GetComponent<Renderer>();
        if (rend == null) return;
        var mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorID, color);
        rend.SetPropertyBlock(mpb);
    }

    public override void Render()
    {
        // Smoothly animate glass based on networked state if not locally animating
        if (!isAnimating)
        {
            Quaternion targetRot = IsOpen ? hingeOpenedRot : hingeClosedRot;
            if (Quaternion.Angle(glassHinge.localRotation, targetRot) > 0.01f)
            {
                glassHinge.localRotation = Quaternion.Slerp(glassHinge.localRotation, targetRot, Time.deltaTime * animSpeed * 2f);
            }
        }
    }

    void Update()
    {
        // Snapping logic should ideally only run on State Authority to avoid jitter/conflicts
        if (Object != null && Object.HasStateAuthority)
        {
            HandleLightBall();
            HandleBattery();
        }
    }

    // ── Held 판정 ──────────────────────────────────────────

    // XRGrabInteractable.isSelected 는 "잡은 그 클라이언트"에서만 true 인 로컬 값이다.
    // 스냅 로직은 melter 의 State Authority 피어에서만 도므로, 상대 플레이어가 들고 있을 때
    // isSelected 로 판정하면 "안 잡힘"으로 오판 → 손에서 뺏어 스냅하거나, 박힌 걸 못 빼게 된다.
    // NetworkGrabbableSync.IsGrabbed 는 [Networked] 라 모든 피어에서 일치한다.
    static bool IsHeld(GameObject obj)
    {
        // [Networked] IsGrabbed 는 Spawned() 이후에만 접근 가능 — 스폰 전 접근 시 예외가 난다.
        // 스폰됐을 때만 네트워크 값을 읽고, 아니면 로컬 isSelected 로 폴백.
        var ngs = obj.GetComponent<NetworkGrabbableSync>();
        if (ngs != null && ngs.Object != null && ngs.Object.IsValid)
            return ngs.IsGrabbed;

        var grab = obj.GetComponent<XRGrabInteractable>();
        return grab != null && grab.isSelected;
    }

    // ── LightBall ──────────────────────────────────────────

    void HandleLightBall()
    {
        GameObject lb = snappedLightBall;
        if (lb == null)
        {
            GameObject[] all = GameObject.FindGameObjectsWithTag("LightBall");
            float bestD = snapDistance;
            foreach (var b in all)
            {
                if (b == null) continue;
                if (IsHeld(b)) continue;
                float d = Vector3.Distance(b.transform.position, lightBallHole.position);
                if (d < bestD) { bestD = d; lb = b; }
            }
        }
        if (lb == null) return;

        bool isHeld = IsHeld(lb);

        if (snappedLightBall != null)
        {
            if (isHeld)
            {
                StartCoroutine(UnsnapNextFrame(snappedLightBall, false));
            }
            else
            {
                ApplySnap(lb, lightBallHole, lightBallSnapPositionOffset, Vector3.zero);
            }
        }
        else
        {
            if (!isHeld && Vector3.Distance(lb.transform.position, lightBallHole.position) < snapDistance)
            {
                Snap(lb, lightBallHole, lightBallSnapPositionOffset, Vector3.zero, ref snappedLightBall);
            }
        }
    }

    // ── Battery ────────────────────────────────────────────

    void HandleBattery()
    {
        // 유리 열림(IsOpen) 여부와 무관하게 "근처에 대면" 자동으로 가로 스냅하도록 게이트 제거.
        // (충전(Activate)은 여전히 유리를 닫은 상태에서만 동작.)
        if (isAnimating) return;

        if (snappedBattery != null)
        {
            bool isHeld = IsHeld(snappedBattery);

            if (isHeld)
            {
                StartCoroutine(UnsnapNextFrame(snappedBattery, true));
            }
            else
            {
                // 권한을 '가지고 있을 때만' 위치를 고정한다. 권한을 매 프레임 재요청하지 않는다.
                // (초기 권한 확보는 Snap()에서 1회 수행. 여기서 계속 재요청하면, P2가 설치된 배터리를
                //  잡으려고 권한을 가져갈 때 멜터가 도로 뺏어 '줄다리기'가 되어 P2가 못 집는다.
                //  권한을 잃으면 = 상대가 잡아간 것 → 위치를 건드리지 않으면 곧 isHeld 가 true 가 되어 unsnap.)
                var no = snappedBattery.GetComponent<NetworkObject>();
                if (no == null || !no.IsValid || no.HasStateAuthority)
                    ApplyBatterySnap(snappedBattery);
            }
            return;
        }

        GameObject[] allBatteries = GameObject.FindGameObjectsWithTag("Battery");
        foreach (var bat in allBatteries)
        {
            if (IsHeld(bat)) continue;

            if (Vector3.Distance(bat.transform.position, batterySlot.position) < snapDistance)
            {
                Snap(bat, batterySlot, batterySnapPositionOffset, batterySnapRotationOffset, ref snappedBattery);
                break;
            }
        }
    }

    // ── Snap / Unsnap ──────────────────────────────────────

    // Applies position+rotation to an already-snapped object every frame.
    void ApplySnap(GameObject obj, Transform slot, Vector3 posOffset, Vector3 rotOffset)
    {
        obj.transform.position = slot.TransformPoint(posOffset);
        obj.transform.rotation = slot.rotation * Quaternion.Euler(rotOffset);
    }

    // 배터리 전용 스냅 — 사용자가 씬에서 배치/회전한 batterySlot(BatterySnapLocation)에 '정확히' 맞춘다.
    // 슬롯을 어디로 옮기거나 돌려도 그 위치·자세(=기계 안쪽)에 그대로 설치된다.
    // 오프셋은 기본 0이라 슬롯 그대로. 필요 시 슬롯 로컬 방향으로 미세 조정 가능.
    void ApplyBatterySnap(GameObject obj)
    {
        if (batterySlot == null) return;
        obj.transform.position = batterySlot.position + batterySlot.rotation * batterySnapPositionOffset;
        obj.transform.rotation = batterySlot.rotation * Quaternion.Euler(batterySnapRotationOffset);
    }

    void Snap(GameObject obj, Transform slot, Vector3 posOffset, Vector3 rotOffset, ref GameObject snapRef)
    {
        snapRef = obj;

        // 스냅 로직은 '해동기의 권한자'에서 transform 을 움직이지만, 배터리/라이트볼의 권한은
        // 마지막에 잡은 사람에게 있을 수 있다. 권한이 다르면 NetworkTransform 이 위치를 되돌려
        // 스냅이 안 잡힌다. 그래서 대상의 권한을 끌어와 이 피어가 위치를 전파하게 만든다.
        var no = obj.GetComponent<NetworkObject>();
        if (no != null && no.IsValid && !no.HasStateAuthority)
            no.RequestStateAuthority();

        var rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints     = RigidbodyConstraints.FreezeAll;
        }

        ApplySnap(obj, slot, posOffset, rotOffset);
        Debug.Log($"{obj.name} snapped!");
    }

    IEnumerator UnsnapNextFrame(GameObject obj, bool isBattery)
    {
        if (isBattery) snappedBattery = null;
        else snappedLightBall = null;

        var grab = obj.GetComponent<XRGrabInteractable>();
        if (grab) grab.throwOnDetach = false;

        yield return null;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity = true;
        }

        Debug.Log($"{obj.name} unsnapped!");
    }

    // ── Glass Button ───────────────────────────────────────

    public void OnGlassButtonPressed()
    {
        if (isAnimating) return;
        
        if (Object.HasStateAuthority)
        {
            ToggleGlass();
        }
        else
        {
            RpcRequestToggleGlass();
        }
        
        StartCoroutine(PressButtonVisual(glassButton, glassButtonOrigin));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestToggleGlass()
    {
        ToggleGlass();
    }

    void ToggleGlass()
    {
        IsOpen = !IsOpen;
        Debug.Log($"Glass {(IsOpen ? "OPEN" : "CLOSED")}");
    }

    // ── Activate Button ────────────────────────────────────

    public void OnActivateButtonPressed()
    {
        // 버튼 눌림 시각 피드백은 누른 쪽 로컬에서 즉시.
        StartCoroutine(PressButtonVisual(activateButton, activateButtonOrigin));

        // 실제 판정에 쓰는 snappedBattery/snappedLightBall 은 '권한자'에만 채워지는 로컬 값이다.
        // 그래서 P2(비권한자)가 누르면 그 값들이 null 이라 항상 "No battery"로 끝났다.
        // → 판정/해동은 반드시 권한자에서 실행한다(비권한자는 RPC 로 위임).
        if (Object == null || !Object.IsValid) { DoActivate(); return; } // 에디터 단독
        if (Object.HasStateAuthority) DoActivate();
        else RpcRequestActivate();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestActivate() => DoActivate();

    // 권한자에서만 실행 — 스냅된 배터리/라이트볼 검사 후 색 맞으면 해동, 틀리면 배출.
    void DoActivate()
    {
        if (IsOpen) { Debug.Log("Close glass first!"); return; }
        if (snappedBattery == null) { Debug.Log("No battery!"); return; }
        if (snappedLightBall == null) { Debug.Log("No light ball!"); return; }

        var bState = snappedBattery.GetComponent<BatteryState>();
        var lTag = snappedLightBall.GetComponent<LightBallColorTag>();

        LightBallColor bColor = LightBallColor.Red;
        if (bState != null) bColor = bState.Color;
        else {
            var bTag = snappedBattery.GetComponent<BatteryColorTag>();
            if (bTag != null) bColor = bTag.color;
        }

        if (lTag != null && bColor != lTag.color)
        {
            Debug.Log($"[BatteryMelter] Color mismatch ({bColor} vs {lTag.color}) — ejecting battery.");
            EjectBattery();
            return;
        }

        MeltBatteryLogic();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestMelt()
    {
        MeltBatteryLogic();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestEject()
    {
        EjectBattery();
    }

    void EjectBattery()
    {
        if (snappedBattery == null) return;

        IsOpen = true;

        var bat = snappedBattery;
        snappedBattery = null;

        var rb = bat.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity   = true;
            // Toss forward and slightly upward so it lands in front of the machine.
            rb.linearVelocity = transform.forward * 2f + Vector3.up * 1f;
        }
    }

    void MeltBatteryLogic()
    {
        if (snappedBattery == null) return;
        
        var bState = snappedBattery.GetComponent<BatteryState>();
        if (bState != null)
        {
            // Pass materials from the machine if the battery doesn't have them assigned
            if (bState.frozenMaterial == null) bState.frozenMaterial = frozenBatteryCore;
            if (bState.meltedMaterial == null) bState.meltedMaterial = meltedBatteryCore;

            bState.Melt();
        }
        
        // Legacy support
        if (snappedBattery.GetComponent<MeltedBattery>() == null)
        {
            snappedBattery.AddComponent<MeltedBattery>();
        }
    }

    // ── Button Press ───────────────────────────────────────

    IEnumerator PressButtonVisual(Transform btn, Vector3 origin)
    {
        Vector3 pressed = origin - new Vector3(0, buttonPressDepth, 0);
        float t = 0f;
        while (t < 1f) { t += Time.deltaTime * 10f; btn.localPosition = Vector3.Lerp(origin, pressed, Mathf.Clamp01(t)); yield return null; }
        yield return new WaitForSeconds(0.12f);
        t = 0f;
        while (t < 1f) { t += Time.deltaTime * 10f; btn.localPosition = Vector3.Lerp(pressed, origin, Mathf.Clamp01(t)); yield return null; }
        btn.localPosition = origin;
    }
}