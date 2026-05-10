using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.MiniGame
{
    /// <summary>
    /// 파이프 미니게임 보드. PipeMiniGame GameObject 에 붙는다.
    /// 셀들이 자기 회전을 바꿀 때마다 <see cref="OnCellChanged"/> 가 호출되고,
    /// 보드는 Source 들에서 BFS 로 흐름을 흘려 <see cref="PipeMiniGameCell.SetConnected"/> 를 갱신한다.
    /// 모든 Sink 가 도달되면 <see cref="OnSolved"/> UnityEvent 발행.
    /// </summary>
    public class PipeMiniGameBoard : MonoBehaviour
    {
        [Header("Grid")]
        public int Width = 5;
        public int Height = 3;

        [Tooltip("flat 1D 배열, 인덱스 = x + y*Width. 빈 칸은 null.")]
        public PipeMiniGameCell[] Cells;

        [Header("Materials")]
        public Material DisconnectedMaterial;
        public Material ConnectedMaterial;
        public Material SourceMaterial;
        public Material SinkMaterial;

        [Header("Events")]
        public UnityEvent OnSolved;
        public UnityEvent OnUnsolved;

        bool _isSolved;
        public bool IsSolved => _isSolved;

        void Start()
        {
            // 보드 참조를 셀에 다시 한 번 채워준다 — prefab/스폰 케이스 대비.
            if (Cells != null)
            {
                foreach (var c in Cells)
                {
                    if (c != null) c.Board = this;
                }
            }
            UpdateFlow();
        }

        public PipeMiniGameCell Get(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return null;
            int idx = x + y * Width;
            if (Cells == null || idx < 0 || idx >= Cells.Length) return null;
            return Cells[idx];
        }

        public void OnCellChanged()
        {
            UpdateFlow();
        }

        void UpdateFlow()
        {
            var visited = new HashSet<PipeMiniGameCell>();
            var queue = new Queue<PipeMiniGameCell>();

            // 모든 Source 에서 BFS 시작.
            if (Cells != null)
            {
                foreach (var c in Cells)
                {
                    if (c != null && c.Shape == PipeShape.Source)
                    {
                        queue.Enqueue(c);
                        visited.Add(c);
                    }
                }
            }

            var dirs = new[] { Direction.N, Direction.E, Direction.S, Direction.W };
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var mask = cur.CurrentMask;

                foreach (var dir in dirs)
                {
                    if ((mask & dir) == 0) continue; // 현재 셀에 그 방향 연결구가 없음
                    var step = PipeShapeDef.Step(dir);
                    int nx = cur.X + step.dx;
                    int ny = cur.Y + step.dy;
                    var next = Get(nx, ny);
                    if (next == null || visited.Contains(next)) continue;

                    var opp = PipeShapeDef.Opposite(dir);
                    if ((next.CurrentMask & opp) == 0) continue; // 상대 셀이 이쪽으로 안 열려 있음

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            // 색 갱신.
            if (Cells != null)
            {
                foreach (var c in Cells)
                {
                    if (c == null) continue;
                    c.SetConnected(visited.Contains(c));
                }
            }

            // 클리어 판정 — 모든 Sink 가 visited 안에 있어야 함.
            bool allSinksReached = true;
            int sinkCount = 0;
            if (Cells != null)
            {
                foreach (var c in Cells)
                {
                    if (c != null && c.Shape == PipeShape.Sink)
                    {
                        sinkCount++;
                        if (!visited.Contains(c)) allSinksReached = false;
                    }
                }
            }
            if (sinkCount == 0) allSinksReached = false;

            if (allSinksReached && !_isSolved)
            {
                _isSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[PipeMiniGame] Solved!");
            }
            else if (!allSinksReached && _isSolved)
            {
                _isSolved = false;
                OnUnsolved?.Invoke();
            }
        }
    }
}
