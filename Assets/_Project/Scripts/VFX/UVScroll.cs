using UnityEngine;

/// <summary>讓材質貼圖持續捲動（瀑布水流、溪水）。用 MaterialPropertyBlock，不會複製材質。</summary>
[RequireComponent(typeof(Renderer))]
public class UVScroll : MonoBehaviour
{
    public string textureProperty = "_BaseMap";
    public Vector2 speed = new Vector2(0f, -1.2f);
    public Vector2 tiling = Vector2.one;

    Renderer rend;
    MaterialPropertyBlock mpb;
    int stId;
    Vector2 offset;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        stId = Shader.PropertyToID(textureProperty + "_ST");
    }

    void Update()
    {
        offset += speed * Time.deltaTime;
        offset.x -= Mathf.Floor(offset.x);
        offset.y -= Mathf.Floor(offset.y);
        rend.GetPropertyBlock(mpb);
        mpb.SetVector(stId, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
        rend.SetPropertyBlock(mpb);
    }
}
