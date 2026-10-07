using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 圓形競技場的地板：由幾百塊形狀不規則的石板 (FloorTile) 拼成。
/// 石板之間剛好密合、用同一個材質，平常看起來是一整片；塌陷、凹陷時才看得出不規則的形狀。
/// （想平常就看得到裂縫：把 Slab Bevel 和 Shade Variation 調大再重新產生地板）
/// - 提供「找出扇形範圍 / 某位置附近的石板 / 某位置的地面高度」等查詢給 Boss 和波次戰使用
/// - GenerateTerrain：地面保持平的，從地底冒出岩石（Boss 戰開始時由 BossController 呼叫）
///     大岩柱：推不動，當掩體擋光束
///     小石塊：玩家可以推著走
///   岩石都是 ArenaRock，會被光束打碎、被 Boss 撞開、跟著塌陷的地板掉下去
///
/// 在 Inspector 對這個元件按右鍵：
///   「重新產生地板」→ 用新的半徑 / 石板大小重建（編輯模式用）
///   「預覽地形」    → 遊戲執行中立刻換一批新的岩石
/// </summary>
[ExecuteAlways]
public class ArenaFloor : MonoBehaviour
{
    [Header("地板")]
    [Tooltip("可站立區域的半徑 (m)")]
    public float radius = 18f;
    [Tooltip("「一格」的長度 (m)。只是個單位：波次戰的切線寬度、生怪預警的範圍、貼圖的大小都以它計算")]
    public float tileSize = 1.5f;
    [Tooltip("石板的厚度 (m)。要比拍地 / 跳砸的凹陷深度厚，凹下去時才不會看到石板底下")]
    public float tileThickness = 2.5f;
    [Tooltip("會塌的石板伸進牆裡多少 (m)。貼牆的石板掉下去後如果牆腳還看得到一條沒掉的邊，就調大一點")]
    public float wallOverlap = 0.25f;
    public Material tileMaterial;

    [Header("石板形狀（改完要按右鍵「重新產生地板」）")]
    [Tooltip("石板的平均寬度 (m)")]
    public float slabSize = 2f;
    [Tooltip("形狀不規則的程度：0 = 整齊的蜂巢，0.45 = 很亂")]
    [Range(0f, 0.48f)] public float slabJitter = 0.42f;
    [Tooltip("大石板的比例：越大 → 石板大小差異越大")]
    [Range(0f, 0.35f)] public float bigSlabChance = 0.14f;
    [Tooltip("上緣斜角的寬度 (m)，也就是石板之間的溝。0 = 沒有溝，平常看起來是一整片，塌下去時才看得出形狀；想看到裂縫就設 0.05 左右")]
    public float slabBevel = 0f;
    [Tooltip("每塊石板明暗的隨機差異。0 = 全部同一個顏色（看不出一塊一塊的，也比較省效能）")]
    [Range(0f, 0.3f)] public float shadeVariation = 0f;
    [Tooltip("排列的亂數種子。0 = 每次重新產生都不一樣")]
    public int layoutSeed = 0;
    /// <summary>目前這批石板產生時用的 slabSize（查石板要用這個，不能用 Inspector 上改過但還沒重新產生的值）</summary>
    [SerializeField, HideInInspector] float generatedSlabSize;

    [Header("岩石：共用")]
    [Tooltip("岩石的材質（不填就用地板的材質）")]
    public Material rockMaterial;
    [Tooltip("想用自己做的岩石 Prefab 就拖進來（上面要有或會自動加上 ArenaRock）；不填 = 用程式產生的岩石")]
    public ArenaRock bigRockPrefab;
    public ArenaRock smallRockPrefab;

    [Header("岩石：大岩柱（推不動，當掩體）")]
    public int bigRockCount = 16;
    [Tooltip("大岩柱之間最少間隔 (m)")]
    public float bigRockSpacing = 9f;
    [Tooltip("高度範圍 (m)")]
    public Vector2 bigRockHeight = new Vector2(3.5f, 5.5f);
    [Tooltip("半徑範圍 (m)")]
    public Vector2 bigRockRadius = new Vector2(1.3f, 2f);

    [Header("岩石：小石塊（可以推）")]
    public int smallRockCount = 20;
    [Tooltip("小石塊和其他岩石之間最少間隔 (m)")]
    public float smallRockSpacing = 5f;
    public Vector2 smallRockHeight = new Vector2(1.1f, 1.7f);
    public Vector2 smallRockRadius = new Vector2(0.75f, 1.1f);
    [Tooltip("推小石塊時玩家的速度剩下幾成：x = 最小顆的，y = 最大顆的（越大顆越重）")]
    public Vector2 pushSpeedFactor = new Vector2(0.55f, 0.35f);

