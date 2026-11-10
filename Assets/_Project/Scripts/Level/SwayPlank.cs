using UnityEngine;

/// <summary>
/// 吊在懸空木條下面的木板：可以站上去喘口氣，但只能站一下。
/// 站上去之後木板會往下沉、左右晃、嘎吱作響；站滿 holdTime 秒就翻過去（碰撞關掉），玩家掉進深溝。
/// 翻過去 resetTime 秒之後自己翻回來。board 是會動的板子（有碰撞），繩子不跟著動。
/// </summary>
public class SwayPlank : MonoBehaviour
{
    [Tooltip("會沉、會晃、會翻的板子（BoxCollider 在這上面）")]
    public Transform board;
    [Tooltip("站多久就翻過去 (秒)")]
    public float holdTime = 1.6f;
    [Tooltip("翻過去之後多久翻回來 (秒)")]
    public float resetTime = 3f;
    [Tooltip("站上去時最多往下沉多少 (m)")]
    public float sinkDepth = 0.22f;
    [Tooltip("木板的半長、半寬（board 的 local x、z）")]
    public Vector2 halfSize = new Vector2(1.3f, 0.45f);

    public bool Flipped { get; private set; }
    /// <summary>現在有人站在上面</summary>
    public bool Occupied { get; private set; }

    PlayerHealth player;
    CharacterController body;
    Collider boardCollider;
    Vector3 restPos;
    Quaternion restRot;
    float load, flipT, flippedAt, nextCreak;

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) body = player.GetComponent<CharacterController>();
        boardCollider = board.GetComponent<Collider>();
        restPos = board.localPosition;
        restRot = board.localRotation;
    }

    void Update()
    {
        Occupied = !Flipped && player != null && IsStanding();
        if (Flipped)
        {
            flipT = Mathf.MoveTowards(flipT, 1f, Time.deltaTime * 5f);
            if (Time.time - flippedAt > resetTime)
            {
                Flipped = false;
                boardCollider.enabled = true;
            }
        }
        else
        {
            flipT = Mathf.MoveTowards(flipT, 0f, Time.deltaTime * 2f);
            load = Occupied ? load + Time.deltaTime : Mathf.Max(0f, load - Time.deltaTime * 0.8f);
            if (Occupied && Time.time > nextCreak)
            {
                nextCreak = Time.time + Mathf.Lerp(0.55f, 0.25f, load / holdTime);
                GameAudio.Play(Random.value < 0.5f ? "SFX/RPG/creak1" : "SFX/RPG/creak2", board.position, 0.6f, Random.Range(0.9f, 1.2f), 0.05f, 1f, 25f);
            }
            if (load >= holdTime) Flip();
        }

        // 姿勢：站越久沉越多、晃越大；翻的時候繞長邊（local x）轉 80 度
        float k = Mathf.Clamp01(load / holdTime);
        float wobble = Mathf.Sin(Time.time * (9f + 8f * k)) * (1.5f + 5f * k) * (Occupied ? 1f : 0.2f);
        board.localPosition = restPos + Vector3.down * (sinkDepth * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 1.6f)));
        board.localRotation = restRot * Quaternion.Euler(wobble + 80f * flipT * flipT, 0f, wobble * 0.3f);
    }

    bool IsStanding()
    {
        Vector3 local = board.InverseTransformPoint(player.transform.position);
        Vector3 scale = board.lossyScale;
        // 板子是 local x 長、z 寬；腳底在板子上面 0 ~ 0.6 m 以內
        if (Mathf.Abs(local.x * scale.x) > halfSize.x + 0.25f || Mathf.Abs(local.z * scale.z) > halfSize.y + 0.3f) return false;
        // 玩家的位置點在身體中間（CharacterController 預設），要用碰撞體的底部當腳底
        float feet = body != null ? body.bounds.min.y : player.transform.position.y;
        float above = feet - board.position.y;
        return above > -0.15f && above < 0.7f && (body == null || body.isGrounded);
    }

    void Flip()
    {
        Flipped = true;
        flippedAt = Time.time;
        load = 0f;
        boardCollider.enabled = false;
        GameAudio.Play("SFX/RPG/creak3", board.position, 1f, 0.8f, 0.05f, 1f, 30f);
    }
}
