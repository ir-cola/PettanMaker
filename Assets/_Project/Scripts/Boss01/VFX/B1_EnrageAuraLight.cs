using UnityEngine;

/// <summary>
/// 怒りオーラのポイントライトを「心拍＋炎の揺らぎ」で明滅させる。
/// 心拍はオーラの脈動リング（B1_EnrageAuraVFXBuilder の PulsePeriod）と同じ周期にして
/// リングが広がる瞬間に最も明るくなるよう同期させる。
/// </summary>
[RequireComponent(typeof(Light))]
public class B1_EnrageAuraLight : MonoBehaviour
{
    [Tooltip("基準の明るさ。心拍と揺らぎはこの値を中心に振れる。")]
    [SerializeField] private float baseIntensity = 6f;
    [Tooltip("心拍1拍の周期（秒）。オーラの脈動リングの発生間隔に合わせる。")]
    [SerializeField] private float pulsePeriod = 1.25f;
    [Tooltip("心拍で上乗せする明るさ。拍の瞬間にピークが来て指数的に減衰する。")]
    [SerializeField] private float pulseGain = 3f;
    [Tooltip("拍のピークが減衰する速さ。大きいほど「ドクン」と鋭くなる。")]
    [SerializeField] private float pulseDecay = 4f;
    [Tooltip("炎の揺らぎ（Perlin ノイズ）で振れる明るさの幅。")]
    [SerializeField] private float flickerGain = 1.2f;
    [Tooltip("揺らぎの速さ。")]
    [SerializeField] private float flickerSpeed = 7f;

    private Light auraLight;
    private float seed;
    private float startTime;

    private void Awake()
    {
        auraLight = GetComponent<Light>();
        seed = Random.value * 100f;
    }

    // ParticleSystem のバーストは再生開始時刻基準なので、こちらも有効化時刻を 0 拍目にして位相を合わせる
    private void OnEnable() => startTime = Time.time;

    private void Update()
    {
        // 拍からの経過時間で指数減衰（ドクン…ドクン…）
        float phase = Mathf.Repeat(Time.time - startTime, pulsePeriod);
        float pulse = pulseGain * Mathf.Exp(-pulseDecay * phase);

        // 炎らしい細かい揺らぎ（-0.5〜+0.5 に正規化した Perlin）
        float flicker = (Mathf.PerlinNoise(seed, Time.time * flickerSpeed) - 0.5f) * flickerGain;

        auraLight.intensity = Mathf.Max(0f, baseIntensity + pulse + flicker);
    }
}