    [Header("岩石：保留空地")]
    [Tooltip("場地中央這個半徑內不放岩石（Boss 放光束的位置）")]
    public float flatCenterRadius = 4f;
    [Tooltip("玩家 / Boss 周圍這個半徑內不放岩石，避免開場被岩石頂到")]
    public float keepFlatRadius = 3f;

    [Header("岩石：升起動畫")]
    public float riseDuration = 1.2f;
    [Tooltip("升起的波從中央往外擴散的速度 (m/s)")]
    public float riseWaveSpeed = 14f;

    public List<FloorTile> Tiles { get; } = new List<FloorTile>();
    /// <summary>目前場上還在的岩石</summary>
    public List<ArenaRock> Rocks { get; } = new List<ArenaRock>();
    public Vector3 Center => transform.position;

    // 查「某個位置屬於哪一塊石板」用的格子：每一格記著種子點落在裡面的石板
    readonly Dictionary<Vector2Int, List<FloorTile>> buckets = new Dictionary<Vector2Int, List<FloorTile>>();
    float bucketSize = 3f;
    int bucketReach = 2;
    Transform rockContainer;

    void Awake() => CollectTiles();

    void OnEnable()
    {
        // 編輯模式下重新編譯之後，清單會被清空，要再收集一次
        if (Tiles.Count == 0) CollectTiles();
    }

    void CollectTiles()
    {
        Tiles.Clear();
        GetComponentsInChildren(true, Tiles);
        BuildLookup();
    }

    void BuildLookup()
    {
        buckets.Clear();
        bool legacy = false;
        foreach (var t in Tiles)
            if (!t.HasShape) { legacy = true; break; }

        // 舊版的方塊地板：種子點 = 方塊中心，每格一塊；不規則石板：種子點比較散，多找幾格
        bucketSize = legacy ? Mathf.Max(tileSize, 0.1f) : Mathf.Max(LookupSlabSize, 0.1f) * 1.5f;
        bucketReach = legacy ? 1 : 2;
        foreach (var t in Tiles)
        {
            var key = BucketOf(t.Site);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<FloorTile>();
            list.Add(t);
        }
        if (legacy && Application.IsPlaying(gameObject))
            Debug.LogWarning("[ArenaFloor] 地板還是舊版的方塊。請執行選單 GeminiGrowUp > 更新 Boss 房地板（不規則石板），換成新的石板地。", this);
    }

    float LookupSlabSize => generatedSlabSize > 0f ? generatedSlabSize : slabSize;

#if UNITY_EDITOR
    // 編輯模式：場景有變動時檢查一下石板的模型還在不在（Prefab 重新存檔後會被清掉）
    void Update()
    {
        if (Application.IsPlaying(gameObject)) return;
        foreach (var t in Tiles)
            if (t != null) t.EnsureMeshAssigned();
    }
#endif

