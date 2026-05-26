using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PipePuz.Firefight.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Big Wide Fires.
    /// 기존 Stage1/FireFight/Fires 또는 Firefight/Fires 아래의 Fire_X 각각에 크고 넓은 BigFlame ParticleSystem 을
    /// 자식으로 추가하고 FirefightFire.FireParticles 를 그쪽으로 갈아끼움.
    ///
    /// 기존 LargeFlames / FireEmbers 등 커스텀 자식은 건드리지 않음 — 필요 없으면 인스펙터에서 직접 비활성화.
    /// 메뉴 재실행 시 기존 BigFlame 만 삭제 후 새로 생성 (idempotent).
    /// </summary>
    public static class BigFireBuilder
    {
        const string BigFlameChildName = "BigFlame";

        [MenuItem("Tools/PipePuz/Build Big Wide Fires")]
        public static void BuildBigWideFires()
        {
            // 1. Firefight 컨테이너 찾기 (Stage1 안쪽 / 루트 어디에 있든 OK).
            GameObject room = FindFirefightRoot();
            if (room == null)
            {
                EditorUtility.DisplayDialog("Build Big Wide Fires",
                    "씬에서 'Firefight' / 'FireFight' GameObject 를 찾을 수 없습니다.\n" +
                    "Stage1 씬을 연 상태에서 다시 시도하세요.", "OK");
                return;
            }

            // 2. Fires 자식 찾기, 없으면 생성.
            var firesT = room.transform.Find("Fires");
            GameObject firesGo;
            if (firesT == null)
            {
                firesGo = new GameObject("Fires");
                firesGo.transform.SetParent(room.transform, false);
            }
            else
            {
                firesGo = firesT.gameObject;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Big Wide Fires");

            // 3. 기존 Fire_X 수집. 하나도 없으면 디폴트 3개 위치에 생성.
            var fireChildren = new List<Transform>();
            foreach (Transform child in firesGo.transform)
            {
                if (child.name.StartsWith("Fire_")) fireChildren.Add(child);
            }

            if (fireChildren.Count == 0)
            {
                var defaults = new[]
                {
                    new Vector3(-0.7f, 0.25f, 1.5f),
                    new Vector3( 0.0f, 0.25f, 1.8f),
                    new Vector3(+0.7f, 0.25f, 1.5f),
                };
                for (int i = 0; i < defaults.Length; i++)
                {
                    var fGo = new GameObject($"Fire_{i}");
                    Undo.RegisterCreatedObjectUndo(fGo, "Build Big Wide Fires");
                    fGo.transform.SetParent(firesGo.transform, false);
                    fGo.transform.localPosition = defaults[i];

                    // 표적용 콜라이더 (raycast).
                    var col = fGo.AddComponent<SphereCollider>();
                    col.radius = 0.30f;
                    col.isTrigger = true;

                    fireChildren.Add(fGo.transform);
                }
            }

            // 4. 각 Fire_X 에 BigFlame 추가 + FirefightFire 갈아끼움.
            var fireComps = new List<FirefightFire>();
            foreach (var fire in fireChildren)
            {
                // 기존 BigFlame 만 제거 (LargeFlames 등 커스텀은 보존).
                var oldBig = fire.Find(BigFlameChildName);
                if (oldBig != null) Undo.DestroyObjectImmediate(oldBig.gameObject);

                // 새 BigFlame 생성.
                var bigGo = new GameObject(BigFlameChildName);
                Undo.RegisterCreatedObjectUndo(bigGo, "Build Big Wide Fires");
                bigGo.transform.SetParent(fire, false);
                bigGo.transform.localPosition = Vector3.zero;
                var bigPs = bigGo.AddComponent<ParticleSystem>();
                ConfigureBigFlame(bigPs);

                // FirefightFire 컴포넌트 확보 & 연결.
                var ff = fire.GetComponent<FirefightFire>();
                if (ff == null)
                {
                    ff = Undo.AddComponent<FirefightFire>(fire.gameObject);
                }
                else
                {
                    Undo.RecordObject(ff, "Build Big Wide Fires");
                }

                ff.FireParticles = bigPs;
                // 우리가 만든 BigFlame 은 이미 위협적 톤으로 셋업했으니 런타임 덮어쓰기는 끔.
                ff.AutoEnhanceFlames = false;
                ff.DriveFlameEmissionAndSize = false;
                ff.StartStrength = 0.6f;     // 시작부터 충분히 크고 위협적
                ff.GrowthRate = 0.04f;
                ff.MaxStrength = 1.0f;
                // 위협 레이어 (라이트 + ember) 도 더 강하게.
                ff.MaxLightIntensity = 6f;
                ff.LightRange = 6f;
                ff.LightFlickerSpeed = 16f;
                ff.MaxEmberRate = 110f;

                fireComps.Add(ff);
            }

            // 5. FirefightController.Fires 배열 갱신.
            var ctrl = room.GetComponent<FirefightController>();
            if (ctrl != null)
            {
                Undo.RecordObject(ctrl, "Build Big Wide Fires");
                ctrl.Fires = fireComps.ToArray();
            }

            EditorUtility.SetDirty(firesGo);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(firesGo.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[BigWideFires] {fireChildren.Count} 개의 Fire 에 BigFlame 추가 완료. " +
                      "Play 누르면 크고 넓은 불꽃이 보입니다. " +
                      "기존 LargeFlames / FireEmbers 는 그대로 두었으니 필요 없으면 비활성화하세요.");
        }

        // -------------------------------------------------------------------------------------

        static GameObject FindFirefightRoot()
        {
            // 우선 정확한 이름으로 찾기.
            foreach (var n in new[] { "FireFight", "Firefight", "firefight" })
            {
                var go = GameObject.Find(n);
                if (go != null) return go;
            }
            // 못 찾으면 씬 전체에서 FirefightController 가 붙은 GameObject 검색.
            var ctrls = Object.FindObjectsByType<FirefightController>(FindObjectsSortMode.None);
            return ctrls.Length > 0 ? ctrls[0].gameObject : null;
        }

        static void ConfigureBigFlame(ParticleSystem ps)
        {
            // 메인 모듈 — 크고 넓고 천천히 솟구침.
            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); // 큰 입자
            main.startColor = new Color(1f, 0.55f, 0.15f, 1f);
            main.maxParticles = 600;
            main.gravityModifier = -0.25f; // 천천히 위로 (발원지에 오래 머묾)
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            var emission = ps.emission;
            emission.rateOverTime = 55f;

            // 넓은 원뿔 베이스.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.40f; // 넓은 발원지
            shape.rotation = new Vector3(-90f, 0f, 0f); // 위쪽 방향

            // 색 그라데이션 — 흰열 → 핫 오렌지 → 진한 적 → 그을음.
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.6f, 1.3f, 0.7f), 0f),
                    new GradientColorKey(new Color(1.1f, 0.45f, 0.08f), 0.3f),
                    new GradientColorKey(new Color(0.7f, 0.12f, 0.03f), 0.7f),
                    new GradientColorKey(new Color(0.12f, 0.04f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.5f, 0f),    // 시작부터 보이게
                    new GradientAlphaKey(1.0f, 0.2f),
                    new GradientAlphaKey(0.9f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            // 사이즈 커브 — spawn 시 보통 → 중간에 크게 부풀음 → 끝에서 줄어듦.
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.5f);
            sizeCurve.AddKey(0.35f, 1.3f); // 피크 — 화염 덩어리가 부풀어 오름
            sizeCurve.AddKey(1f, 0.5f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // 강한 난기류.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.1f;
            noise.frequency = 1.3f;
            noise.scrollSpeed = 1.5f;
            noise.damping = true;
            noise.octaveCount = 2;

            // 머티리얼 — 기본 파티클 머티리얼.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (mat != null) renderer.sharedMaterial = mat;

                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sortingFudge = -0.5f; // 약간 앞쪽 정렬 — 다른 입자/지형과 겹쳐도 잘 보이게
            }
        }
    }
}
