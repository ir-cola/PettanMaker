using UnityEngine;

/// <summary>
/// 着地衝撃・爆発などの瞬間に点灯し、急減衰して自壊するワンショットのポイントライト。
/// B1_ScreenColorDrain と同じ自己完結・自己破棄パターン。影は焼かないのでコストはほぼゼロ。
/// B1_Toon は Additional Lights 対応済みのため、地面だけでなくボス自身も照らされる。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_ImpactLight : MonoBehaviour
{
    private Light light_;
    private float duration;
    private float peakIntensity;
    private float elapsed;

    /// <summary>フラッシュを1回発生させる。デフォルトは白橙 (爆発想定)。</summary>
    public static void Spawn(Vector3 position, Color color, float intensity = 6f,
                             float range = 12f, float duration = 0.25f)
    {
        var go = new GameObject("B1_ImpactLight");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト
        go.transform.position = position + Vector3.up * 1f; // 地面直置きだと減衰が急なので浮かせる

        var l      = go.AddComponent<Light>();
        l.type     = LightType.Point;
        l.color    = color;
        l.intensity = intensity;
        l.range    = range;
        l.shadows  = LightShadows.None;

        var self           = go.AddComponent<B1_ImpactLight>();
        self.light_        = l;
        self.peakIntensity = intensity;
        self.duration      = Mathf.Max(0.01f, duration);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        // 二乗減衰で「一瞬光って消える」フラッシュ感を出す。
        light_.intensity = peakIntensity * (1f - t) * (1f - t);
        if (t >= 1f) Destroy(gameObject);
    }
}
