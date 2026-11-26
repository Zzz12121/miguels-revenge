using UnityEngine;

[ExecuteAlways, RequireComponent(typeof(TextMesh))]
public sealed class InkboundSign : MonoBehaviour
{
    [TextArea] public string inscription = "茶";
    public int size = 64;
    public float characterSize = .11f;
    public Material letteringMaterial;
    static Font font;
    static Material lettering;
    void OnEnable() => Apply();
    void OnValidate() => Apply();
    public void Apply()
    {
        if (!font)
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "KaiTi", "DFKai-SB", "Microsoft JhengHei", "Noto Sans CJK TC", "Arial" }, 64);
            Font.textureRebuilt+=UpdateAtlas;
        }
        var text=GetComponent<TextMesh>();text.text=inscription;text.font=font;text.fontSize=size;
        text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.characterSize=characterSize;
        text.color=new Color(.065f,.07f,.075f);
        if(!lettering && letteringMaterial)lettering=new Material(letteringMaterial){hideFlags=HideFlags.HideAndDontSave};
        var renderer=GetComponent<MeshRenderer>();
        if(renderer && font){UpdateAtlas(font);renderer.sharedMaterial=lettering?lettering:font.material;}
    }
    static void UpdateAtlas(Font rebuilt){if(rebuilt==font && lettering && font.material)lettering.mainTexture=font.material.mainTexture;}
}
