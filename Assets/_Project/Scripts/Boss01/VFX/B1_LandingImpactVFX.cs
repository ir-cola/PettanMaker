using UnityEngine;

/// <summary>
/// テールアタック着地時の衝撃波リング＋土煙バーストを再生する。
/// Play(worldPos) を1回呼ぶだけでよい。
/// </summary>
public class B1_LandingImpactVFX : MonoBehaviour
{
    [Header("衝撃波リング")]
    [SerializeField] private int   ringCount     = 80;
    [SerializeField] private float ringSpeed     = 10f;
    [SerializeField] private float ringLifetime  = 0.55f;
    [SerializeField] private float ringSizeStart = 0.25f;
    [SerializeField] private Color ringColorHead = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color ringColorTail = new Color(1f, 0.45f, 0.1f, 0f);

    [Header("土煙バースト")]
    [SerializeField] private int   dustCount      = 55;
    [SerializeField] private float dustSpeedMin   = 2.5f;
    [SerializeField] private float dustSpeedMax   = 9f;
    [SerializeField] private float dustLifetimeMin = 0.45f;
    [SerializeField] private float dustLifetimeMax = 0.9f;
    [SerializeField] private float dustSizeMin    = 0.12f;
    [SerializeField] private float dustSizeMax    = 0.42f;
    [SerializeField] private Color dustColor      = new Color(0.72f, 0.57f, 0.32f, 0.85f);

    [Tooltip("未設定の場合は Unity デフォルトのパーティクルマテリアルを使用")]
    [SerializeField] private Material particleMaterial;

    private ParticleSystem ringPS;
    private ParticleSystem dustPS;

    public void Configure(LandingImpactVFXSettings cfg)
    {
        ringCount      = cfg.RingCount;
        ringSpeed      = cfg.RingSpeed;
        ringLifetime   = cfg.RingLifetime;
        ringColorHead  = cfg.RingColorHead;
        ringColorTail  = cfg.RingColorTail;
        dustCount      = cfg.DustCount;
        dustSpeedMin   = cfg.DustSpeedMin;
        dustSpeedMax   = cfg.DustSpeedMax;
        dustColor      = cfg.DustColor;
        if (cfg.ParticleMaterial != null) particleMaterial = cfg.ParticleMaterial;
    }

    public void Play(Vector3 worldPos)
    {
        if (ringPS == null) BuildRingPS();
        if (dustPS == null) BuildDustPS();

        ringPS.transform.position = worldPos;
        dustPS.transform.position = worldPos;

        ringPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ringPS.Play();
        dustPS.Play();
    }

    private void BuildRingPS()
    {
        var go = new GameObject("LandingRing");
        go.transform.SetParent(transform, false);
        ringPS = go.AddComponent<ParticleSystem>();
        ringPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ringPS.main;
        main.loop            = false;
        main.duration        = 0.1f;
        main.startLifetime   = ringLifetime;
        main.startSpeed      = ringSpeed;
        main.startSize       = ringSizeStart;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        var emission = ringPS.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, ringCount) });

        // 地面に平行な円から外向きに放出
        var shape = ringPS.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.15f;
        shape.arc       = 360f;
        shape.rotation  = new Vector3(90f, 0f, 0f);

        // 色: 頭部オレンジ→末端フェードアウト
        var col = ringPS.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(ringColorHead,                          0f),
                new GradientColorKey(ringColorTail,                          1f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f,  0f),
                new GradientAlphaKey(0.9f, 0.3f),
                new GradientAlphaKey(0f,  1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // サイズ: 最初大きく → フェード
        var sol = ringPS.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,   1f,  0f, -0.5f),
            new Keyframe(0.6f, 0.5f),
            new Keyframe(1f,   0f)));

        if (particleMaterial != null)
            ringPS.GetComponent<ParticleSystemRenderer>().material = particleMaterial;

        ringPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildDustPS()
    {
        var go = new GameObject("LandingDust");
        go.transform.SetParent(transform, false);
        dustPS = go.AddComponent<ParticleSystem>();
        dustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = dustPS.main;
        main.loop            = false;
        main.duration        = 0.15f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(dustLifetimeMin, dustLifetimeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(dustSpeedMin, dustSpeedMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(dustSizeMin, dustSizeMax);
        main.startColor      = dustColor;
        main.gravityModifier = 0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        var emission = dustPS.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, dustCount) });

        // 地面付近の広めの円から斜め上に飛散
        var shape = dustPS.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.6f;
        shape.arc       = 360f;
        shape.rotation  = new Vector3(60f, 0f, 0f);

        var col = dustPS.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(dustColor, 0f),
                new GradientColorKey(dustColor, 1f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f,   0f),
                new GradientAlphaKey(0.85f, 0.1f),
                new GradientAlphaKey(0.5f,  0.55f),
                new GradientAlphaKey(0f,   1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = dustPS.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,   0.3f, 0f, 4f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f,   0f)));

        if (particleMaterial != null)
            dustPS.GetComponent<ParticleSystemRenderer>().material = particleMaterial;

        dustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
