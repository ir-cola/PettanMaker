/**===========================================================================

 File   : シーン内 光の粒子 [SceneLightParticles.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/03
 -----------------------------------------------------------------------------

   ステージ空間にふわふわ漂う光の粒子（ホコリ/花粉/蛍のような）を飛ばす。
   ワールド空間シミュレーションなので、カメラやプレイヤーが動いても粒子は
   その場の空気中に留まって漂う。ParticleSystem をコードで構築し、加算発光
   シェーダー(Environment/LightParticle)の光点を上昇＋乱流＋明滅で見せる。
   範囲・量・色などは Inspector で調整できる。

 ===========================================================================**/
using UnityEngine;

[DisallowMultipleComponent]
public class SceneLightParticles : MonoBehaviour
{
    [Header("漂う範囲（ワールド, この Transform 中心のボックス）")]
    [SerializeField] private Vector3 volumeSize = new Vector3(46f, 10f, 55f);

    [Header("量・大きさ・寿命")]
    [SerializeField] private int     maxParticles = 260;
    [SerializeField] private float   emissionRate = 30f;
    [SerializeField] private Vector2 sizeRange    = new Vector2(0.14f, 0.42f);
    [SerializeField] private Vector2 lifetime     = new Vector2(7f, 13f);

    [Header("動き")]
    [SerializeField] private float riseSpeed      = 0.35f;   // ゆっくり上昇
    [SerializeField] private float driftSpeed     = 0.22f;   // 横のたゆたい
    [SerializeField] private float turbulence     = 0.40f;   // ノイズ乱流の強さ
    [SerializeField] private float turbulenceFreq = 0.22f;

    [Header("色（加算発光。1超でよりまぶしく＝ブルームで光る）")]
    [SerializeField] private Color colorWarm = new Color(1.00f, 0.96f, 0.82f, 1f); // 暖白
    [SerializeField] private Color colorCool = new Color(0.78f, 0.90f, 1.00f, 1f); // 淡青
    [SerializeField, Range(0.2f, 6f)] private float brightness = 2.6f;  // >1.1 でブルーム発光

    [Header("カメラ追従（視線の先で湧かせる）")]
    [Tooltip("ON でカメラの向いている先へ湧き範囲が追従する。イントロのカメラ回しでも" +
        "画面内に粒子が漂う (OFF だと初期位置=プレイヤー周辺にしか湧かない)")]
    [SerializeField] private bool followCameraView = true;
    [Tooltip("カメラ前方どれだけ先を範囲の中心にするか (m)")]
    [SerializeField] private float viewAheadDistance = 14f;
    [Tooltip("カット等で範囲がこの距離以上飛んだら、即時にバースト充填する (m)")]
    [SerializeField] private float warpDistance = 10f;
    [Tooltip("バースト充填の粒子数 (通常湧きは数秒かかるため、カット直後の空白を埋める)")]
    [SerializeField] private int warpFillBurst = 90;

    private const string ShaderName = "Environment/LightParticle";
    private Material _mat;
    private ParticleSystem _ps;
    private Vector3 _lastFollowPos = new Vector3(float.NaN, 0f, 0f);

    private void Awake() { Build(); }

    private void OnEnable()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps != null) ps.Play();
    }

    // カメラの視線の先へ湧き範囲を追従させる。シミュレーションはワールド空間なので、
    // 既存の粒子はその場に残り、新しい粒子だけが視界の先で湧く (違和感が出ない)。
    // カット (イントロのショット切替等) で大きく飛んだ場合はバーストで即時充填する。
    private void LateUpdate()
    {
        if (!followCameraView) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.transform.position + cam.transform.forward * viewAheadDistance;
        transform.position = p;

        bool warped = float.IsNaN(_lastFollowPos.x)
                   || (p - _lastFollowPos).sqrMagnitude > warpDistance * warpDistance;
        if (warped)
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            if (_ps != null && warpFillBurst > 0) _ps.Emit(warpFillBurst);
        }
        _lastFollowPos = p;
    }

    private void Build()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps == null) ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake の即再生を止めてから構築

        // --- Main ---
        var main = ps.main;
        main.loop            = true;
        main.duration        = 12f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed      = 0f;   // 速度は velocity/noise で与える
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startColor      = new ParticleSystem.MinMaxGradient(Bright(colorWarm), Bright(colorCool));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // ★ ワールド配置（スクリーンではない）
        main.maxParticles    = maxParticles;
        main.playOnAwake     = true;
        // prewarm は「duration 秒ぶんを1フレームで同期シミュレート」して初期充填するため、
        // 入場時に重いスパイクを生む。切って、数秒かけて自然に湧かせる（負荷を平滑化）。
        main.prewarm         = false;

        // --- Emission ---
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = emissionRate;

        // --- Shape: ボックス内から湧く ---
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = volumeSize;
        shape.randomDirectionAmount = 0f;

        // --- Velocity: ゆっくり上昇＋横のたゆたい（ワールド軸） ---
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.4f, riseSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);

        // --- Noise: 空気のゆらぎ ---
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = turbulence;
        noise.frequency   = turbulenceFreq;
        noise.scrollSpeed = 0.12f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;
        noise.damping     = true;

        // --- Color over Lifetime: ふわっと現れ、ふわっと消える（明滅） ---
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.22f),
                new GradientAlphaKey(1f, 0.72f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        // --- Size over Lifetime: わずかに脈動 ---
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.7f)));

        // --- Renderer: 加算発光のビルボード ---
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode        = ParticleSystemRenderMode.Billboard;
        r.alignment         = ParticleSystemRenderSpace.View;
        r.sortMode          = ParticleSystemSortMode.None;
        r.material          = BuildMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        r.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;

        ps.Play();
    }

    private Color Bright(Color c) => new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a);

    private Material BuildMaterial()
    {
        var sh = Shader.Find(ShaderName);
        if (sh == null) { Debug.LogWarning("[SceneLightParticles] シェーダーが見つからない: " + ShaderName); return null; }
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
        Gizmos.color  = new Color(1f, 0.95f, 0.6f, 0.28f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, volumeSize);
    }
#endif
}
