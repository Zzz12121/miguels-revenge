using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 蓋房間用的素材（Kenney Retro Fantasy Kit 的城堡零件，一格 4m）。
/// 由選單「GeminiGrowUp > 安裝隨機房間（第二關）」自動填好；沒填的話會用灰色方塊代替。
/// </summary>
[System.Serializable]
public class DungeonKit
{
    [Tooltip("城牆（4x4x4m 的方塊）")]
    public GameObject wall;
    [Tooltip("有窗戶的城牆（放在二樓）")]
    public GameObject wallWindow;
    [Tooltip("有彩繪的城牆（放在一樓）")]
    public GameObject wallPaint;
    [Tooltip("牆頂的城垛")]
    public GameObject battlement;
    [Tooltip("地板（4x4m）")]
    public GameObject floor;
    [Tooltip("靠牆擺的雜物（木桶、木箱…）。擺出來的會自動變成可以打飛的 KnockableProp")]
    public GameObject[] props;
    public Material doorMaterial;
    public Material torchWoodMaterial;
    public Material torchFlameMaterial;
}

/// <summary>
/// 隨機地城：每次按 Play 都重新排一次房間（類似元氣騎士的關卡）。
///
///   起點房間 → 戰鬥房間 ×N（偶爾有岔路）→ Boss 門前的小房間（檢查點）→ Boss 房大門
///
/// - 房間排在 36m 一格的格子上，大小是 20m 或 28m，中間用短通道連起來，通道中間有一道門
/// - 走進戰鬥房間 → 門全部關上、小怪從地底鑽出來；殺光 → 門打開（邏輯在 DungeonRoom）
/// - 玩家死掉：地圖不變，從起點（或已經碰過的檢查點）重生，清過的房間不用重打
///
/// 整座地城以 Boss 房的大門為基準往外蓋，所以 BossRoom 搬到哪裡、轉什麼方向都會跟著對齊。
/// 想重現某一次的地圖：看 Console 印出的種子，填進 Seed。
/// </summary>
[DefaultExecutionOrder(-200)] // 要比玩家、檢查點、小怪都早執行：先把地城蓋好、把玩家放到起點
public class DungeonGenerator : MonoBehaviour
{
    [Header("房間數量（含起點房間，不含 Boss 門前的小房間）")]
    public Vector2Int roomCount = new Vector2Int(4, 6);
    [Tooltip("最多幾間岔路（死路）房間")]
    public int maxSideRooms = 2;
    [Tooltip("亂數種子。0 = 每次都不一樣")]
    public int seed = 0;

    [Header("每間戰鬥房間的小怪")]
    public GameObject gruntPrefab;
    public GameObject ambusherPrefab;
    [Tooltip("每間幾隻（隨機範圍）")]
    public Vector2Int enemiesPerRoom = new Vector2Int(3, 5);
    [Tooltip("比較大的房間（28m）多幾隻")]
    public int extraEnemiesInBigRoom = 1;
    [Tooltip("離 Boss 越近的房間多幾隻（每靠近一間加這麼多，可以是小數）")]
    public float extraEnemiesPerDepth = 0.5f;
    [Range(0f, 1f)] public float ambusherChance = 0.3f;

    [Header("補血道具")]
    [Tooltip("每間戰鬥房間和 Boss 門前的小房間各放一個（選單 GeminiGrowUp > 放置補血道具 會自動填）。留空 = 不放")]
    public GameObject healthPickupPrefab;

    [Header("清完一間房間時的標題")]
    [Tooltip("小怪全部打倒時畫面上方出現的字。留空 = 不顯示")]
    public string roomClearTitle = "COMPLETE";
    [Tooltip("標題下面的小字（可以留空）")]
    public string roomClearSubtitle = "房間已清空";

    [Header("素材")]
    public DungeonKit kit = new DungeonKit();

    [Header("連接（不填會自動在場景裡找）")]
    public BossGate gate;
    public Checkpoint checkpoint;
    [Tooltip("遊戲開始時把玩家搬到起點房間")]
    public bool movePlayerToStart = true;

    /// <summary>這一次實際用的種子</summary>
    public int LastSeed { get; private set; }
    public List<DungeonRoom> Rooms { get; } = new List<DungeonRoom>();
    public DungeonRoom StartRoom { get; private set; }

