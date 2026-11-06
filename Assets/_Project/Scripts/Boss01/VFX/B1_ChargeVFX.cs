using UnityEngine;

/// <summary>
/// チャージテレグラフ中にボス周囲からパーティクルを収束させる演出。
/// UpdateProgress(t) を毎フレーム呼ぶことで放出量・速度・サイズ・収束半径が連動して変化する。
/// </summary>
public class B1_ChargeVFX : MonoBehaviour
{
    // ── 形状 ────────────────────────────────────────────────────────────────
    [Header("形状")]
    [SerializeField] private float emitRadiusStart = 2f;
    [SerializeField] private float emitRadiusEnd   = 0.6f;  // チャージ完了時に絞り込む半径
    [SerializeField] [Range(0f, 1f)] private float radiusThickness = 0.15f; // 0=球面のみ 1=全体積

    // ── パーティクル挙動 ─────────────────────────────────────────────────
    [Header("速度")]
    [SerializeField] private float inwardSpeedStart = 4f;
    [SerializeField] private float inwardSpeedEnd   = 20f;

    [Header("放出レート")]
    [SerializeField] private float emitRateStart = 20f;
    [SerializeField] private float emitRateEnd   = 120f;

    [Header("寿命")]
    [SerializeField] private float lifetimeMin = 0.5f;
    [SerializeField] private float lifetimeMax = 1.0f;

    [Header("サイズ")]
    [SerializeField] private float sizeMin           = 0.1f;
    [SerializeField] private float sizeMax           = 0.35f;
    [SerializeField] private float sizePeakMult      = 1.8f;  // チャージ完了時の倍率

    // ── 有機的な動き ─────────────────────────────────────────────────────
    [Header("ノイズ（有機的な揺れ）")]
    [SerializeField] private float noiseStrength    = 0.6f;
    [SerializeField] private float noiseFrequency   = 1.2f;
    [SerializeField] private float noiseScrollSpeed = 0.4f;

    [Header("回転")]
    [SerializeField] private float rotSpeedMin = -120f; // deg/s
    [SerializeField] private float rotSpeedMax =  120f;

    // ── 見た目 ───────────────────────────────────────────────────────────
    [Header("色")]
    [SerializeField] private float startAlpha  = 0.4f;
    [SerializeField] private Color colorOuter  = new Color(1f, 0.45f, 0.05f, 1f);
    [SerializeField] private Color colorInner  = new Color(1f, 0.95f, 0.35f, 1f);

    [Tooltip("未設定の場合は Unity デフォルトのパーティクルマテリアルを使用")]
    [SerializeField] private Material particleMaterial;

    // ── バースト ─────────────────────────────────────────────────────────
    [Header("バースト（チャージ完了→突進）")]
    [SerializeField] private int   burstCount    = 120;
    [SerializeField] private float burstSpeedMin = 10f;
    [SerializeField] private float burstSpeedMax = 24f;
    [SerializeField] private float burstLifetimeMin = 0.2f;
    [SerializeField] private float burstLifetimeMax = 0.4f;

    // ── ライト ───────────────────────────────────────────────────────────
    [Header("ライト（チャージ光で地面とボス自身を照らす）")]
    [Tooltip("Player のチャージライトと同様に実ライトを置く。B1_Toon は Additional Lights 対応済み。")]
    [SerializeField] private bool  enableLight = true;
    [Tooltip("チャージ完了時のライト強度。ボスは大きいので Player(2.5) より強め。")]
    [SerializeField] private float lightIntensityPeak = 5f;
    [SerializeField] private float lightRange = 16f;
    [Tooltip("ライトの高さオフセット。地面すれすれだと減衰が急なので少し浮かせる。")]
    [SerializeField] private float lightHeight = 1.5f;
    [Header("バーストフラッシュ")]
    [SerializeField] private float burstLightIntensity = 9f;
    [SerializeField] private float burstLightDecay = 0.3f;

    // ── 内部 ─────────────────────────────────────────────────────────────
    private ParticleSystem ps;
    private ParticleSystem burstPS;
    private Light          chargeLight;
    private float          flashTimer;   // バーストフラッシュの残り時間 (0 で消灯)

