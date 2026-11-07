using UnityEngine;

/// <summary>
/// 登場用の煙（チェシャ猫のように煙の中から現れる用）。
/// Polygon スモーク（メッシュ）＋ 好きなマテリアル（SG/Smoke など）で出せる。
/// ボスに追従（Local space + 親子）し、Stop() を呼ぶまで生成し続ける（duration=0 のとき）。
/// 設定は B1_EffectManager の <see cref="Settings"/>（Scale・発生数・Duration など）から注入する。
/// </summary>
public class B1_EntranceSmokeVFX : MonoBehaviour
{
    /// <summary>登場煙のパラメータ一式（B1_EffectManager から丸ごと設定する）。</summary>
    [System.Serializable]
    public class Settings
    {
        [Header("見た目（Polygon スモーク）")]
        [Tooltip("煙マテリアル（SG/Smoke004・006 などから作ったマテリアル）。")]
        public Material material;
        [Tooltip("Polygon スモークのメッシュ（POLY/StylizedSmoke001）。未設定ならビルボード。")]
        public Mesh mesh;
        [ColorUsage(true, true)]
        [Tooltip("パーティクルの色（頂点カラー）。マテリアル側で乗算される。")]
        public Color color = Color.white;

        [Header("Alpha オーバーライド")]
        [Tooltip("マテリアルの Alpha を下の値で上書きする（アセットは汚さない / MPB）。")]
        public bool overrideAlpha = false;
        [Range(0f, 1f)]
        [Tooltip("上書きする Alpha 値。")]
        public float alpha = 1f;
        [Tooltip("Alpha を上書きするマテリアルのカラープロパティ名（SG スモークは _BaseColor）。")]
        public string alphaColorProperty = "_BaseColor";

        [Header("量 / 時間")]
        [Tooltip("毎秒の発生数。")]
        public float emitRate = 20f;
        [Tooltip("放出し続ける時間（秒）。0 以下でループ（Stop するまで出し続ける）。")]
        public float duration = 0f;
        [Tooltip("初期バーストの数（0 で無し）。")]
        public int startBurst = 8;
        [Tooltip("最大同時パーティクル数。")]
        public int maxParticles = 400;
        [Tooltip("寿命（秒）min/max。")]
        public Vector2 lifetime = new Vector2(1.2f, 2.2f);

        public enum EmitShape { Sphere, Hemisphere, Circle }

        [Header("形状 / スケール")]
        [Tooltip("スケール（開始サイズ）min/max。")]
        public Vector2 startScale = new Vector2(1.1f, 2.7f);
        [Tooltip("発生形状。Sphere=中心から球状に放射。Hemisphere=上半球。Circle=平面円。")]
        public EmitShape emitShape = EmitShape.Sphere;
        [Tooltip("発生半径。小さいほど中心から出て外へ広がる。")]
        public float radius = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("厚み。1=中身全体から / 0=表面のみから発生。")]
        public float radiusThickness = 1f;
        [Tooltip("発生高さオフセット（ボス原点からの上方向）。")]
        public float heightOffset = 0.2f;
        [Tooltip("初速 min/max。中心から外へ広がる速さ（形状の法線方向）。")]
        public Vector2 speed = new Vector2(1.2f, 3.0f);
        [Tooltip("重力係数。負で上昇する（煙が立ち上る）。")]
        public float gravity = -0.18f;
        [Tooltip("メッシュの回転速度（ランダム tumbling）。")]
        public float rotateSpeed = 0.6f;
        [Tooltip("寿命に沿った Alpha フェード（板/メッシュのポップ防止）。")]
        public bool alphaFade = true;

        [Header("動き（有機的な揺らぎ）")]
        [Tooltip("乱流ノイズの強さ。0 で無効。直線的な放射に渦を加えて煙らしいうねりを出す。")]
        public float noiseStrength = 0.5f;
        [Tooltip("ノイズの周波数。低いほど大きくゆったりうねる。")]
        public float noiseFrequency = 0.35f;
        [Tooltip("ノイズのスクロール速度。")]
        public float noiseScrollSpeed = 0.3f;
        [Tooltip("減速ドラッグ。0 で無効。勢いよく吹き出た煙が減速してその場に漂うようになる。")]
        public float drag = 1.5f;