    Vector2Int BucketOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / bucketSize), Mathf.FloorToInt(p.y / bucketSize));

    // ---------------- 查詢 ----------------

    /// <summary>取得某個世界座標正下方的石板（場地外面就回傳 null）</summary>
    public FloorTile GetTileAt(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos); // 支援 Prefab 被移動 / 旋轉
        var p = new Vector2(local.x, local.z);
        var centre = BucketOf(p);

        // 石板 = 離它的種子點最近的範圍，所以找最近的種子點就好
        FloorTile best = null;
        float bestDist = float.MaxValue;
        for (int dx = -bucketReach; dx <= bucketReach; dx++)
        {
            for (int dy = -bucketReach; dy <= bucketReach; dy++)
            {
                if (!buckets.TryGetValue(new Vector2Int(centre.x + dx, centre.y + dy), out var list)) continue;
                foreach (var t in list)
                {
                    float d = (t.Site - p).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = t; }
                }
            }
        }
        if (best == null) return null;
        // 最外圈再往外就不是地板了
        float limit = Mathf.Max(LookupSlabSize, tileSize) * 2f;
        return bestDist <= limit * limit ? best : null;
    }

    /// <summary>這個位置下面有沒有可以站的地板</summary>
    public bool IsSolidAt(Vector3 worldPos)
    {
        var t = GetTileAt(worldPos);
        return t != null && t.IsSolid;
    }

    /// <summary>
    /// 某位置的地面高度（不含凹陷）。下面是洞或場外就回傳 false。
    /// </summary>
    public bool TryGetGroundHeight(Vector3 worldPos, out float height)
    {
        var t = GetTileAt(worldPos);
        if (t == null || !t.IsSolid) { height = 0f; return false; }
        height = t.RestTopY;
        return true;
    }

    /// <summary>
    /// 中心的水平距離在 range 以內的石板，加上 worldPos 正下方那一塊
    /// （石板比較大，range 很小時可能沒有任何一塊的中心在範圍內，但正下方那塊一定要算）。
    /// </summary>
    public List<FloorTile> GetTilesNear(Vector3 worldPos, float range)
    {
        var result = new List<FloorTile>();
        foreach (var t in Tiles)
            if (FlatDistance(t.RestPosition, worldPos) <= range) result.Add(t);
        if (FlatDistance(worldPos, Center) <= radius + 0.5f)
        {
            var under = GetTileAt(worldPos);
            if (under != null && !result.Contains(under)) result.Add(under);
        }
        return result;
    }

    /// <summary>以 origin 為圓心、面向 forward 的扇形範圍內的石板</summary>
    public List<FloorTile> GetTilesInFan(Vector3 origin, Vector3 forward, float halfAngle, float range, float minRange = 0f)
    {
        var result = new List<FloorTile>();
        forward.y = 0f;
        forward.Normalize();
        foreach (var t in Tiles)
        {
            Vector3 to = t.RestPosition - origin;
            to.y = 0f;
            float d = to.magnitude;
            if (d < minRange || d > range) continue;
            if (Vector3.Angle(forward, to) <= halfAngle) result.Add(t);
        }
        return result;
    }

    /// <summary>所有石板立刻恢復；flatten = true 時連岩石也清掉（回到一片平地）</summary>
    public void ResetAllTiles(bool flatten = false)
    {
        foreach (var t in Tiles) t.ResetTile(flatten);
        if (flatten) ClearRocks();
    }

    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------------- 岩石 ----------------

    /// <summary>
    /// 產生新的一批岩石，從中央往外依序從地底冒出來。地面本身保持平的。
    /// keepFlat：這些位置附近不放岩石（通常傳入玩家和 Boss 的位置）。
    /// </summary>
    public void GenerateTerrain(IList<Vector3> keepFlat)
    {
        ClearRocks();

        // 還沒復原的地板（波次戰留下的洞）跟著岩石一起升回來
        foreach (var t in Tiles)
            t.RestoreNow(FlatDistance(t.RestPosition, Center) / Mathf.Max(riseWaveSpeed, 0.1f));

        // 可以放岩石的石板：會塌的、不靠牆、不在中央、離玩家和 Boss 夠遠
        var candidates = new List<FloorTile>();
        foreach (var t in Tiles)
        {
            if (!t.canCollapse) continue;
            float d = FlatDistance(t.RestPosition, Center);
            if (d < flatCenterRadius + 2f || d > radius - 3f) continue;
            if (!FarFrom(t.RestPosition, keepFlat, keepFlatRadius + 2.5f)) continue;
            candidates.Add(t);
        }
        Shuffle(candidates);

        var placed = new List<Vector3>();
        int big = 0, small = 0;

        foreach (var c in candidates)
        {
            if (big >= bigRockCount) break;
            if (!FarFrom(c.RestPosition, placed, bigRockSpacing)) continue;
            placed.Add(c.RestPosition);
            SpawnRock(c, true);
            big++;
        }
        foreach (var c in candidates)
        {
            if (small >= smallRockCount) break;
            if (!FarFrom(c.RestPosition, placed, smallRockSpacing)) continue;
            placed.Add(c.RestPosition);
            SpawnRock(c, false);
            small++;
        }
    }

    void SpawnRock(FloorTile tile, bool big)
    {
        if (rockContainer == null)
        {
            rockContainer = new GameObject("Rocks").transform;
            rockContainer.SetParent(transform.parent, false);
        }

        Vector3 pos = tile.RestPosition;
        pos.y = tile.RestTopY;
        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        ArenaRock prefab = big ? bigRockPrefab : smallRockPrefab;
        ArenaRock rock;
        if (prefab != null)
        {
            rock = Instantiate(prefab, pos, rot, rockContainer);
        }
        else
        {
            // 先關著建立，數值都設好再打開，模型才只會產生一次
            var go = new GameObject(big ? "RockPillar" : "Boulder");
            go.SetActive(false);
            go.transform.SetParent(rockContainer, false);
            go.transform.SetPositionAndRotation(pos, rot);
            rock = go.AddComponent<ArenaRock>();
            rock.generateMesh = true;
            rock.seed = 0;
            rock.material = rockMaterial != null ? rockMaterial : tileMaterial;

            float size = Random.value; // 0 = 這一類裡最小的，1 = 最大的
            if (big)
            {
                rock.spire = true;
                rock.radius = Mathf.Lerp(bigRockRadius.x, bigRockRadius.y, size);
                rock.height = Mathf.Lerp(bigRockHeight.x, bigRockHeight.y, Random.value);
                rock.debrisCount = 12;
            }
            else
            {
                rock.spire = false;
                rock.radius = Mathf.Lerp(smallRockRadius.x, smallRockRadius.y, size);
                rock.height = Mathf.Lerp(smallRockHeight.x, smallRockHeight.y, size);
                rock.pushSpeedFactor = Mathf.Lerp(pushSpeedFactor.x, pushSpeedFactor.y, size);
                rock.debrisCount = 7;
            }
            go.SetActive(true);
        }

        rock.pushable = !big;
        rock.Arena = this;
        Rocks.Add(rock);
        rock.Rise(FlatDistance(pos, Center) / Mathf.Max(riseWaveSpeed, 0.1f), riseDuration);
    }

    /// <summary>清掉場上所有岩石（重置戰鬥時）</summary>
    public void ClearRocks()
    {
        foreach (var r in Rocks)
            if (r != null) Destroy(r.gameObject);
        Rocks.Clear();
        // 碎石、正在掉下去的岩石也一起清掉
        if (rockContainer != null)
            for (int i = rockContainer.childCount - 1; i >= 0; i--)
                Destroy(rockContainer.GetChild(i).gameObject);
    }

    /// <summary>岩石碎掉 / 掉下去時通知地板（ArenaRock 自己會呼叫）</summary>
    public void ForgetRock(ArenaRock rock) => Rocks.Remove(rock);

    /// <summary>
    /// Boss 的身體碰到岩石：小石塊被撞開，大岩柱被撞碎。Boss 移動時每一幀呼叫。
    /// </summary>
    public void TouchRocks(Vector3 position, float bodyRadius, Transform attacker)
    {
        for (int i = Rocks.Count - 1; i >= 0; i--)
        {
            var r = Rocks[i];
            if (r == null) { Rocks.RemoveAt(i); continue; }
            if (r.CurrentState == ArenaRock.State.Sliding || r.CurrentState == ArenaRock.State.Rising) continue;
            if (FlatDistance(r.transform.position, position) > bodyRadius + r.radius * 0.8f) continue;
            r.Smash(position, 9f, 0f, attacker);
        }
    }

    /// <summary>
    /// 範圍攻擊打到岩石（跳砸、登場）：
    /// shatterRadius 以內不管大小全部碎掉；到 knockRadius 為止的小石塊被震開（大岩柱沒事）。
    /// waveSpeed：衝擊波往外擴散的速度，越遠的越晚被震開。
    /// </summary>
    public void SmashRocksNear(Vector3 center, float shatterRadius, float knockRadius, float waveSpeed, Transform attacker)
    {
        for (int i = Rocks.Count - 1; i >= 0; i--)
        {
            var r = Rocks[i];
            if (r == null) { Rocks.RemoveAt(i); continue; }
            float d = FlatDistance(r.transform.position, center);
            if (d <= shatterRadius + r.radius * 0.5f) r.Shatter();
            else if (d <= knockRadius && r.pushable)
                r.Smash(center, Mathf.Lerp(11f, 4f, Mathf.InverseLerp(shatterRadius, knockRadius, d)),
                        (d - shatterRadius) / Mathf.Max(waveSpeed, 0.1f), attacker);
        }
    }

    /// <summary>
    /// 扇形的範圍攻擊打到岩石（拍地）：
    /// breakRange 以內的大岩柱碎掉；整個扇形裡的小石塊被震開，越遠力道越小。
    /// </summary>
    public void SmashRocksInFan(Vector3 origin, Vector3 forward, float halfAngle, float range, float minRange,
                                float breakRange, float waveSpeed, Transform attacker)
    {
        forward.y = 0f;
        forward.Normalize();
        for (int i = Rocks.Count - 1; i >= 0; i--)
        {
            var r = Rocks[i];
            if (r == null) { Rocks.RemoveAt(i); continue; }
            Vector3 to = r.transform.position - origin;
            to.y = 0f;
            float d = to.magnitude;
            if (d > range + r.radius || Vector3.Angle(forward, to) > halfAngle) continue;
            if (!r.pushable && d > breakRange) continue;
            float delay = Mathf.Max(0f, d - minRange) / Mathf.Max(waveSpeed, 0.1f);
            r.Smash(origin, Mathf.Lerp(12f, 4f, Mathf.Clamp01(d / Mathf.Max(range, 0.1f))), delay, attacker);
        }
    }

    static bool FarFrom(Vector3 p, IList<Vector3> others, float distance)
    {
        if (others == null) return true;
        foreach (var o in others)
            if (FlatDistance(p, o) < distance) return false;
        return true;
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    [ContextMenu("預覽地形 (執行中 Preview Terrain)")]
    void PreviewTerrain()
    {
        if (!Application.isPlaying) { Debug.LogWarning("請在按下 Play 之後再使用「預覽地形」"); return; }
        var keep = new List<Vector3>();
        var player = GameObject.FindWithTag("Player");
        if (player != null) keep.Add(player.transform.position);
        GenerateTerrain(keep);
    }

    // ---------------- 產生地板（編輯模式） ----------------

    [ContextMenu("重新產生地板 (Regenerate Floor)")]
    public void Generate()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        // 石板鋪到「牆的內側再往裡面一點點」為止，全部都會塌（包括貼著牆的那一圈）
        int seed = layoutSeed != 0 ? layoutSeed : Random.Range(1, int.MaxValue);
        float edge = radius + wallOverlap;
        var cells = ShatterPattern.Generate(edge, slabSize, slabJitter, bigSlabChance, 1, seed);
        var shadeRng = new System.Random(seed);

        int index = 0;
        foreach (var cell in cells)
        {
            float shade = 1f + ((float)shadeRng.NextDouble() * 2f - 1f) * shadeVariation;
            CreateTile($"Slab_{index++:0000}", cell.site, cell.centroid, 0f, cell.outline, slabBevel, shade, true);
        }

        // 牆的正下方另外墊一圈不會塌的底座（平常被牆擋住看不到）：
        // 貼牆的石板掉下去時，牆的下面才不會是空的；大門口也一直有地方可以站
        int rimCount = Mathf.Max(24, Mathf.CeilToInt(2f * Mathf.PI * edge / Mathf.Max(slabSize * 1.8f, 0.5f)));
        float rimInner = edge - 0.03f, rimOuter = edge + Mathf.Max(slabSize * 1.5f, 2.5f);
        for (int i = 0; i < rimCount; i++)
        {
            float a0 = i * Mathf.PI * 2f / rimCount, a1 = (i + 1) * Mathf.PI * 2f / rimCount;
            Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            var outline = new[] { d0 * rimInner, d0 * rimOuter, d1 * rimOuter, d1 * rimInner };
            Vector2 centroid = ShatterPattern.Centroid(outline);
            // 種子點放在最外側：場地裡面的位置查石板時才不會查到底座
            Vector2 site = centroid.normalized * rimOuter;
            // 比地板低一點點，跟石板、門檻重疊的地方才不會閃爍
            CreateTile($"Rim_{i:000}", site, centroid, -0.04f, outline, 0f, 1f, false);
        }

        generatedSlabSize = slabSize;
        Tiles.Clear();
        GetComponentsInChildren(true, Tiles);
        BuildLookup();
#if UNITY_EDITOR
        // 讓 Unity 知道這裡改過了，存檔時才會存進去
        if (!Application.IsPlaying(gameObject))
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
        Debug.Log($"[ArenaFloor] 產生了 {cells.Count} 塊石板");
    }

    void CreateTile(string tileName, Vector2 site, Vector2 centroid, float y, Vector2[] outline, float bevel, float shade, bool canCollapse)
    {
        var go = new GameObject(tileName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(centroid.x, y, centroid.y);

        go.AddComponent<MeshFilter>();
        var meshRenderer = go.AddComponent<MeshRenderer>();
        if (tileMaterial != null) meshRenderer.sharedMaterial = tileMaterial;
        go.AddComponent<MeshCollider>().convex = true;

        var tile = go.AddComponent<FloorTile>();
        tile.site = site;
        tile.uvOrigin = centroid;
        tile.outline = new Vector2[outline.Length];
        for (int i = 0; i < outline.Length; i++) tile.outline[i] = outline[i] - centroid;
        tile.thickness = tileThickness;
        tile.bevel = bevel;
        tile.uvSize = Mathf.Max(tileSize, 0.1f);
        tile.shade = shade;
        tile.canCollapse = canCollapse;
        tile.Rebuild();
    }
}