    // Awake では Build しない。Configure() または Play() の初回呼び出しで遅延 Build する。

    // ─────────────────────────────────────────────────────────────────────
    // 公開 API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>B1_EffectSettings からパラメータを適用する（Play() より前に呼ぶこと）。</summary>
    public void Configure(ChargeVFXSettings cfg)
    {
        emitRadiusStart  = cfg.EmitRadiusStart;
        emitRadiusEnd    = cfg.EmitRadiusEnd;
        inwardSpeedStart = cfg.InwardSpeedStart;
        inwardSpeedEnd   = cfg.InwardSpeedEnd;
        emitRateStart    = cfg.EmitRateStart;
        emitRateEnd      = cfg.EmitRateEnd;
        colorOuter       = cfg.ColorOuter;
        colorInner       = cfg.ColorInner;
        burstCount       = cfg.BurstCount;
        burstSpeedMin    = cfg.BurstSpeedMin;
        burstSpeedMax    = cfg.BurstSpeedMax;
        if (cfg.ParticleMaterial != null) particleMaterial = cfg.ParticleMaterial;
    }

    public void Play()
    {
        if (ps == null) BuildPS();
        ps.Play();
        if (enableLight)
        {
            if (chargeLight == null) BuildLight();
            chargeLight.color     = colorInner;
            chargeLight.intensity = 0f;
            chargeLight.enabled   = true;
        }
    }

    public void Stop()
    {
        if (chargeLight != null && flashTimer <= 0f) chargeLight.enabled = false;
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// チャージ完了→突進開始直前に呼ぶ。収束エネルギーを爆散させる。
    public void Burst()
    {
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (burstPS == null) BuildBurstPS();
        burstPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        burstPS.Play();

        // ライトも白く強烈にフラッシュさせ、Update で減衰させる。
        if (enableLight)
        {
            if (chargeLight == null) BuildLight();
            chargeLight.color     = Color.Lerp(colorInner, Color.white, 0.7f);
            chargeLight.intensity = burstLightIntensity;
            chargeLight.enabled   = true;
            flashTimer            = burstLightDecay;
        }
    }

    /// t: 0=チャージ開始  1=チャージ完了。毎フレーム呼ぶこと。
    public void UpdateProgress(float t)
    {
        if (ps == null) return;

        float e1 = t * t;           // ease-in（速度・サイズ等に使用）
        float e2 = t * t * t;       // 強めのease-in（レートに使用：終盤で急増）

        var main = ps.main;

        // 速度: 終盤に向けて加速（より強く引き込まれる）
        main.startSpeed = -Mathf.Lerp(inwardSpeedStart, inwardSpeedEnd, e1);

        // サイズ: チャージが溜まるほど粒が大きくなる
        float s = Mathf.Lerp(1f, sizePeakMult, e1);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin * s, sizeMax * s);

        // 色・アルファ: 終盤に向けて不透明になる
        float alpha = Mathf.Lerp(startAlpha, 1f, e1);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(colorInner.r, colorInner.g, colorInner.b, colorInner.a * alpha),
            new Color(colorOuter.r, colorOuter.g, colorOuter.b, colorOuter.a * alpha));

        // 放出レート: 終盤に向けて急増（密度が上がる）
        var emission = ps.emission;
        emission.rateOverTime = Mathf.Lerp(emitRateStart, emitRateEnd, e2);

        // 収束半径: チャージが進むほど発生点が中心に近づく
        var shape = ps.shape;
        shape.radius = Mathf.Lerp(emitRadiusStart, emitRadiusEnd, e1);