        [Header("色の深み")]
        [Tooltip("寿命の終わりに向けて乗算する色。消え際に影色を落として立体感を出す。白で無効。")]
        public Color endTint = new Color(0.78f, 0.73f, 0.88f, 1f);
    }

    [SerializeField] private Settings settings = new();

    private ParticleSystem ps;
    private MaterialPropertyBlock smokeMpb;

    /// <summary>設定を丸ごと注入する（B1_EffectManager から）。</summary>
    public void Configure(Settings s)
    {
        if (s != null) settings = s;
        if (ps != null) Apply();
    }

    /// <summary>ボスに追従して煙を出し続ける（duration=0 のとき Stop まで継続）。</summary>
    public void Play()
    {
        if (ps == null) Build();
        Apply();
        ps.Clear(true);
        ps.Play();
    }

    /// <summary>放出を止める（残りは寿命でフェードアウト）。</summary>
    public void Stop()
    {
        if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void Build()
    {
        var go = new GameObject("EntranceSmoke");
        go.transform.SetParent(transform, false);        // ボス(B1_EffectManager)の子 → 追従
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // 現在の Settings を ParticleSystem に反映する（Play/Configure のたびに呼ぶ）。
    private void Apply()
    {
        if (ps == null) return;
        // main.duration 等は再生中に変更できないため、再生中なら一旦止めてから設定する
        // (Appear が連続で発火した場合や Timeline スクラブでの再トリガーに対応するため)。
        if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var s = settings;
        ps.transform.localPosition = Vector3.up * s.heightOffset;

        bool loop = s.duration <= 0f;

        var main = ps.main;
        main.loop            = loop;
        if (!loop) main.duration = Mathf.Max(0.05f, s.duration);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(s.lifetime.x, s.lifetime.y);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
        main.startSize       = new ParticleSystem.MinMaxCurve(s.startScale.x, s.startScale.y);
        // 頂点カラーで効くシェーダ向けに Alpha を startColor にも反映する
        Color startCol = s.color;
        if (s.overrideAlpha) startCol.a *= s.alpha;
        main.startColor      = startCol;
        main.startRotation3D = true;
        main.startRotationX  = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY  = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ  = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = s.gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // ボスに追従
        main.maxParticles    = Mathf.Max(1, s.maxParticles);
        main.playOnAwake     = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Max(0f, s.emitRate);
        if (s.startBurst > 0)
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)s.startBurst) });
        else
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

        // 中心から球状に放射する発生。Sphere/Hemisphere は法線(外向き)へ startSpeed で広がる。
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = s.emitShape switch
        {
            Settings.EmitShape.Circle     => ParticleSystemShapeType.Circle,
            Settings.EmitShape.Hemisphere => ParticleSystemShapeType.Hemisphere,
            _                             => ParticleSystemShapeType.Sphere,
        };
        shape.radius          = Mathf.Max(0.001f, s.radius);
        shape.radiusThickness = s.radiusThickness;
        shape.arc             = 360f;
        // Circle は水平に寝かせる。Sphere/Hemisphere は既定向きでOK。
        shape.rotation = s.emitShape == Settings.EmitShape.Circle ? new Vector3(90f, 0f, 0f) : Vector3.zero;

        // 最初を濃く、寿命に沿って単調に薄くする Alpha フェード。
        // 生成直後のフェードインを挟まないことで、煙が消えるまで自然に減衰し続ける。
        var col = ps.colorOverLifetime;
        col.enabled = s.alphaFade;
        if (s.alphaFade)
        {
            Color end = new Color(s.endTint.r, s.endTint.g, s.endTint.b, 1f);
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.45f),
                    new GradientColorKey(end,         1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f,    0f),
                    new GradientAlphaKey(0.85f, 0.25f),
                    new GradientAlphaKey(0.45f, 0.65f),
                    new GradientAlphaKey(0f,    1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);
        }

        // 乱流ノイズ: 放射直線に渦を加えて煙らしいうねりを出す。
        var noise = ps.noise;
        noise.enabled = s.noiseStrength > 0.0001f;
        if (noise.enabled)
        {
            noise.strength    = s.noiseStrength;
            noise.frequency   = Mathf.Max(0.01f, s.noiseFrequency);
            noise.scrollSpeed = s.noiseScrollSpeed;
            noise.damping     = true;
            noise.quality     = ParticleSystemNoiseQuality.Medium;
        }

        // 減速ドラッグ: 吹き出し→減速→漂う、の緩急を作る。
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = s.drag > 0.0001f;
        if (limit.enabled)
        {
            limit.drag              = s.drag;
            limit.separateAxes      = false;
            limit.multiplyDragByParticleSize     = false;
            limit.multiplyDragByParticleVelocity = true;
        }

        // スケール成長
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1.0f), new Keyframe(1f, 1.4f)));

        // 3D tumbling
        var rot = ps.rotationOverLifetime;
        rot.enabled = s.rotateSpeed > 0.0001f;
        if (rot.enabled)
        {
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-s.rotateSpeed, s.rotateSpeed);
            rot.y = new ParticleSystem.MinMaxCurve(-s.rotateSpeed, s.rotateSpeed);
            rot.z = new ParticleSystem.MinMaxCurve(-s.rotateSpeed, s.rotateSpeed);
        }

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (s.mesh != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh       = s.mesh;      // Polygon スモークのメッシュ
        }
        else
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }
        if (s.material != null) renderer.sharedMaterial = s.material;

        ApplyAlphaOverride(renderer, s);
    }

    // マテリアルの Alpha を MPB で上書きする（sharedMaterial アセットは書き換えない）。
    private void ApplyAlphaOverride(ParticleSystemRenderer renderer, Settings s)
    {
        if (smokeMpb == null) smokeMpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(smokeMpb);

        bool canOverride = s.overrideAlpha && s.material != null
                        && !string.IsNullOrEmpty(s.alphaColorProperty)
                        && s.material.HasProperty(s.alphaColorProperty);
        if (canOverride)
        {
            Color c = s.material.GetColor(s.alphaColorProperty);
            c.a = s.alpha;
            smokeMpb.SetColor(s.alphaColorProperty, c);
        }
        else if (!string.IsNullOrEmpty(s.alphaColorProperty))
        {
            // 上書き解除：以前セットした値を消す
            smokeMpb.Clear();
        }
        renderer.SetPropertyBlock(smokeMpb);
    }
}
