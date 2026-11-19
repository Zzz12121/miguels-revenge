using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家喝藥（藥師遞過來的藥瓶，ApothecaryPotionGift 掛上來、喝完拿掉）：
///   伸右手接過藥瓶 → 舉到嘴邊、仰頭（瓶底朝上）咕嚕咕嚕喝三口 → 放下、收起藥瓶，Done = true。
/// 武器由 ApothecaryPotionGift 先收起來（PlayerLoadout.Holster），喝完再拔出來。手臂、頭在 LateUpdate 擺（動畫和 PlayerLoadout 之後）。
/// </summary>
[DefaultExecutionOrder(32600)]
public class PotionDrink : MonoBehaviour
{
    public Transform bottle;
    [Tooltip("藥瓶直立時的方向（瓶口朝上）")]
    public Quaternion upright = Quaternion.identity;
    [Tooltip("藥瓶多高（支點在瓶底中心）")]
    public float bottleHeight = 0.24f;
    public bool Done { get; private set; }

    const float TakeEnd = 0.45f, RaiseEnd = 1.0f, DrinkEnd = 2.1f, LowerEnd = 2.55f, Total = 2.85f;

    Animator anim;
    Transform rArm, rFore, rHand, rMiddle, head;
    float t;
    Vector3 bottleStart, centerLocal;
    bool grabbed, gulped0, gulped1, gulped2, ahh;

    void Start()
    {
        // 玩家身上不只一個 Animator：跟 PlayerLoadout 一樣用 PlayerWeaponPresenter 那個（看得到的模型）
        var presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        anim = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : null;
        if (anim == null || !anim.isHuman)
            foreach (var a in GetComponentsInChildren<Animator>())
                if (a.isHuman && a.isActiveAndEnabled) { anim = a; break; }
        if (anim == null || !anim.isHuman || bottle == null) { Done = true; return; }
        rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        rMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        // 瓶身中心：瓶底中心（支點）往上半個瓶高（網格的外框不準，不用 Renderer.bounds）
        centerLocal = Vector3.up * (bottleHeight * 0.5f / Mathf.Max(0.0001f, bottle.lossyScale.y));
        bottleStart = bottle.TransformPoint(centerLocal);
    }

    void LateUpdate()
    {
        if (Done || rArm == null || rFore == null || rHand == null || head == null) return;
        t += Time.deltaTime;
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        Vector3 mouth = head.position + fwd * 0.1f + up * 0.02f;
        // 喝的時候藥瓶的中心：嘴的前上方、離嘴半個瓶身，瓶口剛好貼著嘴（瓶子越大拿得越遠，不會插進臉裡）
        Vector3 atMouth = mouth + (fwd * 0.8f + up * 0.6f).normalized * (bottleHeight * 0.48f);
        Vector3 chestFront = head.position - up * 0.38f + fwd * 0.32f + side * 0.08f;

        float w;
        Vector3 target;
        float raise = 0f; // 0 = 直立拿著，1 = 瓶口對著嘴
        if (t < TakeEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / TakeEnd);
            w = k;
            target = bottleStart;
        }
        else if (t < RaiseEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - TakeEnd) / (RaiseEnd - TakeEnd));
            w = 1f;
            target = Vector3.Lerp(bottleStart, atMouth, k);
            raise = k;
        }
        else if (t < DrinkEnd)
        {
            w = 1f;
            float k = (t - RaiseEnd) / (DrinkEnd - RaiseEnd);
            target = atMouth + up * 0.025f * k; // 越喝瓶底越往上
            raise = 1f + 0.25f * k;
        }
        else if (t < LowerEnd)
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - DrinkEnd) / (LowerEnd - DrinkEnd));
            w = 1f;
            target = Vector3.Lerp(atMouth, chestFront, k);
            raise = 1.25f * (1f - k);
        }
        else
        {
            float k = Mathf.Clamp01((t - LowerEnd) / (Total - LowerEnd));
            w = 1f - k;
            target = chestFront;
            if (bottle.gameObject.activeSelf) bottle.gameObject.SetActive(false); // 喝完收起來
        }

        // 仰頭喝
        float headBack = t > TakeEnd && t < LowerEnd + 0.2f ? Mathf.Clamp01(raise) * 24f : 0f;
        if (headBack > 0.01f) head.rotation = Quaternion.AngleAxis(-headBack, side) * head.rotation;

        Vector3 pole = rArm.position - up * 0.5f + side * 0.45f - fwd * 0.1f;
        Vector3 palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f) - rHand.position;
        NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, w);
        // 手掌相對手腕的位置跟著前臂轉了（手放下 → 舉到嘴邊會整個翻過來），用新的姿勢再修一次，手掌才會真的到目標
        if (w > 0.999f)
        {
            palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f) - rHand.position;
            NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, 1f);
        }

        // 藥瓶：接到以後跟著手掌，瓶口從朝上轉成對著嘴
        if (t >= TakeEnd * 0.9f) grabbed = true;
        if (grabbed && bottle.gameObject.activeSelf)
        {
            Vector3 palm = NpcRoutine.Palm(rHand, rMiddle, rFore, 1f);
            Vector3 toMouth = (mouth - palm).normalized;
            Vector3 dir = Vector3.Slerp(up, toMouth, Mathf.Clamp01(raise));
            if (raise > 1f) dir = Vector3.Slerp(toMouth, -up, (raise - 1f) * 0.6f).normalized; // 瓶底再抬高一點
            Quaternion rot = Quaternion.FromToRotation(upright * Vector3.up, dir) * upright;
            bottle.rotation = rot;
            bottle.position = palm - (bottle.TransformPoint(centerLocal) - bottle.position);
        }

        // 咕嚕咕嚕
        if (!gulped0 && t > RaiseEnd + 0.05f) { gulped0 = true; Gulp(mouth); }
        if (!gulped1 && t > RaiseEnd + 0.42f) { gulped1 = true; Gulp(mouth); }
        if (!gulped2 && t > RaiseEnd + 0.8f) { gulped2 = true; Gulp(mouth); }
        if (!ahh && t > LowerEnd - 0.2f) { ahh = true; GameAudio.Play("SFX/Gen/drink_ahh", mouth, 0.45f, 0.95f, 0.05f, 1f, 15f); }

        if (t >= Total) Done = true;
    }

    static void Gulp(Vector3 at) =>
        GameAudio.Play("SFX/Gen/drink_gulp_" + Random.Range(1, 4), at + Vector3.down * 0.12f, 0.6f, Random.Range(0.9f, 1f), 0f, 1f, 15f);
}
