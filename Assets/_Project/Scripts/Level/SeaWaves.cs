using UnityEngine;
using WaterSystem;
using WaterSystem.Data;

/// <summary>
/// 海面高度：照 Boat Attack Water 著色器（GerstnerWaves.hlsl + WaterCommon.hlsl 的 WaveVertexOperations）
/// 同一套算法算出畫面上海面在某個 (x, z) 的高度，船才能貼著看得到的水面：
///   - Gerstner 波：頂點會水平移動（peak = 1.5），所以要反推「哪個頂點移到了這裡」再取它的高度
///   - 越淺的地方浪越小：高度乘上 saturate(水深 × 0.1 + 0.05)，水平移動乘上 saturate(3 × 同一個值)
/// 場景裡沒有 Water（或還沒產生波浪資料）時用 fallbackY + 簡單的正弦波代替。
/// 想調整浪的大小：Boat Attack Water System/Prefabs/WaterSurfaceData.asset 的 Basic Wave Settings。
/// </summary>
public static class SeaWaves
{
    const float Peak = 1.5f;      // 和著色器一樣
    const int InverseSteps = 3;   // 反推水平位移的次數，3 次就很準

    static Water water;
    static int searchedFrame = -1;

    static Water GetWater()
    {
        if (water != null) return water;
        // 沒有 Water 的場景每幀最多找一次
        if (searchedFrame == Time.frameCount) return null;
        searchedFrame = Time.frameCount;
        water = Object.FindAnyObjectByType<Water>();
        return water;
    }

    /// <summary>
    /// 海面在 p 這個位置的世界高度。
    /// waterDepth = 這裡的水深 (m)，用來照著色器把淺水的浪變小；不知道就給很大的數字（當成深海）
    /// </summary>
    public static float HeightAt(Vector3 p, float fallbackY, float waterDepth = 1000f)
    {
        var w = GetWater();
        if (w == null || w._waves == null || w._waves.Length == 0)
            return fallbackY + Mathf.Sin(Time.timeSinceLevelLoad * 1.2f + p.x * 0.15f + p.z * 0.1f) * 0.25f;

        float opacity = Mathf.Clamp01(waterDepth * 0.1f + 0.05f);
        float vMask = opacity;
        float hMask = Mathf.Clamp01(opacity * 3f);

        // 著色器用 _Time.y（= 載入關卡後的秒數）
        float time = Time.timeSinceLevelLoad;
        var waves = w._waves;
        Vector2 target = new Vector2(p.x, p.z);

        // 頂點 x0 會被推到 x0 + 水平位移(x0)；反推出推到 target 的那個 x0
        Vector2 x0 = target;
        for (int i = 0; i < InverseSteps; i++)
        {
            Displace(waves, x0, time, out Vector2 shift, out _);
            x0 = target - shift * hMask;
        }
        Displace(waves, x0, time, out _, out float height);
        return w.transform.position.y + height * vMask;
    }

    /// <summary>位置 pos 的頂點被 Gerstner 波推動的水平位移和高度（和 GerstnerWave() 一樣）</summary>
    static void Displace(Wave[] waves, Vector2 pos, float time, out Vector2 shift, out float height)
    {
        shift = Vector2.zero;
        height = 0f;
        int count = waves.Length;
        float multi = 1f / count;
        for (int i = 0; i < count; i++)
        {
            var wave = waves[i];
            if (wave.amplitude <= 0f || wave.wavelength <= 0f) continue;
            float k = 6.28318f / wave.wavelength;
            float speed = Mathf.Sqrt(9.8f * k);
            float qi = Peak / (wave.amplitude * k * count);
            Vector2 origin = new Vector2(wave.origin.x, wave.origin.y);
            float rad = wave.direction * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (1f - wave.onmiDir)
                        + (pos - origin) * wave.onmiDir;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
            float calc = Vector2.Dot(dir, pos - origin * wave.onmiDir) * k - time * speed;
            shift += dir * (qi * wave.amplitude * Mathf.Cos(calc));
            height += Mathf.Sin(calc) * wave.amplitude * multi;
        }
    }
}