        // ライト: チャージ量に応じて増光。終盤は微振動を乗せて「はち切れそう」感を出す。
        if (chargeLight != null && flashTimer <= 0f)
        {
            float flicker = 1f + 0.08f * e1 * Mathf.Sin(Time.time * 40f);
            chargeLight.intensity = lightIntensityPeak * e1 * flicker;
            chargeLight.color     = Color.Lerp(colorOuter, colorInner, t);
        }
    }

    private void Update()
    {
        // バーストフラッシュの減衰。終わったら消灯する。
        if (flashTimer <= 0f) return;
        flashTimer = Mathf.Max(0f, flashTimer - Time.deltaTime);
        if (chargeLight == null) return;
        float k = burstLightDecay > 0f ? flashTimer / burstLightDecay : 0f;
        chargeLight.intensity = burstLightIntensity * k * k;
        if (flashTimer <= 0f) chargeLight.enabled = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // PS 構築
    // ─────────────────────────────────────────────────────────────────────

    private void BuildPS()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake=true のデフォルト再生を即止め

        // Main
        var main             = ps.main;
        main.loop            = true;
        main.duration        = 99f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSpeed      = -inwardSpeedStart;
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorInner, colorOuter);
        main.startRotation   = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        // Emission
        var emission          = ps.emission;
        emission.rateOverTime = emitRateStart;

        // Shape
        var shape             = ps.shape;
        shape.shapeType       = ParticleSystemShapeType.Sphere;
        shape.radius          = emitRadiusStart;
        shape.radiusThickness = radiusThickness;

        // Color over Lifetime: 外周の橙→内側で白くフラッシュ→消える
        var col  = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(colorOuter,  0f),
                new GradientColorKey(colorInner,  0.45f),
                new GradientColorKey(Color.white, 0.85f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f,  0f),
                new GradientAlphaKey(1f,  0.12f),
                new GradientAlphaKey(1f,  0.65f),
                new GradientAlphaKey(0f,  1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Size over Lifetime: 小さく生まれ→膨らみ→消える
        var sol     = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.1f,  0f,  4f),
            new Keyframe(0.4f,  1f),
            new Keyframe(1f,    0f)));

        // Rotation over Lifetime: ゆっくりスピン
        var rotLife     = ps.rotationOverLifetime;
        rotLife.enabled = true;
        rotLife.z       = new ParticleSystem.MinMaxCurve(
            rotSpeedMin * Mathf.Deg2Rad,
            rotSpeedMax * Mathf.Deg2Rad);

        // Noise: 軌道に有機的な揺れを追加
        var noise         = ps.noise;
        noise.enabled     = true;
        noise.strength    = noiseStrength;
        noise.frequency   = noiseFrequency;
        noise.scrollSpeed = noiseScrollSpeed;
        noise.damping     = true;

        // Material
        if (particleMaterial != null)
            ps.GetComponent<ParticleSystemRenderer>().material = particleMaterial;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildLight()
    {
        var go = new GameObject("B1_ChargeVFX_Light");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, lightHeight, 0f);
        chargeLight          = go.AddComponent<Light>();
        chargeLight.type     = LightType.Point;
        chargeLight.range    = lightRange;
        chargeLight.shadows  = LightShadows.None;
        chargeLight.enabled  = false;
    }

    private void BuildBurstPS()
    {
        var go = new GameObject("B1_ChargeVFX_Burst");
        go.transform.SetParent(transform, false);
        burstPS = go.AddComponent<ParticleSystem>();
        burstPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake=true のデフォルト再生を即止め

        // Main
        var main             = burstPS.main;
        main.loop            = false;
        main.duration        = 0.4f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(burstLifetimeMin, burstLifetimeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(burstSpeedMin, burstSpeedMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin * 1.5f, sizeMax * 2f);
        main.startColor      = new ParticleSystem.MinMaxGradient(Color.white, colorInner);
        main.startRotation   = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        // Emission: 単発バースト
        var emission = burstPS.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

        // Shape: 極小の中心点から全方向へ
        var shape             = burstPS.shape;
        shape.shapeType       = ParticleSystemShapeType.Sphere;
        shape.radius          = 0.1f;
        shape.radiusThickness = 1f;

        // Color over Lifetime: 白フラッシュ→橙→透明
        var col     = burstPS.colorOverLifetime;
        col.enabled = true;
        var grad    = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(colorInner,  0.25f),
                new GradientColorKey(colorOuter,  0.6f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Size over Lifetime: 大きく生まれて急縮小
        var sol     = burstPS.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,   1.5f, 0f, -5f),
            new Keyframe(0.4f, 0.6f),
            new Keyframe(1f,   0f)));

        if (particleMaterial != null)
            burstPS.GetComponent<ParticleSystemRenderer>().material = particleMaterial;

        burstPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
