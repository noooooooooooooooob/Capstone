#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Stage1;

namespace Stage1.Editor
{
    /// <summary>
    /// Shared 모드 네트워크에서 "잡고 놓으면 사라짐" 일괄 해결.
    ///
    /// 부착하는 것들:
    ///   1) GrabAuthorityHandover  — Select 진입 시 State Authority 가져오기
    ///   2) GrabNetworkSyncPause    — 잡힌 동안 NetworkTransform 비활성 + throwOnDetach OFF
    ///   3) NetworkObject.Flags |= AllowStateAuthorityOverride 강제
    /// </summary>
    public static class GrabAuthoritySetupTool
    {
        [MenuItem("Tools/Stage 1/Grab Authority Setup/Diagnose Grabbables")]
        public static void DiagnoseGrabbables()
        {
            int total = 0, withGah = 0, withSyncPause = 0, withoutNo = 0, missingFlag = 0, withoutNt = 0;
            var interactables = Object.FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None);
            foreach (var it in interactables)
            {
                total++;
                var no = it.GetComponent<NetworkObject>();
                if (no == null) { withoutNo++; continue; }

                var nt = it.GetComponent<NetworkTransform>();
                if (nt == null)
                {
                    withoutNt++;
                    Debug.LogWarning($"[Diagnose] '{it.name}' NetworkObject 있지만 NetworkTransform 없음 — 위치 동기 안 됨.", it);
                }

                if (it.GetComponent<GrabAuthorityHandover>() != null) withGah++;
                else Debug.LogWarning($"[Diagnose] '{it.name}' GrabAuthorityHandover 없음.", it);

                if (it.GetComponent<GrabNetworkSyncPause>() != null) withSyncPause++;
                else Debug.LogWarning($"[Diagnose] '{it.name}' GrabNetworkSyncPause 없음 — 잡을 때 권위 갭으로 흔들림 가능.", it);

                bool allow = (no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride;
                if (!allow)
                {
                    missingFlag++;
                    Debug.LogWarning($"[Diagnose] '{it.name}' NetworkObject.AllowStateAuthorityOverride = false — RequestStateAuthority 거부됨.", no);
                }
            }
            Debug.Log($"[Diagnose] Interactable {total} | NetworkObject 없음 {withoutNo} | NetworkTransform 없음 {withoutNt} | GAH 있음 {withGah} | GNSP 있음 {withSyncPause} | AllowOverride 없음 {missingFlag}");
        }

        [MenuItem("Tools/Stage 1/Grab Authority Setup/Attach All (GAH + GNSP + Flag)")]
        public static void AttachAll()
        {
            int gahAdded = 0, gnspAdded = 0, flagFixed = 0, skipped = 0;

            var interactables = Object.FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None);
            foreach (var it in interactables)
            {
                var no = it.GetComponent<NetworkObject>();
                if (no == null) { skipped++; continue; }

                // 1) GrabAuthorityHandover
                if (it.GetComponent<GrabAuthorityHandover>() == null)
                {
                    Undo.AddComponent<GrabAuthorityHandover>(it.gameObject);
                    gahAdded++;
                }

                // 2) GrabNetworkSyncPause
                if (it.GetComponent<GrabNetworkSyncPause>() == null)
                {
                    Undo.AddComponent<GrabNetworkSyncPause>(it.gameObject);
                    gnspAdded++;
                }

                // 3) AllowStateAuthorityOverride flag — Fusion 2의 NetworkObject 는 internal field,
                //    SerializedObject로 접근 시도. Property name 후보 여러개 시도.
                if (TrySetAllowOverride(no))
                {
                    flagFixed++;
                }

                EditorUtility.SetDirty(it.gameObject);
            }

            Debug.Log($"[Setup] 완료. GAH 부착 {gahAdded} | GNSP 부착 {gnspAdded} | AllowOverride 활성화 시도 {flagFixed} | NetworkObject 없어 스킵 {skipped}");
        }

        /// <summary>
        /// Fusion 버전마다 NetworkObject.Flags의 SerializedProperty 이름이 다를 수 있어
        /// 여러 후보 시도. true 반환 시 수정 적용됨.
        /// </summary>
        static bool TrySetAllowOverride(NetworkObject no)
        {
            int needBit = (int)NetworkObjectFlags.AllowStateAuthorityOverride;

            // 이미 켜져 있으면 skip
            if ((no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride)
                return false;

            var so = new SerializedObject(no);

            // 후보 property names — Fusion 2 빌드별 차이 대응
            string[] candidates = { "Flags", "_flags", "m_Flags", "_objectFlags" };
            foreach (var name in candidates)
            {
                var prop = so.FindProperty(name);
                if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
                {
                    prop.intValue = prop.intValue | needBit;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(no);
                    Debug.Log($"[Setup] {no.name} Flags 셋팅 ({name}).", no);
                    return true;
                }
            }

            Debug.LogWarning($"[Setup] {no.name} NetworkObject.Flags property 못 찾음 — " +
                             "인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
            return false;
        }
    }
}
#endif
