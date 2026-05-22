using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using PipePuz.MiniGame;

namespace PipePuz.MiniGame2
{
    /// <summary>
    /// PipeMiniGame2 의 메인 보드. 빈 slot 들과 사용자가 가져다 둔 파이프들을 관리한다.
    ///
    /// 파이프가 slot 에 들어가거나 회전하거나 빠지면 <see cref="OnFlowChanged"/> 가 호출되고,
    /// BFS 로 Source 에서 Sink 까지 흐름을 다시 계산한다.
    /// 모든 Sink 가 도달되면 <see cref="OnSolved"/> 발행.
    /// </summary>
    public class PipeMiniGame2Board : MonoBehaviour
    {
        [Header("Grid")]
        public int Width = 5;
        public int Height = 3;

        [Tooltip("flat 1D 배열, 인덱스 = x + y * Width.")]
        public PipeMiniGame2Slot[] Slots;

        [Header("Pipes")]
        public List<PipeMiniGame2Pipe> AllPipes = new List<PipeMiniGame2Pipe>();

        [Header("Solution Path")]
        [Tooltip("랜덤 정답 경로의 cell 좌표 (Source/Sink 자체 제외). Solved 판정에 사용.")]
        public List<Vector2Int> RequiredCells = new List<Vector2Int>();

        [Header("Random Path On Start (Runtime)")]
        [Tooltip("Start() 에서 새 랜덤 path 를 생성해 RequiredCells / PathLine 을 덮어쓴다. Play 모드 진입 때마다 다른 path.")]
        public bool RegeneratePathOnStart = true;

        [Tooltip("음수면 시간 기반 시드 (매번 다름). 양수면 고정 시드.")]
        public int RandomSeed = -1;

        [Tooltip("Panel 자식의 PathLine LineRenderer. RegeneratePath 시 좌표 자동 갱신.")]
        public LineRenderer PathLine;

        [Tooltip("Source slot — PathLine 시작점.")]
        public PipeMiniGame2Slot SourceSlot;

        [Tooltip("Sink slot — PathLine 끝점.")]
        public PipeMiniGame2Slot SinkSlot;

        [Header("Snap")]
        [Tooltip("Slot 중심에서의 부착 반경(m). Slot 의 trigger SphereCollider radius 와 동일 의미. " +
                 "값이 작을수록 pipe 가 slot 중심에 가까이 와야 부착. (legacy FindContainingSlot 도 동일 값 사용)")]
        public float SnapDistance = 0.115f;

        [Header("Materials")]
        public Material ConnectedMaterial;
        public Material DisconnectedMaterial;

        [Header("Events")]
        public UnityEvent OnSolved;
        public UnityEvent OnUnsolved;

        bool _isSolved;
        public bool IsSolved => _isSolved;

        void Start()
        {
            // 모든 파이프와 slot 의 Board 참조 확정.
            if (AllPipes != null)
            {
                for (int i = 0; i < AllPipes.Count; i++)
                {
                    if (AllPipes[i] != null) AllPipes[i].Board = this;
                }
            }
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    if (Slots[i] != null) Slots[i].Board = this;
                }
            }

            // Play 모드 진입 시 새 랜덤 path 생성 (Editor 박힌 값 덮어쓰기).
            if (RegeneratePathOnStart)
            {
                RegenerateRandomPath();
            }