    // ---- 尺寸（都是 4m 的倍數，城堡零件才對得上） ----
    const float U = 4f;                 // 一個城堡零件的大小
    const float Pitch = 36f;            // 格子的間距（房間中心到隔壁房間中心）
    static readonly int[] HalfSizes = { 10, 14 };   // 房間內部的半邊長：20m 或 28m
    const float GateRoomHalf = 6f;      // Boss 門前小房間的半邊長（12m）
    const float GateRoomCenterZ = -9f;  // 它的中心在大門後方多遠（大門的本地座標，-Z = 往外）
    const float WallHeight = 8f;        // 兩層樓高
    const float DoorHeight = 4f;
    const float WallTop = WallHeight + 1.6f;   // 連牆頂的城垛一起算
    const float TorchHeight = 2.3f;            // 火把掛多高：比人高一點，揮劍打得到（可以把它打下來）

    Transform root, floorParent, wallParent, doorParent, propParent, torchParent, colliderParent;
    float frontSign = 1f;   // 零件的「正面」在 Unity 裡是 +Z（1）還是 -Z（-1）
    System.Random rng;

    // 房間的四個方向（本地座標）：0 = +Z（往 Boss）、1 = -Z、2 = +X、3 = -X
    static readonly Vector3[] Dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

    void Awake()
    {
        if (gate == null) gate = FindInScene<BossGate>();
        if (checkpoint == null) checkpoint = FindInScene<Checkpoint>();
        DisableOldCorridor();
        Generate();
    }

    /// <summary>
    /// 保險：舊的手工走廊 / 小怪 / 伏擊點如果還開著，會跟新的房間疊在一起（牆會把房間切開）。
    /// 安裝選單已經把它們停用了；萬一又被打開，這裡在遊戲開始時再關一次。
    /// </summary>
    void DisableOldCorridor()
    {
        foreach (var go in gameObject.scene.GetRootGameObjects())
        {
            string n = go.name;
            if (!go.activeSelf || !(n.StartsWith("Corridor_") || n == "Enemies" || n.StartsWith("Ambush_"))) continue;
            go.SetActive(false);
            Debug.LogWarning($"[Dungeon] 場景裡舊的「{n}」還開著，會跟隨機房間重疊，已經在這次遊戲中把它關掉。不需要的話請在場景裡把它停用或刪掉。", go);
        }
    }

    T FindInScene<T>() where T : Component
    {
        foreach (var go in gameObject.scene.GetRootGameObjects())
        {
            var c = go.GetComponentInChildren<T>(false);
            if (c != null) return c;
        }
        return null;
    }

    // =====================================================================
    //  產生
    // =====================================================================

