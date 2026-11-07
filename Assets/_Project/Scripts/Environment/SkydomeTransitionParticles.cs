/**===========================================================================

 File   : スカイドーム切替バースト [SkydomeTransitionParticles.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/15
 -----------------------------------------------------------------------------

   スカイドームのクロスフェード切り替えに同期して、プレイ空間全体へ光の粒子を
   一気に湧かせるワンショット演出。SceneLightParticles(常時漂うダスト)の見た目を
   流用しつつ loop=false のバーストにして「世界が塗り替わる瞬間」を飾る。

   ・発生密度はクロスフェードのピーク付近(0.35〜0.65)で最大になる台形カーブで制御。
   ・色は切り替えの炎エッジ(マゼンタ)/白熱コア(金)へ自動で合わせられる(Play で受け取る)。
   ・寿命を長めに取り、バースト後も数秒スターダストとして漂わせ余韻を残す。
   ・切り替えの瞬間、中心から地平線へ広がるリングウェーブを1発飛ばして
     「塗り替えがここから広がった」という起点を見せる。
   ・粒子の半分はまたたき(明滅)、回転も入れてスターダストの粒立ちを出す。

   ワールド空間シミュレーションなので、この Transform をアリーナ中心に置き、
   volumeSize でプレイ範囲を覆う。SkydomeBlendSystem から Play() で駆動する。

 ===========================================================================**/
using UnityEngine;

[DisallowMultipleComponent]
public class SkydomeTransitionParticles : MonoBehaviour
{
    [Header("湧く範囲（ワールド, この Transform 中心のボックス）")]
    [SerializeField] private Vector3 volumeSize = new Vector3(60f, 24f, 60f);

    [Header("量・大きさ・寿命")]
    [Tooltip("同時に存在できる最大粒子数。")]
    [SerializeField] private int     maxParticles  = 900;
    [Tooltip("クロスフェードのピーク時に達する毎秒発生数。")]
    [SerializeField] private float   peakEmission  = 700f;
    [Tooltip("切り替え開始の瞬間に一気に充填するバースト数（空白を埋め、立ち上がりを強くする）。")]
    [SerializeField] private int     initialBurst  = 220;
    [SerializeField] private Vector2 sizeRange     = new Vector2(0.18f, 0.55f);
    [Tooltip("粒子の寿命。長めにするとバースト後もスターダストとして漂い余韻が残る。")]
    [SerializeField] private Vector2 lifetime      = new Vector2(2.5f, 5.5f);

    [Header("動き")]
    [Tooltip("バーストが外へふわっと膨らむ初速。")]
    [SerializeField] private Vector2 burstSpeed    = new Vector2(0.6f, 2.2f);
    [SerializeField] private float   riseSpeed     = 0.5f;    // ゆっくり上昇
    [SerializeField] private float   driftSpeed    = 0.35f;   // 横のたゆたい
    [SerializeField] private float   turbulence    = 0.55f;   // ノイズ乱流の強さ
    [SerializeField] private float   turbulenceFreq = 0.2f;

    [Header("リングウェーブ（切り替えの瞬間、中心から地平線へ広がる光の波）")]
    [Tooltip("切り替え開始時に中心から放射状に飛ばす粒子数。0 で無効。")]
    [SerializeField] private int     ringCount    = 90;
    [Tooltip("リングが外へ走る初速。速いほど「波が走った」感が出る。")]
    [SerializeField] private float   ringSpeed    = 14f;
    [Tooltip("リングの発生高さ（この Transform からの Y オフセット）。")]
    [SerializeField] private float   ringHeight   = 1.2f;
    [Tooltip("リング粒子の寿命。本体より短めにして波だけ先に消す。")]
    [SerializeField] private Vector2 ringLifetime = new Vector2(1.2f, 2.2f);

    [Header("色（加算発光。1超でよりまぶしく＝ブルームで光る）")]
    [Tooltip("Play() が渡す切り替え色（炎エッジ/白熱コア）でこの2色を上書きするか。")]
    [SerializeField] private bool  useTransitionColors = true;
    [SerializeField] private Color colorWarm = new Color(1.6f, 0.35f, 1.2f, 1f);  // マゼンタ（既定=B1の炎エッジ）
    [SerializeField] private Color colorCool = new Color(2.4f, 1.6f, 0.6f, 1f);   // 金（既定=B1の白熱コア）
    [SerializeField, Range(0.2f, 6f)] private float brightness = 1.4f;            // >1.1 でブルーム発光

    private const string ShaderName = "Environment/LightParticle";
    private Material _mat;
    private ParticleSystem _ps;
    private bool _built;

    private void Awake() { EnsureBuilt(); }

    /// <summary>Inspector の色でバーストを再生する。</summary>
    public void Play(float blendDuration)
    {
        Play(blendDuration, colorWarm, colorCool);
    }

    /// <summary>
    /// スカイドーム切り替えに同期してバーストを再生する。
    /// 発生密度は blendDuration の中央でピークになる。useTransitionColors が ON なら
    /// warm/cool（切り替えの炎エッジ/白熱コア）で発光色を合わせる。
    /// </summary>
    public void Play(float blendDuration, Color warm, Color cool)
    {
        EnsureBuilt();
        if (_ps == null) return;

        if (useTransitionColors)
        {
            colorWarm = warm;
            colorCool = cool;
        }

        float dur = Mathf.Max(0.1f, blendDuration);
        ConfigureForDuration(dur);

        _ps.Clear();
        _ps.Play();
        if (initialBurst > 0) _ps.Emit(initialBurst);   // 立ち上がりの空白を即時充填
        EmitRingWave();                                  // 中心から地平線へ走る光の波
    }

