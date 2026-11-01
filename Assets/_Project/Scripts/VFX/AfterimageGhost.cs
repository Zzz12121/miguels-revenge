using UnityEngine;

/// <summary>
/// 殘影：把角色現在的姿勢（SkinnedMeshRenderer）烘成一個靜止的網格，半透明、慢慢淡掉後刪除。
/// 空中側閃斬會一路留下殘影；之後的「幻影分身」也可以用同一招做分身的外觀。
/// 用法：AfterimageGhost.Spawn(renderers, 顏色(含透明度), 存活秒數)
/// </summary>
public sealed class AfterimageGhost : MonoBehaviour
{
    static Material sharedMaterial;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    Mesh mesh;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock block;
    Color color;
    float life, age;

    public static void Spawn(SkinnedMeshRenderer[] renderers, Color color, float life)
    {
        if (renderers == null) return;
        var material = SharedMaterial();
        if (material == null) return;
        foreach (var source in renderers)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || source.sharedMesh == null) continue;
            var mesh = new Mesh { name = "Afterimage" };
            source.BakeMesh(mesh, true); // 連縮放一起烘進去，物件本身不縮放
            var go = new GameObject("Afterimage");
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var ghost = go.AddComponent<AfterimageGhost>();
            ghost.mesh = mesh;
            ghost.meshRenderer = mr;
            ghost.color = color;
            ghost.life = Mathf.Max(.05f, life);
            ghost.block = new MaterialPropertyBlock();
            ghost.Apply(1f);
        }
    }

    static Material SharedMaterial()
    {
        if (sharedMaterial != null) return sharedMaterial;
        // Sprites/Default：不受光、半透明、不寫深度，網格也能用；找不到才退回 URP Unlit
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        sharedMaterial = new Material(shader) { name = "Afterimage (runtime)", renderQueue = 3000 };
        return sharedMaterial;
    }

    void Apply(float k)
    {
        var c = color;
        c.a *= k * k;
        block.SetColor(ColorId, c);
        block.SetColor(BaseColorId, c);
        meshRenderer.SetPropertyBlock(block);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        Apply(1f - age / life);
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
