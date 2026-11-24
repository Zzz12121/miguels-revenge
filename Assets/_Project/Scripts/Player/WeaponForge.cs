using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鐵匠鋪的武器升級（用打跑雙尾虎掉的虎皮、虎牙）：
///   - 升級過 = 背包裡 WeaponForged = 1（PlayerInventory，所以跟著換關卡、存檔）
///   - 傷害：大刀、小刀、長槍、弓箭都多 DamageBonus（PlayerLoadout.Apply 乘上 DamageScale）
///   - 外觀：手上的武器微微發橘光（WeaponForgeGlow：武器表面多畫一層淡淡的橘色、外面一圈更淡的光暈，加一盞小小的橘色點光，慢慢呼吸）
/// </summary>
public static class WeaponForge
{
    public const string ForgedKey = "WeaponForged";
    public const float DamageBonus = 0.15f;
    /// <summary>升級要的材料</summary>
    public const int PeltCost = 1, FangCost = 2;

    public static bool Forged => VillageWallet.Get(ForgedKey) > 0;
    public static float DamageScale => Forged ? 1f + DamageBonus : 1f;
    public static bool HasMaterials => VillageWallet.Get(TigerLoot.PeltKey) >= PeltCost && VillageWallet.Get(TigerLoot.FangKey) >= FangCost;

    /// <summary>扣材料、升級，套到現在的武器上；材料不夠或已經升級過回傳 false</summary>
    public static bool Upgrade()
    {
        if (Forged || !HasMaterials) return false;
        var inv = VillageWallet.Inventory;
        if (inv == null) return false;
        inv.ConsumeResource(TigerLoot.PeltKey, PeltCost);
        inv.ConsumeResource(TigerLoot.FangKey, FangCost);
        VillageWallet.Set(ForgedKey, 1);
        PlayerLoadout.Refresh();
        return true;
    }
}

/// <summary>
/// 升級過的武器微微發橘光：掛在玩家身上（PlayerLoadout 自動加），每幀跟著「現在看得到的武器網格」
/// （大刀 / 小刀 / 長槍 = PlayerWeaponPresenter 正在用的網格，弓 = PlayerLoadout 的弓），
/// 用 Graphics.RenderMesh 在同一個位置多畫一次同樣形狀的加亮（Additive）橘色，網格能讀的話外面再一圈稍微脹大的淡光暈，
/// 加一盞小小的橘色點光，慢慢呼吸。不建立子物件、不改任何武器的材質（不會被 PlayerWeaponPresenter 當成武器網格）。
/// 武器看不到（收起來、坐下、聚合 / 散開的溶解效果換了材質）時光也跟著不見。
/// </summary>
[DefaultExecutionOrder(32650)] // 所有擺武器、手的腳本之後；BenchSeat（32350）藏起武器之後
[DisallowMultipleComponent]
public class WeaponForgeGlow : MonoBehaviour
{
    static readonly Color Glow = new Color(1f, 0.42f, 0.08f);

    sealed class Overlay
    {
        public Mesh mesh, halo;
    }

    readonly Dictionary<Renderer, Overlay> overlays = new Dictionary<Renderer, Overlay>();
    readonly List<Renderer> current = new List<Renderer>();
    readonly List<Renderer> stale = new List<Renderer>();
    static Material tintMat, haloMat;
    PlayerWeaponPresenter presenter;
    Light lamp;
    System.Reflection.FieldInfo renderersField;

