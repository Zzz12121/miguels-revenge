using System.Collections.Generic;

/// <summary>
/// 隨機排出一層地城的房間（只算「哪一格有房間、誰跟誰相連」，不碰任何場景物件，DungeonGenerator 才負責蓋出來）。
///
/// 房間排在格子上：
///   - 第 0 間（rooms[0]）固定在 (0, 0)，是最靠近 Boss 房大門的那一間
///   - 主線：從 (0, 0) 開始隨機往左 / 右 / 遠離 Boss 的方向走，不會繞回 Boss 那一側，也不會走到重複的格子
///     主線的最後一間是起點房間（玩家出生的地方）
///   - 岔路：在主線的房間旁邊隨機多接一兩間死路房間
/// 同一個亂數種子一定排出一樣的地圖。
/// </summary>
public static class DungeonLayout
{
    public class Room
    {
        /// <summary>格子座標。cx：左右；cz：離 Boss 多遠（0 = 最靠近 Boss 的那一排，越大越遠）</summary>
        public int cx, cz;
        /// <summary>房間內部的半寬 / 半深 (m)</summary>
        public int halfX, halfZ;
        /// <summary>在主線上的第幾間（0 = 最靠近 Boss）；岔路房間是 -1</summary>
        public int mainIndex = -1;
        public bool isStart;
        /// <summary>相連的房間（rooms 清單裡的編號）</summary>
        public readonly List<int> links = new List<int>();
    }

    static readonly int[] DX = { -1, 1, 0, 0 };
    static readonly int[] DZ = { 0, 0, 1, -1 };   // 最後一個（-1）= 往 Boss 的方向，只有岔路能用

    /// <param name="totalMin">房間總數的下限（含起點房間）</param>
    /// <param name="totalMax">房間總數的上限</param>
    /// <param name="maxSideRooms">最多幾間岔路房間</param>
    /// <param name="halfSizes">房間半邊長的選項 (m)，例如 { 10, 14 } = 20m 或 28m 的房間</param>
    public static List<Room> Generate(System.Random rng, int totalMin, int totalMax, int maxSideRooms, int[] halfSizes)
    {
        if (totalMin < 2) totalMin = 2;
        if (totalMax < totalMin) totalMax = totalMin;
        int total = rng.Next(totalMin, totalMax + 1);
        int side = rng.Next(0, System.Math.Min(maxSideRooms, total - 3) + 1);
        if (side < 0) side = 0;
        int main = total - side;

        for (int attempt = 0; attempt < 200; attempt++)
        {
            var rooms = new List<Room>();
            var used = new HashSet<long>();

            // ---- 主線：不走回頭路的隨機漫步 ----
            int x = 0, z = 0;
            rooms.Add(NewRoom(rng, x, z, halfSizes, 0));
            used.Add(Key(x, z));
            bool stuck = false;
            for (int i = 1; i < main; i++)
            {
                var options = new List<int>();
                for (int d = 0; d < 3; d++) // 左、右、遠離 Boss
                    if (!used.Contains(Key(x + DX[d], z + DZ[d]))) options.Add(d);
                if (options.Count == 0) { stuck = true; break; }
                int dir = options[rng.Next(options.Count)];
                x += DX[dir];
                z += DZ[dir];
                rooms.Add(NewRoom(rng, x, z, halfSizes, i));
                used.Add(Key(x, z));
                rooms[i - 1].links.Add(i);
                rooms[i].links.Add(i - 1);
            }
            if (stuck) continue;

            var start = rooms[main - 1];
            start.isStart = true;
            start.halfX = start.halfZ = Min(halfSizes); // 起點房間用最小的

            // ---- 岔路 ----
            for (int s = 0; s < side; s++)
            {
                var spots = new List<int[]>();
                for (int i = 0; i < main; i++)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = rooms[i].cx + DX[d], nz = rooms[i].cz + DZ[d];
                        if (nz < 0 || used.Contains(Key(nx, nz))) continue; // 第 0 排再往 Boss 那邊就是競技場了
                        spots.Add(new[] { i, nx, nz });
                    }
                }
                if (spots.Count == 0) break;
                var pick = spots[rng.Next(spots.Count)];
                var room = NewRoom(rng, pick[1], pick[2], halfSizes, -1);
                rooms.Add(room);
                used.Add(Key(pick[1], pick[2]));
                rooms[pick[0]].links.Add(rooms.Count - 1);
                room.links.Add(pick[0]);
            }
            return rooms;
        }

        // 幾乎不可能走到這裡；保險起見給一條直線
        var fallback = new List<Room>();
        for (int i = 0; i < main; i++)
        {
            fallback.Add(NewRoom(rng, 0, i, halfSizes, i));
            if (i > 0) { fallback[i - 1].links.Add(i); fallback[i].links.Add(i - 1); }
        }
        fallback[main - 1].isStart = true;
        return fallback;
    }

    static Room NewRoom(System.Random rng, int cx, int cz, int[] halfSizes, int mainIndex)
    {
        return new Room
        {
            cx = cx, cz = cz, mainIndex = mainIndex,
            halfX = halfSizes[rng.Next(halfSizes.Length)],
            halfZ = halfSizes[rng.Next(halfSizes.Length)],
        };
    }

    static int Min(int[] values)
    {
        int m = values[0];
        foreach (int v in values) if (v < m) m = v;
        return m;
    }

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
}
