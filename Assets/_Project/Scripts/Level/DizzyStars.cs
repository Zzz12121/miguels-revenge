using UnityEngine;

/// <summary>暈眩時頭上繞圈的幾顆小星星（不是文字）</summary>
public class DizzyStars : MonoBehaviour
{
    Transform target;
    float until, height = 2f;
    Transform[] stars;
    static Material starMaterial;

    public static void Show(Transform target, float seconds)
    {
        var existing = target.GetComponentInChildren<DizzyStars>();
        if (existing != null) { existing.until = Mathf.Max(existing.until, Time.time + seconds); return; }
        var go = new GameObject("DizzyStars");
        go.transform.SetParent(target, false);
        var s = go.AddComponent<DizzyStars>();
        s.target = target;
        s.until = Time.time + seconds;
        var cc = target.GetComponent<CharacterController>();
        if (cc != null) s.height = (cc.center.y + cc.height * 0.5f) * target.lossyScale.y + 0.25f;
        s.Build();
    }

    void Build()
    {
        if (starMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            starMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { color = new Color(1f, 0.88f, 0.35f) };
            starMaterial.SetColor("_BaseColor", new Color(1f, 0.88f, 0.35f));
        }
        stars = new Transform[4];
        for (int i = 0; i < stars.Length; i++)
        {
            var star = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(star.GetComponent<Collider>());
            star.name = "Star";
            star.transform.SetParent(transform, false);
            star.transform.localScale = Vector3.one * 0.11f;
            var r = star.GetComponent<Renderer>();
            r.sharedMaterial = starMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stars[i] = star.transform;
        }
    }

    void Update()
    {
        if (target == null || Time.time >= until) { Destroy(gameObject); return; }
        transform.position = target.position + Vector3.up * height;
        transform.rotation = Quaternion.identity;
        for (int i = 0; i < stars.Length; i++)
        {
            float a = Time.time * 4f + i * Mathf.PI * 0.5f;
            stars[i].localPosition = new Vector3(Mathf.Cos(a) * 0.4f, Mathf.Sin(a * 2f) * 0.05f, Mathf.Sin(a) * 0.4f);
            stars[i].localRotation = Quaternion.Euler(45f, Time.time * 360f, 45f);
        }
    }
}
