using UnityEngine;

/// <summary>
/// 按鍵喝背包裡的藥水（PlayerItems：治癒藥水 Q、精力藥水 G）時的動作：
///   右手先把武器收起來（立刻收），很快地從右腰的藥袋拿出一個小藥瓶、舉到嘴邊（0.3 秒）→ 仰頭咕嚕咕嚕喝 1.2 秒 → 放回腰間，再把武器拿出來（共 1.8 秒）。
/// 喝的時候可以走動（PlayerItems 放慢移動速度、鎖住攻擊 / 技能 / 翻滾 / 跳），所以手的位置每幀都照身體現在的位置算，
/// 只蓋過右手臂和頭（腳照走路動畫）。藥瓶是程式做的（圓肚子、細瓶頸、軟木塞，治癒是綠色、精力是米白色）。
/// 手臂、頭在 LateUpdate 擺（動畫、PlayerLoadout 之後）。PlayerItems 開始喝時 Begin，喝完 / 中斷時 Stop。
/// </summary>
[DefaultExecutionOrder(32600)]
[DisallowMultipleComponent]
public class PotionSip : MonoBehaviour
{
    // 時間點（秒，以 Base 秒為準，照實際喝的時間等比例縮放）：伸手拿（快）→ 舉起（快）→ 在嘴前喝 1.2 秒 → 放下 → 收好
    public const float Base = 1.8f;
    const float GrabEnd = 0.14f, RaiseEnd = 0.3f, DrinkEnd = 1.5f, LowerEnd = 1.67f;
    const float BottleHeight = 0.12f;

    Animator anim;
    Transform rArm, rFore, rHand, rMiddle, head, hips;
    GameObject bottle;
    Material liquid;
    float t, duration;
    bool active, gulp0, gulp1, gulp2, ahh;

    public bool Active => active;