    // 中心から放射状に飛ぶリング状のバーストを撃つ。
    // Emission/Shape を通さず EmitParams で直接撒くので、本体の設定に影響しない。
    private void EmitRingWave()
    {
        if (ringCount <= 0 || _ps == null) return;

        var ep = new ParticleSystem.EmitParams();
        Vector3 center = transform.position + Vector3.up * ringHeight;
        for (int i = 0; i < ringCount; i++)
        {
            // 等間隔 + 少しジッターで機械的な円に見えないようにする
            float ang = (i + Random.value * 0.8f) / ringCount * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

            ep.position      = center + dir * 1.5f;
            ep.velocity      = dir * (ringSpeed * Random.Range(0.85f, 1.15f))
                             + Vector3.up * Random.Range(0.4f, 1.6f);
            ep.startLifetime = Random.Range(ringLifetime.x, ringLifetime.y);
            ep.startSize     = Random.Range(sizeRange.x, sizeRange.y) * 1.3f; // 波の粒は少し大きく
            ep.startColor    = Bright(Color.Lerp(colorWarm, colorCool, Random.value));
            _ps.Emit(ep, 1);
        }
    }

    /// <summary>放出を止める（残粒子は寿命でフェードアウト）。</summary>
    public void Stop()
    {
        if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        Build();
        _built = true;
    }

    // 発生カーブ・色をこの回の blendDuration に合わせて確定する。
    private void ConfigureForDuration(float dur)
    {
        var main = _ps.main;
        main.duration      = dur;
        main.startColor    = new ParticleSystem.MinMaxGradient(Bright(colorWarm), Bright(colorCool));

        // 密度はクロスフェードのピーク帯(0.35〜0.65)で最大を保つ台形。
        // 三角形の山なりより「切り替わっている最中」の充実感が続く。
        var emission = _ps.emission;
        var rate = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.35f, 1f),
            new Keyframe(0.65f, 1f),
            new Keyframe(1f, 0f));
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(peakEmission, rate);
    }

    private void Build()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps == null) ps = gameObject.AddComponent<ParticleSystem>();
        _ps = ps;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // --- Main ---
        var main = ps.main;
        main.loop            = false;   // ワンショット
        main.duration        = 2f;      // Play() で blendDuration に上書き
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(burstSpeed.x, burstSpeed.y); // 外へ膨らむ初速
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startColor      = new ParticleSystem.MinMaxGradient(Bright(colorWarm), Bright(colorCool));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // ★ ワールド配置
        main.maxParticles    = maxParticles;
        main.playOnAwake     = false;   // トリガー駆動
        main.prewarm         = false;
        main.startRotation   = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI); // 粒の向きをばらす

        // --- Rotation over Lifetime: ゆっくり回してきらめきの粒立ちを出す ---
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        // --- Emission（rate は ConfigureForDuration で山なりに設定） ---
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        // --- Shape: ボックス内から湧く。少しランダム方向を混ぜて全体に散らす ---
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = volumeSize;
        shape.randomDirectionAmount = 0.6f;

        // --- Velocity: ゆっくり上昇＋横のたゆたい（ワールド軸） ---
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.3f, riseSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);

        // --- Noise: 空気のゆらぎ ---
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = turbulence;
        noise.frequency   = turbulenceFreq;
        noise.scrollSpeed = 0.14f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;
        noise.damping     = true;

        // --- Color over Lifetime: 2種のグラデーションからランダム選択 ---
        //   滑らか … ふわっと現れ、ふわっと消える（従来）
        //   またたき … 寿命の途中で2回暗くなる明滅。星のきらめきに見える
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
        var gSmooth = new Gradient();
        gSmooth.SetKeys(colorKeys,
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0f, 1f),
            });
        var gTwinkle = new Gradient();
        gTwinkle.SetKeys(colorKeys,
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0.3f, 0.35f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0.35f, 0.75f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(gSmooth, gTwinkle); // 粒ごとにランダム

        // --- Size over Lifetime: 湧いて膨らみ、しぼんで消える ---
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.6f)));

        // --- Renderer: 加算発光のビルボード ---
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode        = ParticleSystemRenderMode.Billboard;
        r.alignment         = ParticleSystemRenderSpace.View;
        r.sortMode          = ParticleSystemSortMode.None;
        r.material          = BuildMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        r.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    private Color Bright(Color c) => new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a);

    private Material BuildMaterial()
    {
        var sh = Shader.Find(ShaderName);
        if (sh == null) { Debug.LogWarning("[SkydomeTransitionParticles] シェーダーが見つからない: " + ShaderName); return null; }
        _mat = new Material(sh);
        _mat.SetColor("_TintColor", Color.white);
        _mat.SetFloat("_Core", 0.0f);
        _mat.SetFloat("_Falloff", 2.4f);
        return _mat;
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

#if UNITY_EDITOR
    // 範囲をシーンビューに可視化（選択時のみ）
    private void OnDrawGizmosSelected()
    {
        Gizmos.color  = new Color(1f, 0.6f, 0.95f, 0.28f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, volumeSize);
    }

    // 編集中に発火確認できるコンテキストメニュー。
    [ContextMenu("Debug/▶ Play Burst (2s)")]
    private void DebugPlay()
    {
        EnsureBuilt();
        Play(2f);
    }
#endif
}
