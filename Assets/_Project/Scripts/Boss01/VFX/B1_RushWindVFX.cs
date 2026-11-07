using UnityEngine;

/// <summary>
/// Rush中に風を切るストリークエフェクト。
/// Play(rushDir) で突進方向を受け取り、逆方向へ高速パーティクルを放出する。
/// </summary>
public class B1_RushWindVFX : MonoBehaviour
{
    [Header("放出形状（ボス前面断面）")]
    [SerializeField] private float emitWidth  = 1.4f;
    [SerializeField] private float emitHeight = 1.8f;
    [SerializeField] private float emitDepth  = 0.2f;

    [Header("速度")]
    [SerializeField] private float speedMin = 14f;
    [SerializeField] private float speedMax = 26f;

    [Header("放出レート")]
    [SerializeField] private float emitRate = 120f;

    [Header("寿命")]
    [SerializeField] private float lifetimeMin = 0.10f;
    [SerializeField] private float lifetimeMax = 0.22f;

    [Header("サイズ")]
    [SerializeField] private float sizeMin = 0.03f;
    [SerializeField] private float sizeMax = 0.10f;

    [Header("ストレッチ")]
    [SerializeField] private float velocityScale = 0.12f;

    [Header("方向ランダム幅 [0=直線 1=全方向]")]
    [SerializeField] [Range(0f, 1f)] private float spreadAmount = 0.15f;

    [Header("色")]
    [SerializeField] private Color colorHead = new Color(1.0f, 1.0f, 1.0f,  1.0f);
    [SerializeField] private Color colorMid  = new Color(0.55f, 0.85f, 1.0f, 0.8f);

    [Tooltip("未設定の場合はデフォルトのパーティクルマテリアルを使用")]
    [SerializeField] private Material particleMaterial;

    private ParticleSystem         ps;
    private ParticleSystemRenderer psRenderer;

    // Awake では Build しない。Configure() または Play() の初回呼び出しで遅延 Build する。

    /// <summary>B1_EffectSettings からパラメータを適用する（Play() より前に呼ぶこと）。</summary>
    public void Configure(RushWindVFXSettings cfg)
    {
        emitWidth     = cfg.EmitWidth;
        emitHeight    = cfg.EmitHeight;
        emitDepth     = cfg.EmitDepth;
        speedMin      = cfg.SpeedMin;
        speedMax      = cfg.SpeedMax;
        emitRate      = cfg.EmitRate;
        lifetimeMin   = cfg.LifetimeMin;
        lifetimeMax   = cfg.LifetimeMax;
        velocityScale = cfg.VelocityScale;
        spreadAmount  = cfg.SpreadAmount;
        colorHead     = cfg.ColorHead;
        colorMid      = cfg.ColorMid;
        if (cfg.ParticleMaterial != null) particleMaterial = cfg.ParticleMaterial;
    }

    /// <summary>突進開始時に呼ぶ。rushDir は突進方向の単位ベクトル。</summary>
    public void Play(Vector3 rushDir)
    {
        if (ps == null) Build();
        // 子GOだけ回転させる。Bossのtransformには触れない。
        if (rushDir != Vector3.zero)
            ps.transform.rotation = Quaternion.LookRotation(-rushDir, Vector3.up);
        ps.Play();
    }

    public void Stop()
    {
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void Build()
    {
        // ボス本体のtransformに触れないよう専用子GOにParticleSystemを持たせる
        var psGO = new GameObject("B1_RushWindVFX_PS");
        psGO.transform.SetParent(transform, false);
        ps         = psGO.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake=true のデフォルト再生を即止め
        psRenderer = ps.GetComponent<ParticleSystemRenderer>();

        // --- Main ---
        var main = ps.main;
        main.loop            = true;
        main.duration        = 99f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorHead, colorMid);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        // --- Emission ---
        var emission = ps.emission;
        emission.rateOverTime = emitRate;

        // --- Shape: ボス前面断面の薄いボックス ---
        var shape = ps.shape;
        shape.shapeType           = ParticleSystemShapeType.Box;
        shape.scale               = new Vector3(emitWidth, emitHeight, emitDepth);
        shape.randomDirectionAmount = spreadAmount;

        // --- Color over Lifetime: 白フラッシュ → 水色 → 透明 ---
        var col  = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(colorHead,          0f),
                new GradientColorKey(colorMid,           0.35f),
                new GradientColorKey(new Color(0.3f, 0.6f, 1f), 1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f,  0f),
                new GradientAlphaKey(1f,  0.08f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f,  1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // --- Size over Lifetime: 細く生まれ → 膨らみ → 消える ---
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,   0.2f, 0f, 5f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f,   0f)));

        // --- Renderer: 速度方向に引き伸ばしてストリーク表現 ---
        psRenderer.renderMode    = ParticleSystemRenderMode.Stretch;
        psRenderer.velocityScale = velocityScale;
        psRenderer.lengthScale   = 0.5f;
        psRenderer.sortingFudge  = -1f;
        psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (particleMaterial != null)
            psRenderer.material = particleMaterial;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
