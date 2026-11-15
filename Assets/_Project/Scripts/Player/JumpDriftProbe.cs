using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 測試工具：自動原地跳幾次，每一幀記錄「模型（髖部、兩腳）相對角色碰撞體」偏了多少公分，
/// 並輪流關掉 PlayerVisualAnimator 的幾層程式（空中姿勢、腳踩地、台階平滑），看位移是哪一層造成的。
/// 結果寫到專案的 Logs/jump_probe.csv（逐幀）和 Logs/jump_probe_summary.txt（每一跳的摘要）。
/// 用法：Play 中執行選單 GeminiGrowUp > 測試 > 記錄跳躍時模型的位移，放開鍵盤滑鼠等它跳完（約 20 秒）。
/// </summary>
[DefaultExecutionOrder(1000)] // 在動畫、PlayerVisualAnimator、彈簧骨都算完之後才量
public sealed class JumpDriftProbe : MonoBehaviour
{
    static readonly string[] Phases = { "全部開著", "關掉空中姿勢(JumpDynamics)", "再關掉腳踩地(GroundFeet)", "再關掉台階平滑(StepSmooth)" };
    const int JumpsPerPhase = 2;
    static readonly string[] States = { "Idle", "wait", "look_around", "Walk", "Run", "Jump", "Fall", "Land", "Roll", "Slash1", "Slash2", "Slash3" };

    ThirdPersonController controller;
    Animator animator;
    Transform hips, leftFoot, rightFoot;
    readonly StringBuilder csv = new StringBuilder();
    readonly List<string> summary = new List<string>();
    int phase = -1, jumpIndex;
    string status = "準備中…";
    bool recording;
    float t0;
    bool savedAi, savedInput;

    void Start()
    {
        controller = GetComponent<ThirdPersonController>();
        animator = GetComponentInChildren<Animator>();
        if (controller == null || animator == null || !animator.isHuman) { Debug.LogWarning("[JumpDriftProbe] 找不到玩家的 ThirdPersonController / 人形 Animator"); Destroy(this); return; }
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        csv.AppendLine("time,phase,jump,grounded,vy,state,inTransition,next,normTime,hipsX_cm,hipsZ_cm,hipsY_cm,feetX_cm,feetZ_cm,LfootX_cm,LfootZ_cm,RfootX_cm,RfootZ_cm,capsuleX,capsuleZ");
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        savedAi = controller.aiControlled; savedInput = controller.inputEnabled;
        controller.aiControlled = true; controller.inputEnabled = true;
        controller.SetAIInput(Vector3.zero);
        t0 = Time.time;
        recording = true;
        for (phase = 0; phase < Phases.Length; phase++)
        {
            PlayerVisualAnimator.DebugSkipJumpDynamics = phase >= 1;
            PlayerVisualAnimator.DebugSkipGroundFeet = phase >= 2;
            PlayerVisualAnimator.DebugSkipStepSmooth = phase >= 3;
            for (jumpIndex = 0; jumpIndex < JumpsPerPhase; jumpIndex++)
            {
                status = $"第 {phase + 1}/{Phases.Length} 組：{Phases[phase]}　第 {jumpIndex + 1} 跳（請不要按鍵盤滑鼠）";
                // 站穩 1.5 秒，量跳之前的位置
                yield return new WaitForSeconds(1.5f);
                var before = Snapshot();
                controller.SetAIInput(Vector3.zero, false, true);
                float jumpAt = Time.time;
                // 等離地，再等落地
                while (controller.IsGrounded && Time.time - jumpAt < 1f) yield return null;
                while (!controller.IsGrounded && Time.time - jumpAt < 4f) yield return null;
                float landAt = Time.time;
                // 落地後 0～0.8 秒內偏最多多少、1.5 秒後停在哪裡
                Vector3 maxDev = Vector3.zero;
                while (Time.time - landAt < 1.5f)
                {
                    var now = Snapshot();
                    var d = now - before;
                    if (Time.time - landAt < .8f && new Vector2(d.x, d.z).magnitude > new Vector2(maxDev.x, maxDev.z).magnitude) maxDev = d;
                    yield return null;
                }
                var after = Snapshot();
                var settle = after - before;
                summary.Add($"[{Phases[phase]}] 第{jumpIndex + 1}跳：落地後0.8秒內髖部最多偏 左右{maxDev.x:+0.0;-0.0}cm 前後{maxDev.z:+0.0;-0.0}cm；1.5秒後停在 左右{settle.x:+0.0;-0.0}cm 前後{settle.z:+0.0;-0.0}cm（正=往右/往前）");
            }
        }
        recording = false;
        PlayerVisualAnimator.DebugSkipJumpDynamics = PlayerVisualAnimator.DebugSkipGroundFeet = PlayerVisualAnimator.DebugSkipStepSmooth = false;
        controller.aiControlled = savedAi; controller.inputEnabled = savedInput;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "jump_probe.csv"), csv.ToString(), Encoding.UTF8);
        File.WriteAllLines(Path.Combine(dir, "jump_probe_summary.txt"), summary, Encoding.UTF8);
        status = "完成！結果在 Logs/jump_probe.csv、jump_probe_summary.txt（跟 Claude 說一聲就好）";
        Debug.Log("[JumpDriftProbe] 完成：\n" + string.Join("\n", summary));
        yield return new WaitForSeconds(6f);
        Destroy(this);
    }

    /// <summary>髖部相對角色碰撞體的位置（公分，x = 右、z = 前、y = 上）</summary>
    Vector3 Snapshot() => hips != null ? Local(hips.position) : Vector3.zero;
    Vector3 Local(Vector3 world) => controller.transform.InverseTransformPoint(world) * 100f;

    void LateUpdate()
    {
        if (!recording || hips == null) return;
        var h = Local(hips.position);
        var lf = leftFoot != null ? Local(leftFoot.position) : Vector3.zero;
        var rf = rightFoot != null ? Local(rightFoot.position) : Vector3.zero;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        string state = Name(info), next = animator.IsInTransition(0) ? Name(animator.GetNextAnimatorStateInfo(0)) : "";
        var p = controller.transform.position;
        csv.AppendLine(string.Join(",", (Time.time - t0).ToString("0.000"), phase, jumpIndex, controller.IsGrounded ? 1 : 0, controller.VerticalVelocity.ToString("0.00"),
            state, animator.IsInTransition(0) ? 1 : 0, next, info.normalizedTime.ToString("0.000"),
            h.x.ToString("0.00"), h.z.ToString("0.00"), h.y.ToString("0.00"),
            ((lf.x + rf.x) * .5f).ToString("0.00"), ((lf.z + rf.z) * .5f).ToString("0.00"),
            lf.x.ToString("0.00"), lf.z.ToString("0.00"), rf.x.ToString("0.00"), rf.z.ToString("0.00"),
            p.x.ToString("0.000"), p.z.ToString("0.000")));
    }

    static string Name(AnimatorStateInfo info)
    {
        foreach (var s in States) if (info.IsName(s)) return s;
        return info.shortNameHash.ToString();
    }

    void OnGUI()
    {
        GUI.Label(new Rect(20, Screen.height - 60, Screen.width - 40, 40), "<size=18><color=yellow>[跳躍位移測試] " + status + "</color></size>");
    }

    void OnDestroy()
    {
        PlayerVisualAnimator.DebugSkipJumpDynamics = PlayerVisualAnimator.DebugSkipGroundFeet = PlayerVisualAnimator.DebugSkipStepSmooth = false;
        if (controller != null && recording) { controller.aiControlled = savedAi; controller.inputEnabled = savedInput; }
    }
}
