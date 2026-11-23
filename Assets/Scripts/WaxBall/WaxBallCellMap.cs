using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 큰 판(Voronoi 셀)을 CPU에서도 똑같이 알 수 있게 하는 표. WaxShell.shader의 Voronoi(q * _PlateScale)와 같은 알고리즘이다.
    /// 셀 중심 위치와 셀 무작위 값은 정수 해시(PCG3D)로 정한다. sin 기반 해시는 GPU(Mac, Quest)마다 값이 달라서 CPU가 재현할 수 없다.
    /// 판 번호(slot)는 격자 좌표 하나로 정한다: (x+4) + 8*(y+4) + 64*(z+4), 0~511. 그래서 _PlateScale은 2.5 이하여야 한다.
    /// 판별 상태는 32x16 텍스처(RGBAHalf, 포인트 샘플링)에 담아 셰이더에 넘긴다. R: 깨짐, G: 섞임(2단계에서 채운다),
    /// B: CPU가 계산한 셀 무작위 값(셰이더 값과 비교하는 확인용), A: 확인용 표식 점(probe)이 들어 있는 판이면 1.
    /// 17번째 줄(판 512칸 아래)의 첫 칸 R은 "C#이 채운 표"라는 표시(1)이다. 표가 없을 때 셰이더가 비교를 건너뛰는 데 쓴다.
    /// </summary>
    public sealed class WaxBallCellMap
    {
        public const int GridSize = 8;
        public const int GridOffset = 4;
        public const int SlotCount = GridSize * GridSize * GridSize;
        public const int TextureWidth = 32;
        public const int TextureHeight = 17;
        public const int ProbeCount = 8;

        // 판 목록을 만들 때 표면에 뿌리는 점 개수. 작은 판도 놓치지 않도록 넉넉히 잡는다.
        private const int SurveyPointCount = 4096;

        // 셰이더의 cellHash 식과 같은 소금값(salt). 바꿀 때는 셰이더도 같이 바꾼다.
        private static readonly Vector3Int CellHashSalt = new Vector3Int(37, 91, 53);

        public float PlateScale { get; private set; }
        public Texture2D Texture { get; private set; }

        /// <summary>이 판이 표면에 있으면 true. 표면에 없는 격자 칸은 false.</summary>
        public readonly bool[] Used = new bool[SlotCount];

        /// <summary>판 중심(셀 점)의 공 로컬 방향(단위 벡터). Used인 칸만 의미가 있다.</summary>
        public readonly Vector3[] SiteDirection = new Vector3[SlotCount];

        /// <summary>셀 무작위 값(0~1). 셰이더의 cellHash와 같은 값이다.</summary>
        public readonly float[] CellHash = new float[SlotCount];

        /// <summary>표면 전체에서 이 판이 차지하는 비율(0~1). 합은 1이다.</summary>
        public readonly float[] AreaRatio = new float[SlotCount];

        /// <summary>확인용 표식 점. xyz는 공 로컬 방향이고 셰이더가 노란 점으로 그린다.</summary>
        public readonly Vector4[] ProbeVectors = new Vector4[ProbeCount];

        private readonly Color[] pixels = new Color[TextureWidth * TextureHeight];

        public int UsedCount { get; private set; }

        /// <summary>판 목록과 텍스처를 (다시) 만든다. plateScale은 머티리얼의 _PlateScale이다.</summary>
        public void Rebuild(float plateScale)
        {
            PlateScale = plateScale;

            var counts = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                Used[i] = false;
                AreaRatio[i] = 0f;
            }

            UsedCount = 0;
            for (int i = 0; i < SurveyPointCount; i++)
            {
                int slot = FindCell(FibonacciPoint(i, SurveyPointCount), plateScale, out Vector3 site);
                if (slot < 0) continue;
                counts[slot]++;
                if (!Used[slot])
                {
                    Used[slot] = true;
                    UsedCount++;
                    SiteDirection[slot] = site.normalized;
                }
            }

            for (int i = 0; i < SlotCount; i++)
            {
                AreaRatio[i] = counts[i] / (float)SurveyPointCount;
                CellHash[i] = Used[i] ? CellHashOfSlot(i) : 0f;
            }

            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
            for (int i = 0; i < ProbeCount; i++)
            {
                Vector3 direction = FibonacciPoint(i, ProbeCount);
                ProbeVectors[i] = new Vector4(direction.x, direction.y, direction.z, 0f);
                int slot = FindCell(direction, plateScale, out _);
                if (slot >= 0) pixels[slot].a = 1f;
            }

            for (int i = 0; i < SlotCount; i++)
                if (Used[i]) pixels[i].b = CellHash[i];
            pixels[SlotCount].r = 1f;

            if (Texture == null)
            {
                Texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBAHalf, false, true)
                {
                    name = "WaxBallCellState",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            Texture.SetPixels(pixels);
            Texture.Apply(false);
        }

        /// <summary>
        /// 공 로컬 방향(단위 벡터)이 속한 큰 판의 번호를 돌려준다. 격자 범위를 벗어나면 -1.
        /// site는 그 판의 중심(셰이더의 site1과 같은 값, 스케일된 공간)이다.
        /// </summary>
        public static int FindCell(Vector3 direction, float plateScale, out Vector3 site)
        {
            Vector3 p = direction * plateScale;
            var cell = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
            Vector3 local = p - (Vector3)cell;

            float best = 8f;
            Vector3Int bestId = cell;
            Vector3 bestSite = Vector3.zero;

            // 셰이더와 같은 순서(z, y, x)로 돌고 같은 비교(<)를 써서 거리가 같을 때도 같은 판이 고른다.
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var id = new Vector3Int(cell.x + x, cell.y + y, cell.z + z);
                Vector3 point = Hash3(id);
                Vector3 diff = new Vector3(x + point.x - local.x, y + point.y - local.y, z + point.z - local.z);
                float d = Vector3.Dot(diff, diff);
                if (d < best)
                {
                    best = d;
                    bestId = id;
                    bestSite = new Vector3(id.x + point.x, id.y + point.y, id.z + point.z);
                }
            }

            site = bestSite;
            return SlotOf(bestId);
        }

        /// <summary>격자 좌표를 판 번호로 바꾼다. 범위를 벗어나면 -1.</summary>
        public static int SlotOf(Vector3Int id)
        {
            int x = id.x + GridOffset, y = id.y + GridOffset, z = id.z + GridOffset;
            if (x < 0 || y < 0 || z < 0 || x >= GridSize || y >= GridSize || z >= GridSize) return -1;
            return x + GridSize * y + GridSize * GridSize * z;
        }

        public static Vector3Int IdOfSlot(int slot)
        {
            return new Vector3Int(
                slot % GridSize - GridOffset,
                slot / GridSize % GridSize - GridOffset,
                slot / (GridSize * GridSize) - GridOffset);
        }

        /// <summary>셀 점의 무작위 오프셋(0~1). 셰이더의 IntHash3와 같은 값이다.</summary>
        public static Vector3 Hash3(Vector3Int id)
        {
            unchecked
            {
                uint x = (uint)(id.x + 16) * 1664525u + 1013904223u;
                uint y = (uint)(id.y + 16) * 1664525u + 1013904223u;
                uint z = (uint)(id.z + 16) * 1664525u + 1013904223u;
                x += y * z; y += z * x; z += x * y;
                x ^= x >> 16; y ^= y >> 16; z ^= z >> 16;
                x += y * z; y += z * x; z += x * y;
                const float scale = 1f / 16777216f;
                return new Vector3((x >> 8) * scale, (y >> 8) * scale, (z >> 8) * scale);
            }
        }

        /// <summary>셀 무작위 값(0~1). 셰이더의 cellHash와 같은 값이다.</summary>
        public static float CellHashOf(Vector3Int id)
        {
            return Hash3(id + CellHashSalt).x;
        }

        private static float CellHashOfSlot(int slot)
        {
            return CellHashOf(IdOfSlot(slot));
        }

        /// <summary>구 표면에 고르게 퍼진 점 i(0 ~ count-1).</summary>
        public static Vector3 FibonacciPoint(int i, int count)
        {
            float y = 1f - 2f * (i + 0.5f) / count;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = i * 2.39996323f;
            return new Vector3(Mathf.Cos(theta) * r, y, Mathf.Sin(theta) * r);
        }
    }
}