            UpdateFlow();
        }

        // --------------------------------------------------------------------
        // Random Path 생성·적용
        // --------------------------------------------------------------------

        /// <summary>새 랜덤 path 생성 → RequiredCells / PathLine 좌표 갱신.</summary>
        public void RegenerateRandomPath()
        {
            int midY = Height / 2;
            var rand = (RandomSeed >= 0) ? new System.Random(RandomSeed) : new System.Random();
            RequiredCells = GenerateRandomPath(Width, Height, midY, rand);
            ApplyPathLine();
            Debug.Log($"[PipeMiniGame2] runtime path 길이 {RequiredCells.Count}: " +
                      string.Join(" ", RequiredCells.ConvertAll(c => $"({c.x},{c.y})")));
        }

        /// <summary>RequiredCells 와 SourceSlot/SinkSlot 위치를 PathLine 의 좌표에 적용.</summary>
        public void ApplyPathLine()
        {
            if (PathLine == null || SourceSlot == null || SinkSlot == null) return;
            if (RequiredCells == null || RequiredCells.Count == 0)
            {
                PathLine.positionCount = 0;
                return;
            }

            Vector3 surfaceOffset = -PathLine.transform.forward * 0.02f;

            int n = RequiredCells.Count + 2;
            PathLine.positionCount = n;
            PathLine.SetPosition(0, SourceSlot.transform.position + surfaceOffset);
            for (int i = 0; i < RequiredCells.Count; i++)
            {
                var slot = Get(RequiredCells[i].x, RequiredCells[i].y);
                Vector3 p = (slot != null ? slot.transform.position : SourceSlot.transform.position)
                            + surfaceOffset;
                PathLine.SetPosition(i + 1, p);
            }
            PathLine.SetPosition(n - 1, SinkSlot.transform.position + surfaceOffset);
        }

        /// <summary>
        /// DFS with depth cap. Source(0,midY) ~ Sink(W-1,midY) 사이의 random walk path.
        /// 반환은 중간 cells (Source/Sink 자체 제외). 첫 = (1,midY), 마지막 = (W-2,midY).
        /// </summary>
        public static List<Vector2Int> GenerateRandomPath(int W, int H, int midY, System.Random rand)
        {
            var start = new Vector2Int(1, midY);
            var end   = new Vector2Int(W - 2, midY);

            if (W <= 3) return new List<Vector2Int> { start };

            int minLen = (W - 3) + 1;
            int maxLen = Mathf.Min(W * H, minLen + rand.Next(2, 6));

            var blocked = new HashSet<Vector2Int>
            {
                new Vector2Int(0, midY),
                new Vector2Int(W - 1, midY),
            };

            for (int attempt = 0; attempt < 20; attempt++)
            {
                var visited = new HashSet<Vector2Int>(blocked);
                var path    = new List<Vector2Int>();
                if (DfsPath(start, end, W, H, maxLen, visited, path, rand))
                    return path;
            }

            Debug.LogWarning("[PipeMiniGame2] 랜덤 경로 생성 실패 — 직선 fallback");
            var fallback = new List<Vector2Int>();
            for (int x = 1; x <= W - 2; x++) fallback.Add(new Vector2Int(x, midY));
            return fallback;
        }

        static bool DfsPath(Vector2Int current, Vector2Int end, int W, int H, int maxLen,
                            HashSet<Vector2Int> visited, List<Vector2Int> path, System.Random rand)
        {
            path.Add(current);
            visited.Add(current);

            if (current == end) return true;
            if (path.Count >= maxLen)
            {
                path.RemoveAt(path.Count - 1);
                visited.Remove(current);
                return false;
            }

            var dirs = new[]
            {
                new Vector2Int(0,  1),
                new Vector2Int(1,  0),
                new Vector2Int(0, -1),
                new Vector2Int(-1, 0),
            };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            foreach (var d in dirs)
            {
                var next = current + d;
                if (next.x < 0 || next.x >= W) continue;
                if (next.y < 0 || next.y >= H) continue;
                if (visited.Contains(next)) continue;
                if (DfsPath(next, end, W, H, maxLen, visited, path, rand)) return true;
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(current);
            return false;
        }

        public PipeMiniGame2Slot Get(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return null;
            int idx = x + y * Width;
            if (Slots == null || idx < 0 || idx >= Slots.Length) return null;
            return Slots[idx];
        }

        /// <summary>주어진 world 위치에서 SnapDistance 안에 있는 가장 가까운 빈 slot 반환. 없으면 null. (거리 기반 — 레거시)</summary>
        public PipeMiniGame2Slot FindNearestEmptySlot(Vector3 worldPos)
        {
            if (Slots == null) return null;
            PipeMiniGame2Slot best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null || !slot.IsEmpty) continue;
                float d = Vector3.Distance(worldPos, slot.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = slot;
                }
            }
            if (best != null && bestDist <= SnapDistance) return best;
            return null;
        }

        /// <summary>
        /// 충돌 기반 — worldPos 가 slot 중심에서 SnapDistance 반경의 구 안에 있으면 그 slot 반환.
        /// 빈 slot 만 검사. 여러 구 안에 있으면 가장 가까운 slot 반환.
        /// </summary>
        public PipeMiniGame2Slot FindContainingSlot(Vector3 worldPos)
        {
            if (Slots == null) return null;
            float radiusSq = SnapDistance * SnapDistance;
            PipeMiniGame2Slot best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null || !slot.IsEmpty) continue;
                float dsq = (slot.transform.position - worldPos).sqrMagnitude;
                if (dsq > radiusSq) continue;
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    best = slot;
                }
            }
            return best;
        }

        /// <summary>외부 (Pipe / Slot) 에서 호출 — 흐름 재계산.</summary>
        public void OnFlowChanged()
        {
            UpdateFlow();
        }

        void UpdateFlow()
        {
            var visited = new HashSet<PipeMiniGame2Slot>();
            var queue = new Queue<PipeMiniGame2Slot>();

            // 모든 Source slot 에서 BFS 시작.
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s != null && s.CurrentPipe != null && s.CurrentPipe.Shape == PipeShape.Source)
                    {
                        queue.Enqueue(s);
                        visited.Add(s);
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
                    if ((mask & dir) == 0) continue;
                    var step = PipeShapeDef.Step(dir);
                    var next = Get(cur.X + step.dx, cur.Y + step.dy);
                    if (next == null || visited.Contains(next)) continue;
                    if (next.CurrentPipe == null) continue;
                    var opp = PipeShapeDef.Opposite(dir);
                    if ((next.CurrentMask & opp) == 0) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            // 색 갱신.
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s == null || s.CurrentPipe == null) continue;
                    s.CurrentPipe.SetConnected(visited.Contains(s));
                }
            }
            // slot 에 안 들어가있는 파이프들은 disconnected 색.
            if (AllPipes != null)
            {
                for (int i = 0; i < AllPipes.Count; i++)
                {
                    var p = AllPipes[i];
                    if (p != null && p.CurrentSlot == null) p.SetConnected(false);
                }
            }

            // === 클리어 판정 — 3개 조건 AND ===
            // (a) 모든 Sink 가 BFS 로 도달 가능 (Source→Sink 통로 완성)
            // (b) RequiredCells 의 모든 cell 에 pipe 가 있음
            // (c) RequiredCells 의 모든 cell 이 BFS visited 에 포함 (path 따라 입출구 연결)
            bool allSinksReached = true;
            int sinkCount = 0;
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s != null && s.CurrentPipe != null && s.CurrentPipe.Shape == PipeShape.Sink)
                    {
                        sinkCount++;
                        if (!visited.Contains(s)) allSinksReached = false;
                    }
                }
            }
            if (sinkCount == 0) allSinksReached = false;

            bool allRequiredFilled = true;
            bool allRequiredReachable = true;
            if (RequiredCells != null && RequiredCells.Count > 0)
            {
                for (int i = 0; i < RequiredCells.Count; i++)
                {
                    var cell = RequiredCells[i];
                    var slot = Get(cell.x, cell.y);
                    if (slot == null || slot.CurrentPipe == null)
                    {
                        allRequiredFilled = false;
                        allRequiredReachable = false;
                        break;
                    }
                    if (!visited.Contains(slot))
                    {
                        allRequiredReachable = false;
                    }
                }
            }

            bool solved = allSinksReached && allRequiredFilled && allRequiredReachable;

            if (solved && !_isSolved)
            {
                _isSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[PipeMiniGame2] Solved! " +
                          "(모든 RequiredCells 채움 + path 따라 입출구 연결 + Source→Sink 완성)");
            }
            else if (!solved && _isSolved)
            {
                _isSolved = false;
                OnUnsolved?.Invoke();
            }
        }
    }
}
