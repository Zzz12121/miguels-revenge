using UnityEngine;

/// <summary>Reusable parallel filaments; follows a live source line without changing its hit path.</summary>
public sealed class LightningFilamentBundle : MonoBehaviour
{
    LineRenderer source;
    LineRenderer[] strands;
    float radius;
    Vector3[] sourcePoints = new Vector3[0];
    public void Initialize(LineRenderer line, int count, float spread)
    {
        source = line;
        radius = spread;
        strands = new LineRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("ParallelFilament");
            go.transform.SetParent(transform, false);
            var strand = strands[i] = go.AddComponent<LineRenderer>();
            strand.sharedMaterial = source.sharedMaterial;
            strand.useWorldSpace = true;
            strand.positionCount = 12;
            strand.widthCurve = new AnimationCurve(new Keyframe(0,0),new Keyframe(.18f,1),new Keyframe(.78f,.7f),new Keyframe(1,0));
        }
    }
    static float Noise(float a, float b) => Mathf.Clamp01(Mathf.PerlinNoise(a,b));
    void LateUpdate()
    {
        if (source == null || strands == null) return;
        float tick = Mathf.Floor(Time.unscaledTime * 18f);
        int n = source.positionCount;
        if (sourcePoints.Length < n) sourcePoints = new Vector3[n];
        n = source.GetPositions(sourcePoints);
        for (int j = 0; j < strands.Length; j++)
        {
            var line = strands[j];
            line.enabled = source.enabled && n > 1 && Noise(j*3.7f,tick*.81f) > .19f;
            if (!line.enabled) continue;
            float first = j < 3 ? 0 : Noise(j*4.7f,tick*.31f)*.7f;
            float length = j < 3 ? 1 : Mathf.Lerp(.12f,.7f,Noise(j*2.3f,tick*.51f));
            float last = Mathf.Min(1,first+length);
            Vector3 axis = (sourcePoints[n-1]-sourcePoints[0]).normalized;
            Vector3 side = Vector3.Cross(axis,Vector3.up).normalized;
            if (side.sqrMagnitude < .01f) side = Vector3.right;
            Vector3 up = Vector3.Cross(axis,side);
            float theta = j*2.39996f + tick*.15f;
            Vector3 offset = (side*Mathf.Cos(theta)+up*Mathf.Sin(theta))*radius;
            for(int i=0;i<12;i++)
            {
                float u = Mathf.Lerp(first,last,i/11f);
                float f = Mathf.Clamp01(u)*(n-1);
                int k = Mathf.Clamp(Mathf.FloorToInt(f),0,n-2);
                Vector3 p = Vector3.Lerp(sourcePoints[k],sourcePoints[k+1],f-k);
                float barb = (Noise(j*11+i*2.7f,tick*.61f)-.5f)*radius*.85f;
                line.SetPosition(i,p + offset*Mathf.Sin(u*Mathf.PI) + side*barb);
            }
            line.widthMultiplier = Mathf.Max(.003f,source.widthMultiplier * Mathf.Lerp(.16f,.48f,Noise(j*7.1f,tick)));
            Color tint = Color.Lerp(new Color(.04f,.35f,1f,1),source.startColor,j%3 == 0 ? .95f : .4f);
            tint.a = source.startColor.a * .85f;
            line.startColor = line.endColor = tint;
        }
    }
}