    void Generate()
    {
        LastSeed = seed != 0 ? seed : (System.Environment.TickCount ^ Random.Range(1, int.MaxValue));
        rng = new System.Random(LastSeed);

        // 整座地城放在一個以「大門」為原點的物件底下：本地 +Z = 往競技場裡面，-Z = 往外
        root = new GameObject("DungeonRooms").transform;
        if (gate != null) root.SetPositionAndRotation(gate.transform.position, gate.transform.rotation);
        else
        {
            root.SetPositionAndRotation(transform.position, transform.rotation);
            Debug.LogWarning("[Dungeon] 場景裡找不到 BossGate，地城會蓋在 DungeonGenerator 這個物件的位置。", this);
        }
        floorParent = Child("Floor");
        wallParent = Child("Walls");
        doorParent = Child("Doors");
        propParent = Child("Props");
        torchParent = Child("Torches");
        colliderParent = Child("Colliders");
        MeasureKit();

        var layout = DungeonLayout.Generate(rng, roomCount.x, roomCount.y, maxSideRooms, HalfSizes);

        // ---- 每間房間的位置 ----
        float z0 = GateRoomCenterZ - GateRoomHalf - 2f * U - layout[0].halfZ; // 第 0 間：跟門前小房間背靠背
        var centers = new Vector3[layout.Count];
        var doorSides = new bool[layout.Count][];
        for (int i = 0; i < layout.Count; i++)
        {
            centers[i] = new Vector3(layout[i].cx * Pitch, 0f, z0 - layout[i].cz * Pitch);
            doorSides[i] = new bool[4];
        }
        for (int i = 0; i < layout.Count; i++)
            foreach (int j in layout[i].links)
                doorSides[i][SideToward(layout[i], layout[j])] = true;
        doorSides[0][0] = true; // 第 0 間往 Boss 的方向接門前小房間

        // ---- 蓋房間 ----
        for (int i = 0; i < layout.Count; i++)
        {
            var info = layout[i];
            var room = BuildRoom($"Room_{i:00}" + (info.isStart ? "_Start" : info.mainIndex < 0 ? "_Side" : ""),
                                 centers[i], info.halfX, info.halfZ, doorSides[i], true);
            if (!info.isStart)
            {
                int count = rng.Next(Mathf.Min(enemiesPerRoom.x, enemiesPerRoom.y), Mathf.Max(enemiesPerRoom.x, enemiesPerRoom.y) + 1);
                if (info.halfX > HalfSizes[0] && info.halfZ > HalfSizes[0]) count += extraEnemiesInBigRoom;
                // 主線上越靠近 Boss 的房間越多；岔路房間算中間
                int fromStart = info.mainIndex >= 0 ? (MainLength(layout) - 1 - info.mainIndex) : MainLength(layout) / 2;
                count += Mathf.FloorToInt(Mathf.Max(0, fromStart - 1) * extraEnemiesPerDepth);
                room.enemyCount = Mathf.Max(1, count);
                room.gruntPrefab = gruntPrefab;
                room.ambusherPrefab = ambusherPrefab;
                room.ambusherChance = ambusherChance;
                room.clearTitle = roomClearTitle;
                room.clearSubtitle = roomClearSubtitle;
            }
            else StartRoom = room;
            Rooms.Add(room);
        }

        // Boss 門前的小房間：沒有小怪，放檢查點。往 Boss 那一面的開口就是 Boss 房的大門
        var gateCenter = new Vector3(0f, 0f, GateRoomCenterZ);
        var gateRoom = BuildRoom("Room_BossGate", gateCenter, (int)GateRoomHalf, (int)GateRoomHalf,
                                 new[] { true, true, false, false }, false);
        // 通往 Boss 大門的門洞底下補一塊地板（這一面沒有通道，不補的話門洞裡會露出一條縫）
        FloorTileAt(gateCenter + Vector3.forward * (GateRoomHalf + U * 0.5f));

        // ---- 通道和門 ----
        for (int i = 0; i < layout.Count; i++)
            foreach (int j in layout[i].links)
                if (j > i) BuildPassage(Rooms[i], centers[i], layout[i], Rooms[j], centers[j], layout[j]);
        BuildPassage(gateRoom, gateCenter, null, Rooms[0], centers[0], layout[0]);

        PlacePlayerAndCheckpoint(layout, centers, gateRoom);
        PlaceHealthPickups(layout, centers, gateCenter);

        // 打翻木桶、打掉火把時會用到的音效和煙：趁現在（剛進關卡）先準備好，第一次打到時才不會頓一下。
        // 煙的材質要真的被畫出來一次才算準備好，所以在起點房間正中央（玩家頭上）噴一顆完全透明的
        KnockableProp.Prewarm();
        KnockableTorch.Prewarm(root.TransformPoint(StartRoom != null ? StartRoom.transform.localPosition + Vector3.up * 2.5f : Vector3.up * 2.5f));
        Debug.Log($"[Dungeon] 產生了 {layout.Count} 間房間（種子 {LastSeed}）。想重現這張地圖就把這個數字填進 DungeonGenerator 的 Seed。", this);
    }