    void Start()
    {
        presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        renderersField = typeof(PlayerWeaponPresenter).GetField("weaponRenderers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    }

    void LateUpdate()
    {
        current.Clear();
        if (WeaponForge.Forged)
        {
            if (presenter == null) presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
            if (presenter != null && renderersField != null && renderersField.GetValue(presenter) is Renderer[] rs)
                foreach (var r in rs) if (r != null) current.Add(r);
            var bow = PlayerLoadout.Instance != null ? PlayerLoadout.Instance.BowModel : null;
            if (bow != null) foreach (var r in bow.GetComponentsInChildren<MeshRenderer>(true)) current.Add(r);
        }

        stale.Clear();
        foreach (var kv in overlays) if (kv.Key == null || !current.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var r in stale) { if (overlays[r]?.halo != null) Destroy(overlays[r].halo); overlays.Remove(r); }

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f);
        if (tintMat == null) tintMat = MakeAdditive("Runtime_ForgeGlow_Tint");
        if (haloMat == null) haloMat = MakeAdditive("Runtime_ForgeGlow_Halo");
        if (tintMat != null) tintMat.SetColor("_BaseColor", new Color(Glow.r, Glow.g, Glow.b, 0.16f * pulse));
        if (haloMat != null) haloMat.SetColor("_BaseColor", new Color(Glow.r, Glow.g, Glow.b, 0.09f * pulse));

        Bounds lit = default;
        bool any = false;
        foreach (var r in current)
        {
            if (!(r is MeshRenderer)) continue; // 蒙皮網格（別關卡的弓）不疊
            if (!overlays.TryGetValue(r, out var o)) { o = Build(r); overlays[r] = o; }
            if (o == null || tintMat == null) continue;
            if (!(r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy) || Dissolving(r)) continue;
            var m = r.localToWorldMatrix;
            var tint = new RenderParams(tintMat) { layer = r.gameObject.layer, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            for (int sub = 0; sub < o.mesh.subMeshCount; sub++) Graphics.RenderMesh(tint, o.mesh, sub, m);
            if (o.halo != null && haloMat != null)
            {
                var halo = new RenderParams(haloMat) { layer = r.gameObject.layer, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                for (int sub = 0; sub < o.halo.subMeshCount; sub++) Graphics.RenderMesh(halo, o.halo, sub, m);
            }
            if (!any) { lit = r.bounds; any = true; } else lit.Encapsulate(r.bounds);
        }

        if (any)
        {
            if (lamp == null)
            {
                var go = new GameObject("ForgeGlow_Light");
                lamp = go.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = Glow;
                lamp.shadows = LightShadows.None;
            }
            lamp.transform.position = lit.center;
            lamp.range = Mathf.Clamp(lit.extents.magnitude * 2.2f, 0.8f, 2.2f);
            lamp.intensity = 0.55f * pulse;
            lamp.enabled = true;
        }
        else if (lamp != null) lamp.enabled = false;
    }

    void OnDestroy()
    {
        if (lamp != null) Destroy(lamp.gameObject);
        foreach (var o in overlays.Values) if (o?.halo != null) Destroy(o.halo);
    }

    static Overlay Build(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return null;
        float scale = Mathf.Max(1e-4f, r.transform.lossyScale.x);
        return new Overlay { mesh = mf.sharedMesh, halo = Inflate(mf.sharedMesh, 0.006f / scale) };
    }

    /// <summary>PlayerWeaponPresenter 正在做聚合 / 散開的溶解效果（材質換成 WeaponLightningDissolve）：這時不發光</summary>
    static bool Dissolving(Renderer r)
    {
        var m = r.sharedMaterial;
        return m != null && m.shader != null && m.shader.name.Contains("Dissolve");
    }

    /// <summary>網格沿法線往外脹一點（光暈）；網格不能讀（沒開 Read/Write）就不做</summary>
    static Mesh Inflate(Mesh src, float amount)
    {
        if (!src.isReadable) return null;
        var v = src.vertices;
        var n = src.normals;
        if (n == null || n.Length != v.Length) return null;
        for (int i = 0; i < v.Length; i++) v[i] += n[i] * amount;
        var m = new Mesh { name = src.name + "_ForgeHalo", indexFormat = src.indexFormat };
        m.vertices = v;
        m.normals = n;
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) m.SetTriangles(src.GetTriangles(s), s);
        m.RecalculateBounds();
        return m;
    }

    static Material MakeAdditive(string name)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) return null;
        var m = new Material(sh) { name = name };
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);   // Additive
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(Glow.r, Glow.g, Glow.b, 0.15f));
        return m;
    }
}
