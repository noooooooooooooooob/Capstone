using System;

namespace PipePuz.MiniGame
{
    /// <summary>파이프 한 칸의 모양.</summary>
    public enum PipeShape
    {
        None,      // 빈 칸
        Straight,  // 직선 — 기본 N|S
        Elbow,     // ㄱ자 — 기본 N|E
        Tee,       // T자 — 기본 N|E|S
        Cross,     // 십자 — 기본 N|E|S|W
        Source,    // 흐름 발생 — 기본 E
        Sink,      // 흐름 도착 — 기본 W
    }

    /// <summary>4 방향 비트마스크. N=1, E=2, S=4, W=8.</summary>
    [Flags]
    public enum Direction
    {
        None = 0,
        N = 1,
        E = 2,
        S = 4,
        W = 8,
    }

    public static class PipeShapeDef
    {
        public static Direction BaseMask(PipeShape s)
        {
            switch (s)
            {
                case PipeShape.Straight: return Direction.N | Direction.S;
                case PipeShape.Elbow:    return Direction.N | Direction.E;
                case PipeShape.Tee:      return Direction.N | Direction.E | Direction.S;
                case PipeShape.Cross:    return Direction.N | Direction.E | Direction.S | Direction.W;
                case PipeShape.Source:   return Direction.E;
                case PipeShape.Sink:     return Direction.W;
                default:                 return Direction.None;
            }
        }

        /// <summary>비트마스크를 시계 방향으로 90° × times 만큼 회전 (N→E→S→W→N).</summary>
        public static Direction Rotate(Direction d, int times)
        {
            times = ((times % 4) + 4) % 4;
            int v = (int)d & 0xF;
            v = ((v << times) | (v >> (4 - times))) & 0xF;
            return (Direction)v;
        }

        public static Direction GetMask(PipeShape s, int rotation)
        {
            return Rotate(BaseMask(s), rotation);
        }

        public static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.N: return Direction.S;
                case Direction.S: return Direction.N;
                case Direction.E: return Direction.W;
                case Direction.W: return Direction.E;
                default: return Direction.None;
            }
        }

        /// <summary>해당 방향의 격자 좌표 변화량 (dx, dy). N=+y, E=+x, S=-y, W=-x.</summary>
        public static (int dx, int dy) Step(Direction d)
        {
            switch (d)
            {
                case Direction.N: return (0, +1);
                case Direction.S: return (0, -1);
                case Direction.E: return (+1, 0);
                case Direction.W: return (-1, 0);
                default: return (0, 0);
            }
        }
    }
}
