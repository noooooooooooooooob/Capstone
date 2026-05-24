using System.Collections.Generic;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 사전 정의된 거울 통과 순서를 2층 패널에 색상으로 표시 — display only.
    ///
    /// 디자이너가 <see cref="RequiredSequence"/> 를 외부에서 설정(Setup 빌드 시) 하면
    /// Start 의 <see cref="UpdateDisplay"/> 가 슬롯에 색상을 입힌다. 플레이어 입력은 받지 않음.
    /// LightBeamController 는 매 프레임 자기 빔 hit 시퀀스를 이 RequiredSequence 와 비교.
    ///
    /// 입력식 패널이 필요하면 외부 코드가 <see cref="SetSequence"/> 또는 직접 <see cref="RequiredSequence"/> 수정 후
    /// <see cref="RefreshDisplay"/> 호출.
    /// </summary>
    public class ColorOrderPanel : MonoBehaviour
    {
        [Header("Sequence")]
        [Tooltip("거울이 빔에 닿아야 하는 색 ID 순서. LightBeamController 가 읽음.")]
        public List<int> RequiredSequence = new List<int>();

        [Tooltip("이 길이로 가득 채워져야 IsComplete = true. 보통 거울 수 = 4.")]
        public int MaxSequenceLength = 4;

        [Header("Activation")]
        [Tooltip("false 면 디스플레이가 모두 EmptySlotColor 로 보임. " +
                 "LightOrbSocket 에 orb 가 삽입돼야 true 로 전환되며 색상 시퀀스가 표시됨. " +
                 "기본값 false — Activator 가 Start 에서 socket 상태에 맞춰 동기화함.")]
        public bool IsActive = false;

        [Header("Visual feedback")]
        [Tooltip("순서를 표시할 슬롯 렌더러들. 좌→우 = 첫번째→마지막.")]
        public List<Renderer> DisplaySlots = new List<Renderer>();

        [Tooltip("ColorId 별 표시색. 인덱스 = ColorId.")]
        public List<Color> ColorPalette = new List<Color>();

        [Tooltip("빈/미정의 슬롯의 색.")]
        public Color EmptySlotColor = new Color(0.08f, 0.08f, 0.10f);

        [Tooltip("emission 강도 배율.")]
        public float EmissionIntensity = 1.4f;

        readonly List<Material> _slotMatInstances = new List<Material>();
        bool _initialized;

        public bool IsComplete => RequiredSequence.Count >= MaxSequenceLength;
        public int Length => RequiredSequence.Count;

        void Start()
        {
            EnsureMaterialInstances();
            UpdateDisplay();
        }

        void EnsureMaterialInstances()
        {
            if (_initialized) return;
            _slotMatInstances.Clear();
            for (int i = 0; i < DisplaySlots.Count; i++)
            {
                var r = DisplaySlots[i];
                _slotMatInstances.Add(r != null ? r.material : null); // material → instance
            }
            _initialized = true;
        }

        /// <summary>외부에서 시퀀스를 통째로 설정. UpdateDisplay 호출 포함.</summary>
        public void SetSequence(IEnumerable<int> sequence)
        {
            RequiredSequence = new List<int>(sequence);
            UpdateDisplay();
        }

        public void RefreshDisplay() => UpdateDisplay();

        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;
            UpdateDisplay();
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;
            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            EnsureMaterialInstances();
            int n = DisplaySlots.Count;
            for (int i = 0; i < n; i++)
            {
                var mat = _slotMatInstances[i];
                if (mat == null) continue;
                Color c = EmptySlotColor;
                // 비활성 상태이면 모든 슬롯이 빈 색 — orb 가 socket 에 들어가야 색상 시퀀스가 보임.
                if (IsActive && i < RequiredSequence.Count)
                {
                    int id = RequiredSequence[i];
                    if (id >= 0 && id < ColorPalette.Count) c = ColorPalette[id];
                }
                mat.color = c;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", c * EmissionIntensity);
                    mat.EnableKeyword("_EMISSION");
                }
            }
        }
    }
}