    /// <summary>補血道具：每間戰鬥房間放一個在角落附近（不擋路），Boss 門前的小房間也放一個</summary>
    void PlaceHealthPickups(List<DungeonLayout.Room> layout, Vector3[] centers, Vector3 gateCenter)
    {
        if (healthPickupPrefab == null) return;
        Physics.SyncTransforms();
        var parent = Child("HealthPickups");
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].isStart) continue;
            // 房間中心往隨機一個角落走一半的距離
            float sx = rng.Next(2) == 0 ? -1f : 1f, sz = rng.Next(2) == 0 ? -1f : 1f;
            var offset = new Vector3(sx * layout[i].halfX * 0.5f, 0f, sz * layout[i].halfZ * 0.5f);
            SpawnPickup(parent, centers[i] + offset);
        }
        SpawnPickup(parent, gateCenter + new Vector3(GateRoomHalf * 0.5f, 0f, 0f));
    }

    void SpawnPickup(Transform parent, Vector3 localPos)
    {
        Vector3 p = root.TransformPoint(localPos);
        // 貼到地板上
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            p = hit.point;
        var go = Instantiate(healthPickupPrefab, p, Quaternion.identity, parent);
        go.name = healthPickupPrefab.name;
    }

    static int MainLength(List<DungeonLayout.Room> layout)
    {
        int n = 0;
        foreach (var r in layout) if (r.mainIndex >= 0) n++;
        return n;
    }

    /// <summary>from 的哪一面朝向 to（0 = +Z 往 Boss、1 = -Z、2 = +X、3 = -X）</summary>
    static int SideToward(DungeonLayout.Room from, DungeonLayout.Room to)
    {
        if (to.cz < from.cz) return 0;   // cz 越小越靠近 Boss = 本地 +Z
        if (to.cz > from.cz) return 1;
        return to.cx > from.cx ? 2 : 3;
    }

    Transform Child(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    // =====================================================================
    //  房間
    // =====================================================================

    /// <summary>
    /// 蓋一間房間：地板、四面兩層樓高的城牆（有門的那一面中間留一個 4m 的開口）、火把、靠牆的雜物。
    /// center：房間中心（大門的本地座標）。doors：四個方向有沒有開口。
    /// </summary>
    DungeonRoom BuildRoom(string roomName, Vector3 center, int halfX, int halfZ, bool[] doors, bool decorate)
    {
        var go = new GameObject(roomName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = center;
        var room = go.AddComponent<DungeonRoom>();
        room.halfSize = new Vector2(halfX, halfZ);

        // 地板：4m 一塊，隨機轉向避免看起來重複
        int nx = Mathf.RoundToInt(halfX * 2f / U), nz = Mathf.RoundToInt(halfZ * 2f / U);
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                FloorTileAt(center + new Vector3(-halfX + U * (ix + 0.5f), 0f, -halfZ + U * (iz + 0.5f)));
        // 地板的碰撞連牆的正下方一起包進去
        AddBox("Floor_" + roomName, center + Vector3.down * 0.5f, new Vector3(halfX * 2f + U * 2f, 1f, halfZ * 2f + U * 2f));

        // 四面牆：每一面從中間往兩邊排（房間的邊長是 4m 的奇數倍，所以正中間剛好是一整塊，拿掉它就是門口）
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Dirs[side];
            Vector3 along = side < 2 ? Vector3.right : Vector3.forward;
            float depth = side < 2 ? halfZ : halfX;      // 中心到這面牆內側的距離
            float halfLen = side < 2 ? halfX : halfZ;    // 這面牆的半長
            int n = Mathf.RoundToInt((halfLen * 2f / U - 1f) * 0.5f);
            Vector3 wallCenter = center + outward * (depth + U * 0.5f);
            for (int k = -n; k <= n; k++)
            {
                bool isDoor = k == 0 && doors[side];
                WallBlock(wallCenter + along * (k * U), -outward, !isDoor);
            }

            // 火把：每面牆一支，28m 的長牆兩支；放在門口旁邊那一格，不會擋到門
            if (decorate || side >= 2)
            {
                int tk = Mathf.Max(1, n - 1);
                Torch(center + outward * (depth - 0.05f) + along * (tk * U) + Vector3.up * TorchHeight, -outward);
                if (n >= 3) Torch(center + outward * (depth - 0.05f) - along * (tk * U) + Vector3.up * TorchHeight, -outward);
            }
        }
        // 四個角落
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                WallBlock(center + new Vector3(sx * (halfX + U * 0.5f), 0f, sz * (halfZ + U * 0.5f)), new Vector3(0f, 0f, -sz), true);

        if (decorate) PlaceProps(center, halfX, halfZ, doors);
        return room;
    }

    /// <summary>靠牆隨機擺幾個木桶、木箱（有碰撞、可以打飛；不放在門口前面，離牆很近不擋路）</summary>
    void PlaceProps(Vector3 center, int halfX, int halfZ, bool[] doors)
    {
        if (kit.props == null || kit.props.Length == 0) return;
        int count = rng.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            var prefab = kit.props[rng.Next(kit.props.Length)];
            if (prefab == null) continue;
            int side = rng.Next(4);
            Vector3 outward = Dirs[side];
            Vector3 along = side < 2 ? Vector3.right : Vector3.forward;
            float depth = side < 2 ? halfZ : halfX;
            float halfLen = side < 2 ? halfX : halfZ;
            // 沿著牆的位置：避開正中間的門口（±4m）
            float t = Mathf.Lerp(4.5f, halfLen - 1.5f, (float)rng.NextDouble()) * (rng.Next(2) == 0 ? -1f : 1f);
            var go = Instantiate(prefab, propParent);
            go.name = prefab.name;
            go.transform.rotation = root.rotation * Quaternion.Euler(0f, 90f * rng.Next(4) + (float)rng.NextDouble() * 20f - 10f, 0f);
            // 可以被打飛 / 用走的頂開（沒被碰過之前固定不動）
            var prop = KnockableProp.Setup(go);
            // 離牆的距離依照它轉向之後實際有多「厚」來算，再留 10cm：不管怎麼轉都不會卡進牆裡
            float reach = prop.HalfExtentAlong(root.TransformDirection(outward));
            Vector3 pos = center + outward * (depth - reach - 0.1f) + along * t;
            go.transform.position = root.TransformPoint(pos);
        }
    }

    // =====================================================================
    //  通道和門
    // =====================================================================

    /// <summary>
    /// 連接兩間相鄰的房間：鋪地板、兩側補牆、中間放一道門（兩間共用）。
    /// infoA / infoB 傳 null 代表那一間是 Boss 門前的小房間。
    /// </summary>
    void BuildPassage(DungeonRoom a, Vector3 ca, DungeonLayout.Room infoA, DungeonRoom b, Vector3 cb, DungeonLayout.Room infoB)
    {
        Vector3 delta = cb - ca;
        bool alongX = Mathf.Abs(delta.x) > Mathf.Abs(delta.z);
        Vector3 dir = alongX ? new Vector3(Mathf.Sign(delta.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(delta.z));
        Vector3 lateral = alongX ? Vector3.forward : Vector3.right;
        float halfA = alongX ? a.halfSize.x : a.halfSize.y;
        float halfB = alongX ? b.halfSize.x : b.halfSize.y;

        Vector3 start = ca + dir * halfA;               // A 的牆內側
        Vector3 end = cb - dir * halfB;                 // B 的牆內側
        float length = Vector3.Distance(start, end);    // 一定是 4m 的倍數，至少 8m（兩道牆的厚度）
        int tiles = Mathf.Max(2, Mathf.RoundToInt(length / U));

        for (int i = 0; i < tiles; i++)
        {
            Vector3 p = start + dir * (U * (i + 0.5f));
            FloorTileAt(p);
            // 頭尾兩格是穿過房間牆壁的門洞；中間露天的部分兩側要補牆
            if (i >= 1 && i <= tiles - 2)
            {
                WallBlock(p + lateral * U, -lateral, true);
                WallBlock(p - lateral * U, lateral, true);
            }
        }
        Vector3 mid = (start + end) * 0.5f;
        AddBox("Floor_Passage", mid + Vector3.down * 0.5f,
               alongX ? new Vector3(length, 1f, U * 3f) : new Vector3(U * 3f, 1f, length));

        // 門：放在通道正中間，寬度剛好塞滿 4m 的門洞
        var doorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorGo.name = "Door";
        doorGo.transform.SetParent(doorParent, false);
        doorGo.transform.localPosition = mid + Vector3.up * (DoorHeight * 0.5f);
        doorGo.transform.localRotation = Quaternion.LookRotation(dir);
        doorGo.transform.localScale = new Vector3(U - 0.02f, DoorHeight, 0.8f);
        if (kit.doorMaterial != null) doorGo.GetComponent<MeshRenderer>().sharedMaterial = kit.doorMaterial;
        var body = doorGo.AddComponent<Rigidbody>(); // 會移動的碰撞體要有 Rigidbody（Kinematic = 位置由程式控制）
        body.isKinematic = true;
        body.useGravity = false;
        var door = doorGo.AddComponent<DungeonDoor>();
        door.height = DoorHeight;
        door.SetImmediate(false); // 一開始是開著的
        a.doors.Add(door);
        b.doors.Add(door);
    }

    // =====================================================================
    //  零件
    // =====================================================================

    /// <summary>
    /// 一格牆：一樓 + 二樓 + 城垛，共 8m 高。basePos = 這一格的中心（地面高度），inward = 朝向房間裡面。
    /// lower = false 時一樓留空當門口（二樓和城垛照樣蓋，門洞上面才不會是空的）。
    /// </summary>
    void WallBlock(Vector3 basePos, Vector3 inward, bool lower)
    {
        if (lower)
        {
            bool paint = kit.wallPaint != null && rng.Next(5) == 0;
            Piece(paint ? kit.wallPaint : kit.wall, wallParent, basePos, -inward, new Vector3(U, U, U));
            // 碰撞從地板下面 1m 包到城垛頂端（鏡頭拉遠、俯角很大時會升到比牆還高）
            AddBox("Wall", basePos + Vector3.up * ((WallTop - 1f) * 0.5f), new Vector3(U, WallTop + 1f, U));
        }
        else
        {
            // 門洞上面那一段牆（4m 以上）也要有碰撞：人碰不到它，但鏡頭的防穿牆是靠碰撞判斷的，
            // 沒有的話，站在門洞裡把鏡頭往上抬，鏡頭就會跑到這段牆的裡面或後面
            AddBox("Wall_AboveDoor", basePos + Vector3.up * ((DoorHeight + WallTop) * 0.5f), new Vector3(U, WallTop - DoorHeight, U));
        }
        bool window = kit.wallWindow != null && rng.Next(4) == 0;
        Piece(window ? kit.wallWindow : kit.wall, wallParent, basePos + Vector3.up * U, -inward, new Vector3(U, U, U));
        // 這套零件的窗戶、彩繪和城垛凸出的部分都在零件的「背面」→ 正面朝外，這些細節才會在房間這一側
        Piece(kit.battlement, wallParent, basePos + Vector3.up * (U * 2f), -inward, new Vector3(U, 1.6f, 1.2f));
    }

    void FloorTileAt(Vector3 pos)
    {
        Vector3 facing = Dirs[rng.Next(4)];
        var tile = Piece(kit.floor, floorParent, pos + Vector3.up * 0.005f, facing, new Vector3(U, 0.02f, U));
        // 地板是平的，不需要投射陰影（還是會接收陰影），少畫幾百次
        foreach (var r in tile.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// 放一個只有外觀的零件（碰撞另外用方塊做，比較平整不會卡腳）。
    /// 素材沒填的話，用 fallbackSize 大小的灰色方塊代替。
    /// </summary>
    GameObject Piece(GameObject prefab, Transform parent, Vector3 localPos, Vector3 localFront, Vector3 fallbackSize)
    {
        if (prefab == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos + Vector3.up * (fallbackSize.y * 0.5f);
            cube.transform.localScale = fallbackSize;
            return cube;
        }
        var go = Instantiate(prefab, parent);
        go.name = prefab.name;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.LookRotation(localFront * frontSign, Vector3.up);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        return go;
    }

    /// <summary>看不見的方塊碰撞（center / size 都是大門的本地座標）</summary>
    void AddBox(string boxName, Vector3 center, Vector3 size)
    {
        var go = new GameObject(boxName);
        go.transform.SetParent(colliderParent, false);
        go.transform.localPosition = center;
        go.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>火把：牆上的木架 + 火焰 + 暖色點光源（會閃爍，可以被打掉）。wallPos = 牆面上的位置，inward = 朝向房間裡面</summary>
    void Torch(Vector3 wallPos, Vector3 inward)
    {
        var torch = new GameObject("Torch");
        torch.transform.SetParent(torchParent, false);
        torch.transform.localPosition = wallPos;
        torch.transform.localRotation = Quaternion.LookRotation(inward);

        var stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stick.name = "Stick";
        Destroy(stick.GetComponent<Collider>());
        stick.transform.SetParent(torch.transform, false);
        stick.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        stick.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
        stick.transform.localScale = new Vector3(0.15f, 0.8f, 0.15f);
        if (kit.torchWoodMaterial != null) stick.GetComponent<MeshRenderer>().sharedMaterial = kit.torchWoodMaterial;

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flame.name = "Flame";
        Destroy(flame.GetComponent<Collider>());
        flame.transform.SetParent(torch.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0.45f, 0.45f);
        flame.transform.localScale = new Vector3(0.28f, 0.45f, 0.28f);
        var flameRenderer = flame.GetComponent<MeshRenderer>();
        if (kit.torchFlameMaterial != null) flameRenderer.sharedMaterial = kit.torchFlameMaterial;
        flameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(torch.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0.9f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.58f, 0.25f);
        l.intensity = 6f;
        l.range = 14f;
        l.shadows = LightShadows.None;

        torch.AddComponent<TorchFlicker>();
        torch.AddComponent<KnockableTorch>(); // 被攻擊掃到會掉到地上熄滅
    }

    /// <summary>量一下城垛零件，找出零件的正面是 +Z 還是 -Z（城垛凸出在零件的「背面」）</summary>
    void MeasureKit()
    {
        frontSign = 1f;
        if (kit.battlement == null) return;
        bool has = false;
        float minZ = 0f, maxZ = 0f;
        Transform prefabRoot = kit.battlement.transform;
        foreach (var mf in kit.battlement.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            // 零件的子物件可能有自己的位移 / 縮放，換算回零件根部的座標
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(sx, sy, sz));
                        float z = prefabRoot.InverseTransformPoint(mf.transform.TransformPoint(corner)).z;
                        if (!has) { minZ = maxZ = z; has = true; }
                        else { minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z); }
                    }
        }
        if (has) frontSign = (minZ + maxZ) * 0.5f < 0f ? 1f : -1f;
    }

    // =====================================================================
    //  玩家 / 檢查點
    // =====================================================================

    void PlacePlayerAndCheckpoint(List<DungeonLayout.Room> layout, Vector3[] centers, DungeonRoom gateRoom)
    {
        // 起點：起點房間的正中央，面向下一間房間
        int startIndex = 0;
        for (int i = 0; i < layout.Count; i++) if (layout[i].isStart) startIndex = i;
        Vector3 startLocal = centers[startIndex];
        Vector3 faceLocal = Vector3.forward;
        if (layout[startIndex].links.Count > 0)
            faceLocal = (centers[layout[startIndex].links[0]] - startLocal).normalized;

        var startPoint = new GameObject("StartPoint").transform;
        startPoint.SetParent(root, false);
        startPoint.localPosition = startLocal + Vector3.up;
        startPoint.localRotation = Quaternion.LookRotation(faceLocal);

        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            var health = player.GetComponent<PlayerHealth>();
            if (health != null) health.respawnPoint = startPoint; // 死掉從起點重生（碰到檢查點之後會改成檢查點）

            if (movePlayerToStart)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // CharacterController 要先關掉才能直接傳送
                player.transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
                if (cc != null) cc.enabled = true;
            }
        }
        else Debug.LogWarning("[Dungeon] 找不到 Tag 是 Player 的物件，沒辦法把玩家放到起點。", this);

        // 檢查點：放在 Boss 門前的小房間（原本就在裡面的話不動它）
        // 觸發範圍放大到涵蓋整間小房間，從哪一邊走過去都一定會碰到
        if (checkpoint != null) checkpoint.activateRadius = Mathf.Max(checkpoint.activateRadius, GateRoomHalf);
        if (checkpoint != null && !gateRoom.Contains(checkpoint.transform.position, 1f))
        {
            Vector3 respawnOffset = checkpoint.respawnPoint != null
                ? checkpoint.respawnPoint.position - checkpoint.transform.position : Vector3.zero;
            checkpoint.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(-3.5f, 0f, GateRoomCenterZ)), root.rotation);
            if (checkpoint.respawnPoint != null && !gateRoom.Contains(checkpoint.transform.position + respawnOffset, 1f))
                checkpoint.respawnPoint.SetPositionAndRotation(root.TransformPoint(new Vector3(0f, 1f, GateRoomCenterZ)), root.rotation);
        }
    }
}
