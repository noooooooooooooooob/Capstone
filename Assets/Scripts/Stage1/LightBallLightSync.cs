using System.Collections.Generic;
using UnityEngine;

namespace Stage1
{
    /// <summary>
    /// MainControlSystem과 같은 GameObject(또는 자식)에 부착하는 외부 라이트 동기화 모듈.
    /// MainControlSystem 코드는 한 줄도 안 건드림.
    ///
    /// 동작:
    ///   MainControlSystem.lightBallLight (원본 LightBall의 Light) 의 enabled/intensity를
    ///   모든 LightBall 태그 GameObject의 자식 Light들에 LateUpdate마다 복사.
    ///
    /// 그래서:
    ///   - 정전(PowerOff) → 원본 Light가 2.0으로 켜짐 → 복사본 Light들도 같이 2.0
    ///   - 복구(Idle) → 원본 0으로 꺼짐 → 복사본도 같이 꺼짐
    ///
    /// 색은 각 LightBall의 Light가 인스펙터에서 가진 색 그대로 유지 (Red/Yellow/Blue 보존).
    /// </summary>
    [DisallowMultipleComponent]
    public class LightBallLightSync : MonoBehaviour
    {
        [Tooltip("비우면 같은 GO 또는 씬에서 자동 검출.")]
        public MainControlSystem mainControl;

        [Tooltip("동기화할 Light들 (원본 lightBallLight는 자동 제외). " +
                 "비워두면 Awake에서 'LightBall' 태그 GameObject들의 자식 Light를 모두 자동 수집.")]
        public Light[] syncedLights;

        void Awake()
        {
            if (mainControl == null) mainControl = GetComponent<MainControlSystem>();
            if (mainControl == null) mainControl = Object.FindFirstObjectByType<MainControlSystem>();

            if (syncedLights == null || syncedLights.Length == 0)
                AutoCollectLights();
        }

        public void AutoCollectLights()
        {
            var list = new List<Light>();
            Light source = mainControl != null ? mainControl.lightBallLight : null;

            GameObject[] all = GameObject.FindGameObjectsWithTag("LightBall");
            foreach (var lb in all)
            {
                if (lb == null) continue;
                foreach (var l in lb.GetComponentsInChildren<Light>(true))
                {
                    if (l == null || l == source) continue;
                    list.Add(l);
                }
            }
            syncedLights = list.ToArray();
            Debug.Log($"[LightBallLightSync] {syncedLights.Length}개 보조 Light 자동 수집 완료.");
        }

        void LateUpdate()
        {
            if (mainControl == null || mainControl.lightBallLight == null) return;
            if (syncedLights == null || syncedLights.Length == 0) return;

            Light src = mainControl.lightBallLight;
            bool en = src.enabled;
            float inten = src.intensity;

            for (int i = 0; i < syncedLights.Length; i++)
            {
                var l = syncedLights[i];
                if (l == null || l == src) continue;
                // 활성 GameObject가 아니면 Light enabled 만지면 의미없으니 SetActive도 같이
                if (en && !l.gameObject.activeSelf) l.gameObject.SetActive(true);
                l.enabled = en;
                l.intensity = inten;
                // 색은 그대로 (Red/Yellow/Blue 보존)
            }
        }
    }
}
