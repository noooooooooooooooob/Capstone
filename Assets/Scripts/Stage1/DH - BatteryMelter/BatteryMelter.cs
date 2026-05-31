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
    [Tooltip("Local position offset applied on top of batterySlot so the battery sits flush inside the device.")]
    public Vector3 batterySnapPositionOffset = Vector3.zero;
    [Tooltip("Euler rotation offset applied on top of batterySlot so the battery lies horizontally.")]
    public Vector3 batterySnapRotationOffset = new Vector3(0f, 0f, 90f);

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
        var ngs = obj.GetComponent<NetworkGrabbableSync>();
        if (ngs != null) return ngs.IsGrabbed;

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
                // 네트워크 핵심: 배터리 권한을 확보한 뒤에만 위치를 강제한다.
                // 권한이 아직 다른 피어(마지막에 잡은 사람)에 있으면, 위치를 써도 그 피어의
                // NetworkTransform 이 매 틱 되돌려 '줄다리기'가 생겨 스냅이 안 잡힌다.
                // 그래서 권한이 없으면 요청만 하고(다음 프레임 재시도), 확보된 뒤 ApplySnap.
                var no = snappedBattery.GetComponent<NetworkObject>();
                if (no != null && no.IsValid && !no.HasStateAuthority)
                    no.RequestStateAuthority();
                else
                    ApplySnap(snappedBattery, batterySlot, batterySnapPositionOffset, batterySnapRotationOffset);
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
        if (IsOpen) { Debug.Log("Close glass first!"); return; }
        if (snappedBattery == null) { Debug.Log("No battery!"); return; }
        if (snappedLightBall == null) { Debug.Log("No light ball!"); return; }

        // Color validation
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
            Debug.Log($"[BatteryMelter] Color mismatch ({bColor} vs {lTag.color}) — opening lid and ejecting battery.");
            if (Object.HasStateAuthority)
                EjectBattery();
            else
                RpcRequestEject();
            StartCoroutine(PressButtonVisual(activateButton, activateButtonOrigin));
            return;
        }

        if (Object.HasStateAuthority)
        {
            MeltBatteryLogic();
        }
        else
        {
            RpcRequestMelt();
        }

        StartCoroutine(PressButtonVisual(activateButton, activateButtonOrigin));
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