    /// <summary>開始喝（seconds = 整個動作多久）；color = 藥水的顏色</summary>
    public void Begin(float seconds, Color color)
    {
        if (!Resolve()) return;
        Stop();
        duration = Mathf.Max(0.5f, seconds);
        t = 0f;
        gulp0 = gulp1 = gulp2 = ahh = false;
        active = true;
        PlayerLoadout.Holster(this, true); // 右手要拿藥瓶：武器立刻收起來
        if (bottle == null) bottle = BuildBottle(out liquid);
        if (liquid != null && liquid.HasProperty("_BaseColor")) liquid.SetColor("_BaseColor", new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, 1f));
        bottle.SetActive(false);
    }

    /// <summary>喝完或中斷（死掉、換場景）：藏起藥瓶，武器拿回來</summary>
    public void Stop()
    {
        if (!active) return;
        active = false;
        if (bottle != null) bottle.SetActive(false);
        PlayerLoadout.Unholster(this);
    }

    void OnDisable() => Stop();

    void OnDestroy()
    {
        Stop();
        if (bottle != null) Destroy(bottle);
    }

    bool Resolve()
    {
        if (anim != null && rHand != null) return true;
        // 玩家身上不只一個 Animator：跟 PlayerLoadout 一樣用 PlayerWeaponPresenter 那個（看得到的模型）
        var presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        anim = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : null;
        if (anim == null || !anim.isHuman)
            foreach (var a in GetComponentsInChildren<Animator>())
                if (a.isHuman && a.isActiveAndEnabled) { anim = a; break; }
        if (anim == null || !anim.isHuman) return false;
        rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        rMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        return rArm != null && rFore != null && rHand != null && head != null && hips != null;
    }

    void LateUpdate()
    {
        if (!active || rHand == null) return;
        t += Time.deltaTime;
        float s = t * Base / duration; // 換算成 1.5 秒的時間軸
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        // 目標都跟著身體現在的位置（邊走邊喝）
        Vector3 pouch = hips.position + side * 0.2f + fwd * 0.04f - up * 0.02f;
        Vector3 mouth = head.position + fwd * 0.1f + up * 0.02f;
        Vector3 atMouth = mouth + (fwd * 0.8f + up * 0.55f).normalized * (BottleHeight * 0.55f);

        float w, raise = 0f; // raise：0 = 瓶子直立，1 = 瓶口對著嘴，>1 = 瓶底再抬高
        Vector3 target;
        if (s < GrabEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, s / GrabEnd);
            w = k;
            target = pouch;
        }
        else if (s < RaiseEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, (s - GrabEnd) / (RaiseEnd - GrabEnd));
            w = 1f;
            target = Vector3.Lerp(pouch, atMouth, k);
            raise = k;
        }
        else if (s < DrinkEnd)
        {
            float k = (s - RaiseEnd) / (DrinkEnd - RaiseEnd);
            w = 1f;
            target = atMouth + up * 0.02f * k;
            raise = 1f + 0.3f * k;
        }
        else if (s < LowerEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, (s - DrinkEnd) / (LowerEnd - DrinkEnd));
            w = 1f;
            target = Vector3.Lerp(atMouth, pouch, k);
            raise = 1.3f * (1f - k);
        }
        else
        {
            float k = Mathf.Clamp01((s - LowerEnd) / (Base - LowerEnd));
            w = 1f - k;
            target = pouch;
            if (bottle != null && bottle.activeSelf) bottle.SetActive(false); // 放回藥袋
        }

        // 仰頭喝
        float headBack = s > GrabEnd && s < LowerEnd + 0.1f ? Mathf.Clamp01(raise) * 22f : 0f;
        if (headBack > 0.01f) head.rotation = Quaternion.AngleAxis(-headBack, side) * head.rotation;

        Vector3 pole = rArm.position - up * 0.5f + side * 0.45f - fwd * 0.1f;
        Vector3 palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f) - rHand.position;
        NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, w);
        if (w > 0.999f)
        {
            // 手掌相對手腕的位置跟著前臂轉了：用新的姿勢再修一次，手掌才會真的到目標
            palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f) - rHand.position;
            NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, 1f);
        }

        // 藥瓶：手伸到藥袋時拿出來，跟著手掌；瓶口從朝上轉成對著嘴
        if (bottle != null && s >= GrabEnd * 0.85f && s < LowerEnd)
        {
            if (!bottle.activeSelf) bottle.SetActive(true);
            Vector3 palm = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f);
            Vector3 toMouth = (mouth - palm).normalized;
            Vector3 dir = Vector3.Slerp(up, toMouth, Mathf.Clamp01(raise));
            if (raise > 1f) dir = Vector3.Slerp(toMouth, -up, (raise - 1f) * 0.6f).normalized;
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, dir).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(fwd, dir) : side, dir);
            // 支點在瓶底中心：讓瓶身中間在手掌裡
            bottle.transform.SetPositionAndRotation(palm - dir * (BottleHeight * 0.45f), rot);
        }

        // 咕嚕咕嚕
        if (!gulp0 && s > RaiseEnd + 0.08f) { gulp0 = true; Gulp(mouth); }
        if (!gulp1 && s > RaiseEnd + 0.5f) { gulp1 = true; Gulp(mouth); }
        if (!gulp2 && s > RaiseEnd + 0.9f) { gulp2 = true; Gulp(mouth); }
        if (!ahh && s > DrinkEnd + 0.02f) { ahh = true; GameAudio.Play("SFX/Gen/drink_ahh", mouth, 0.35f, 1.05f, 0.05f, 1f, 15f); }

        if (s >= Base) Stop();
    }

    static void Gulp(Vector3 at) =>
        GameAudio.Play("SFX/Gen/drink_gulp_" + Random.Range(1, 4), at + Vector3.down * 0.12f, 0.55f, Random.Range(0.95f, 1.08f), 0f, 1f, 15f);

    // ---------------- 藥瓶（程式做的） ----------------

    /// <summary>小藥瓶：玻璃瓶身（圓肚子、細瓶頸，半透明）、裡面的藥水、軟木塞；支點在瓶底中心，沿 +Y 長，高 BottleHeight</summary>
    static GameObject BuildBottle(out Material liquidMat)
    {
        var root = new GameObject("PotionSip_Bottle");
        // 輪廓（高度, 半徑），從瓶底到瓶口
        var glassProfile = new[]
        {
            new Vector2(0f, 0f), new Vector2(0f, 0.028f), new Vector2(0.012f, 0.036f), new Vector2(0.035f, 0.04f),
            new Vector2(0.06f, 0.036f), new Vector2(0.075f, 0.022f), new Vector2(0.083f, 0.011f), new Vector2(0.105f, 0.01f),
            new Vector2(0.108f, 0.013f), new Vector2(0.112f, 0.012f),
        };
        var liquidProfile = new[]
        {
            new Vector2(0.004f, 0f), new Vector2(0.004f, 0.025f), new Vector2(0.014f, 0.033f), new Vector2(0.035f, 0.037f),
            new Vector2(0.055f, 0.034f), new Vector2(0.062f, 0f),
        };
        var corkProfile = new[]
        {
            new Vector2(0.1f, 0f), new Vector2(0.1f, 0.0095f), new Vector2(0.125f, 0.011f), new Vector2(0.128f, 0f),
        };
        var glass = Lit(new Color(0.78f, 0.88f, 0.9f, 0.35f), 0.92f, true);
        liquidMat = Lit(new Color(0.4f, 0.85f, 0.45f, 1f), 0.7f, false);
        var cork = Lit(new Color(0.55f, 0.4f, 0.24f, 1f), 0.15f, false);
        Part(root.transform, "Liquid", Lathe(liquidProfile), liquidMat);
        Part(root.transform, "Glass", Lathe(glassProfile), glass);
        Part(root.transform, "Cork", Lathe(corkProfile), cork);
        // 程式做的尺寸是 0.128 m 高；照 BottleHeight 縮放
        root.transform.localScale = Vector3.one * (BottleHeight / 0.128f);
        var holder = new GameObject("PotionSip");
        root.transform.SetParent(holder.transform, false);
        return holder;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Mesh Lathe(Vector2[] profile)
    {
        const int Sides = 16;
        var verts = new Vector3[profile.Length * Sides];
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i < profile.Length; i++)
            for (int k = 0; k < Sides; k++)
            {
                float a = k * Mathf.PI * 2f / Sides;
                verts[i * Sides + k] = new Vector3(Mathf.Cos(a) * profile[i].y, profile[i].x, Mathf.Sin(a) * profile[i].y);
            }
        for (int i = 0; i < profile.Length - 1; i++)
            for (int k = 0; k < Sides; k++)
            {
                int a = i * Sides + k, b = i * Sides + (k + 1) % Sides, c = a + Sides, d = b + Sides;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        var m = new Mesh { name = "Runtime_PotionSip" };
        m.vertices = verts;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Material Lit(Color c, float smoothness, bool transparent)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Runtime_PotionSip" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (transparent && m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return m;
    }
}
