using UnityEngine;

namespace PipePuz.DimensionalAssembly
{
    /// <summary>
    /// 두 노드 사이의 와이어. LineRenderer 한 개를 매 프레임 두 노드의 위치로 갱신.
    /// Break() 호출 시 알파 페이드 후 자가 파괴.
    /// </summary>
    public class DAConnection : MonoBehaviour
    {
        public DAEnergyNode NodeA;
        public DAEnergyNode NodeB;
        public LineRenderer Line;

        [Tooltip("Break 후 알파가 0 까지 줄어드는 시간(s).")]
        public float FadeDuration = 0.25f;

        bool _breaking;
        float _fadeTimer;
        Color _startColor;
        Color _endColor;

        void Start()
        {
            if (Line != null)
            {
                _startColor = Line.startColor;
                _endColor = Line.endColor;
            }
        }

        void Update()
        {
            // 평상시 양 끝점 갱신.
            if (!_breaking && Line != null && NodeA != null && NodeB != null)
            {
                Line.SetPosition(0, NodeA.transform.position);
                Line.SetPosition(1, NodeB.transform.position);
            }

            // 페이드 처리.
            if (_breaking)
            {
                _fadeTimer += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(_fadeTimer / Mathf.Max(0.01f, FadeDuration));
                if (Line != null)
                {
                    var c = _startColor; c.a *= a; Line.startColor = c;
                    c = _endColor; c.a *= a; Line.endColor = c;
                }
                if (_fadeTimer >= FadeDuration)
                {
                    Destroy(gameObject);
                }
            }
        }

        public void Break()
        {
            if (_breaking) return;
            _breaking = true;
            _fadeTimer = 0f;
        }

        public bool MatchesPair(DAEnergyNode a, DAEnergyNode b)
        {
            return (NodeA == a && NodeB == b) || (NodeA == b && NodeB == a);
        }
    }
}
