using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 出現時のRoar衝撃波。設定に応じてカメラ向きの画面歪みQuad、または
/// ボスを中心に膨張する発光Sphereを生成する。_Progress を0→1で進め、尺が終わると自壊する。
///
/// [ExecuteAlways] + EditorApplication.update により、Edit モードのカメラ
/// プレビュー中でも動作する。時間は Play=unscaledDeltaTime / Edit=realtime 差分。
/// メッシュ(Unity 標準 Quad)とマテリアルは実行時生成するのでプレハブ不要。
/// 描画には URP の Opaque Texture(SampleSceneColor)が必要。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class B1_ShockwaveDistortion : MonoBehaviour
{
    /// <summary>1波ぶんの見た目パラメータ。</summary>
    public struct Params
    {
        public float duration;      // 寿命(秒)。小さいほど速い
        public float strength;      // 歪みの最大量
        public float startScale;    // 開始スケール
        public float endScale;      // 最大スケール
        public float ringWidth;     // リング幅
        public Color rimColor;      // 縁の発光色
        public float rimIntensity;  // 縁の発光強度
        public bool use3D;          // true=Sphere、false=カメラ向きQuad
        public Material material;   // 3D用差し替えMaterial
        public Vector3 scale3D;     // Sphereの軸別倍率
        public float refraction;    // 3D時：>0 でシーン屈折シェーダ(ShockwaveRefract3D)を使い、背後を歪ませる量。0=発光のみ(RoarWave3D)

        public static Params Default => new Params
        {
            duration = 0.6f, strength = 0.035f,
            startScale = 2.5f, endScale = 11f,
            ringWidth = 0.16f,
            rimColor = new Color(0.82f, 0.6f, 1f, 1f), rimIntensity = 0.6f,
            use3D = true, scale3D = new Vector3(1f, 0.65f, 1f),
            refraction = 0f,
        };
    }

    private const string ShaderName = "PettanMaker/Boss_01/ScreenShockwave";
    private const string ShaderName3D = "PettanMaker/Boss_01/RoarWave3D";
    private const string ShaderName3DRefract = "PettanMaker/Boss_01/ShockwaveRefract3D";
    private static readonly int ProgressID     = Shader.PropertyToID("_Progress");
    private static readonly int StrengthID     = Shader.PropertyToID("_Strength");
    private static readonly int RingWidthID    = Shader.PropertyToID("_RingWidth");
    private static readonly int RimColorID      = Shader.PropertyToID("_RimColor");
    private static readonly int RimIntensityID  = Shader.PropertyToID("_RimIntensity");
    private static readonly int RefractionID    = Shader.PropertyToID("_Refraction");

    private Material mat;
    private Params p;
    private float elapsed;
    private Transform cam;
    private bool is3D;
    private float lastRealtime;

    /// <summary>指定位置に衝撃波を1波発生させる。シェーダが見つからなければ null。</summary>
    public static B1_ShockwaveDistortion Spawn(Vector3 pos, in Params p)
    {
        bool spawn3D = p.use3D;
        // 3D かつ屈折量>0 なら、背後のシーンを歪ませる屈折シェルシェーダを使う。
        string name3D = p.refraction > 0f ? ShaderName3DRefract : ShaderName3D;
        var shader = p.material != null ? p.material.shader : Shader.Find(spawn3D ? name3D : ShaderName);
        if (shader == null)
        {
            Debug.LogWarning("[B1_ShockwaveDistortion] Shader not found; shockwave skipped.");
            return null;
        }

        var go = GameObject.CreatePrimitive(spawn3D ? PrimitiveType.Sphere : PrimitiveType.Quad);
        go.name = spawn3D ? "B1_RoarWave3D" : "B1_ShockwaveDistortion";
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト
        var col = go.GetComponent<Collider>();
        if (col != null) B1_CinematicUtil.SafeDestroy(col);
        go.transform.position = pos;

        var self = go.AddComponent<B1_ShockwaveDistortion>();
        self.Init(shader, p);
        return self;
    }

    private void Init(Shader shader, in Params src)
    {
        p = src;
        is3D = p.use3D;
        p.duration = Mathf.Max(0.05f, p.duration);
        p.strength = Mathf.Max(0f, p.strength);
        p.ringWidth = Mathf.Clamp(p.ringWidth, 0.02f, 0.6f);

        mat = p.material != null ? new Material(p.material) : new Material(shader);
        mat.SetFloat(ProgressID, 0f);
        mat.SetFloat(StrengthID, p.strength);
        mat.SetFloat(RingWidthID, p.ringWidth);
        mat.SetColor(RimColorID, p.rimColor);
        mat.SetFloat(RimIntensityID, p.rimIntensity);
        if (mat.HasProperty(RefractionID)) mat.SetFloat(RefractionID, p.refraction);

        var mr = GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;

        var main = Camera.main;
        cam = main != null ? main.transform : null;
        elapsed = 0f;
        if (p.scale3D == Vector3.zero) p.scale3D = Vector3.one;
        transform.localScale = ScaleFor(p.startScale);
    }

    // 1波の進行。dt はモードに応じた経過秒。
    private void Tick(float dt)
    {
        if (mat == null) return; // Init 前

        if (cam == null)
        {
            var main = Camera.main;
            cam = main != null ? main.transform : null;
        }
        if (!is3D && cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);

        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / p.duration);
        mat.SetFloat(ProgressID, t);
        transform.localScale = ScaleFor(Mathf.Lerp(p.startScale, p.endScale, t));

        if (t >= 1f)
        {
            if (mat != null) { B1_CinematicUtil.SafeDestroy(mat); mat = null; }
            B1_CinematicUtil.SafeDestroy(gameObject);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return; // Edit は EditorUpdate 駆動
        Tick(Time.unscaledDeltaTime);
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            lastRealtime = Time.realtimeSinceStartup;
            UnityEditor.EditorApplication.update += EditorUpdate;
        }
    }

    private void OnDisable()
    {
        UnityEditor.EditorApplication.update -= EditorUpdate;
    }

    private void EditorUpdate()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= EditorUpdate; return; }
        if (Application.isPlaying) return;

        float now = Time.realtimeSinceStartup;
        float dt = Mathf.Clamp(now - lastRealtime, 0f, 0.1f);
        lastRealtime = now;
        Tick(dt);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
#endif

    /// <summary>演出が途中で中断されても確実に消すための強制破棄。</summary>
    public void Kill()
    {
        if (mat != null) { B1_CinematicUtil.SafeDestroy(mat); mat = null; }
        B1_CinematicUtil.SafeDestroy(gameObject);
    }

    private Vector3 ScaleFor(float scale) => is3D
        ? Vector3.Scale(Vector3.one * scale, p.scale3D)
        : Vector3.one * scale;

    private void OnDestroy()
    {
        if (mat != null) { B1_CinematicUtil.SafeDestroy(mat); mat = null; }
    }
}
