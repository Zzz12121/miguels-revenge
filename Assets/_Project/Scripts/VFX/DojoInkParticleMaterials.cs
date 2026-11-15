using System.Collections.Generic;
using UnityEngine;

/// <summary>Releases only cloned runtime pigment materials, including short-lived accents.</summary>
public sealed class DojoInkParticleMaterials : MonoBehaviour
{
    public readonly List<Material> materials=new();
    void OnDestroy(){foreach(var m in materials)if(m)Destroy(m);}
}
