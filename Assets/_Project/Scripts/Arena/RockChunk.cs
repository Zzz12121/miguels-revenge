using UnityEngine;

/// <summary>
/// 岩石碎掉時噴出來的小碎石：拋物線飛出去、邊飛邊轉、最後縮小消失。
/// 純粹是視覺效果，沒有碰撞。由 ArenaRock 自動產生，不用手動放。
/// </summary>
public class RockChunk : MonoBehaviour
{
    Vector3 velocity;
    Vector3 spinAxis;
    float spinSpeed;
    float life, age;
    Vector3 startScale;

    static Mesh sharedMesh;

    public static void Spawn(Vector3 position, float size, Vector3 velocity, Material material, Transform parent)
    {
        if (sharedMesh == null)
        {
            sharedMesh = ArenaMeshes.BuildRock(4242, 1f, 1f, false, 1);
            sharedMesh.hideFlags = HideFlags.DontSave; // 所有碎石共用這一個模型，一直留著
        }

        var go = new GameObject("RockChunk");
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.transform.rotation = Random.rotation;
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        if (material != null) renderer.sharedMaterial = material;

        var chunk = go.AddComponent<RockChunk>();
        chunk.velocity = velocity;
        chunk.spinAxis = Random.onUnitSphere;
        chunk.spinSpeed = Random.Range(180f, 540f);
        chunk.life = Random.Range(0.9f, 1.5f);
        chunk.startScale = go.transform.localScale;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (age >= life) { Destroy(gameObject); return; }

        velocity += Vector3.down * 25f * dt;
        transform.position += velocity * dt;
        transform.Rotate(spinAxis, spinSpeed * dt, Space.World);

        // 最後 40% 的時間慢慢縮小
        float k = Mathf.InverseLerp(life, life * 0.6f, age);
        transform.localScale = startScale * Mathf.Clamp01(k);
    }
}
