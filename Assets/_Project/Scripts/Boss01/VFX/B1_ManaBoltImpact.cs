using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// マナボルト（B1_ManaBolt）の着弾点で弾ける「魔力スパーク球」。
///
/// 落雷が地面へ届いた瞬間に、半透明の球を一瞬だけ膨らませ、その球面上から
/// 法線方向（中心から外へ）へ Spark パーティクルを撒き散らす。
/// ＝「魔力が着弾点から球状に爆ぜて弾ける」当たりの手応え。
/// パーティクルは ParticleSystem の Sphere シェイプ（シェルから放出）で球面へ乗せ、
/// 放射速度がそのまま球面の法線方向になる。RenderMode=Stretch で進行方向（＝法線）
/// へ伸びるので、スパークが外向きに刺さって見える。
///
/// 半透明球・Spark ともマテリアルは Inspector 差し込み前提。未設定なら
/// フォールバック（半透明球＝Unlit、Spark＝Sprites/Default）で色だけでも動く。
/// B1_ManaBolt / B1_GroundScarDecal と同じ自己完結・自己破棄パターン。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_ManaBoltImpact : MonoBehaviour
{
    /// <summary>着弾スパーク球1発ぶんのパラメータ。</summary>
    public struct Params
    {
        public Material sphereMaterial; // 半透明球。null で簡易フォールバック
        public Material sparkMaterial;  // Spark パーティクル（Kenney）。null で簡易フォールバック
        public Color    color;          // 球・スパークの色（紫の魔力推奨）
        public float    radius;         // 球の最大半径（m）。通常は AoE 半径に比例させて渡す
        public float    sphereDuration; // 球が膨らんで消えるまでの時間（秒）
        public int      sparkCount;     // スパーク数
        public float    sparkSpeed;     // スパークの飛び出し速度（m/s）
        public float    sparkSize;      // スパーク1粒の大きさ
        public float    sparkDuration;  // スパークの寿命（秒）
    }

    private Transform      dome;        // 地面に伏せた半透明の半球
    private MeshRenderer   domeMr;
    private MaterialPropertyBlock domeMpb;
    private bool           fadeByAlphaProp; // true=_Alpha(専用ドームシェーダ) / false=_BaseColor(フォールバック)
    private Color          domeBaseColor;   // _BaseColor フェード用の基準色
    private float          domeBaseAlpha;   // _Alpha フェード用の基準値
    private Material       ownedSphere; // フォールバック生成時のみ破棄対象
    private Material       ownedSpark;  // 同上
    private Params         p;
    private float          elapsed;
    private float          lifetime;    // 球・スパークのうち長い方＋余韻

    private static Mesh s_domeMesh; // 半径1の半球（底面 y=0・頂点 y=1）。全インスタンス共有

    private const float DomeAlpha = 0.7f; // フォールバック半球の不透明度（濃さ）

    /// <summary>着弾点に魔力スパーク球を1発発生させる。scale で半径・速度を倍化（フィニッシュ用）。</summary>
    public static B1_ManaBoltImpact Spawn(Vector3 pos, in Params src, float scale = 1f)
    {
        var go = new GameObject("B1_ManaBoltImpact"); // DontSave は付けない（残骸防止）
        go.transform.position = pos;
        var self = go.AddComponent<B1_ManaBoltImpact>();
        self.Init(src, Mathf.Max(0.1f, scale));
        return self;
    }

    private void Init(in Params src, float scale)
    {
        p = src;
        p.radius         = Mathf.Max(0.1f, p.radius) * scale;
        p.sphereDuration = Mathf.Max(0.05f, p.sphereDuration);
        p.sparkSpeed     = Mathf.Max(0f, p.sparkSpeed) * scale;
        p.sparkSize      = Mathf.Max(0.01f, p.sparkSize) * scale;
        p.sparkDuration  = Mathf.Max(0.05f, p.sparkDuration);
        p.sparkCount     = Mathf.Max(0, p.sparkCount);
        lifetime         = Mathf.Max(p.sphereDuration, p.sparkDuration) + 0.2f;

        BuildDome();
        BuildSparks();
    }

    // 地面に伏せた半透明の半球（ドーム）。底面が地面(y=0)に接し、頂点が上を向く。
    // 半径はちょうど AoE 半径（p.radius）に一致させる。膨らみながらフェードして消える。
    private void BuildDome()
    {
        var go = new GameObject("Dome");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero; // 着弾点＝地面。半球メッシュの底面がここに来る

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = DomeMesh();

        domeMr = go.AddComponent<MeshRenderer>();
        domeMr.shadowCastingMode    = ShadowCastingMode.Off;
        domeMr.receiveShadows       = false;
        domeMr.lightProbeUsage      = LightProbeUsage.Off;
        domeMr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        Material mat = ResolveSphereMaterial();
        domeMr.sharedMaterial = mat;

        // 以後は MaterialPropertyBlock でフェードする（割り当てマテリアルでも共有を汚さない）。
        // 専用ドームシェーダ(ManaDome)は _Alpha、フォールバック(URP Unlit)は _BaseColor で全体の濃さを持つ。
        fadeByAlphaProp = mat != null && mat.HasProperty("_Alpha");
        if (fadeByAlphaProp)
            domeBaseAlpha = mat.GetFloat("_Alpha");
        else
            domeBaseColor = (mat != null && mat.HasProperty("_BaseColor"))
                ? mat.GetColor("_BaseColor")
                : new Color(p.color.r, p.color.g, p.color.b, DomeAlpha);

        domeMpb = new MaterialPropertyBlock();
        dome = go.transform;
        dome.localScale = Vector3.zero;
    }

    private Material ResolveSphereMaterial()
    {
        if (p.sphereMaterial != null) return p.sphereMaterial;

        // フォールバック：URP Unlit を明示的に「半透明」設定にする（既定は不透明でアルファが効かない）。
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) { ownedSphere = null; return null; }

        var m = new Material(sh);
        Color c = p.color; c.a = DomeAlpha;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f); // Transparent
        if (m.HasProperty("_Blend"))    m.SetFloat("_Blend", 0f);   // Alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull"))     m.SetFloat("_Cull", 0f);    // 内側も見える両面
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        ownedSphere = m;
        return ownedSphere;
    }

    // 半径1の半球メッシュ（底面 y=0・頂点 y=1、法線は外向き）。両面表示するので底の蓋は張らない。
    private static Mesh DomeMesh()
    {
        if (s_domeMesh != null) return s_domeMesh;

        const int rings = 12;    // 緯度（底→頂点）
        const int segs  = 24;    // 経度
        var verts = new Vector3[(rings + 1) * (segs + 1)];
        var norms = new Vector3[verts.Length];
        var uvs   = new Vector2[verts.Length];
        var tris  = new int[rings * segs * 6];

        int vi = 0;
        for (int r = 0; r <= rings; r++)
        {
            float vlat  = r / (float)rings;                 // 0=底, 1=頂点 → V（這い上がり方向）
            float theta = vlat * (Mathf.PI * 0.5f);
            float y = Mathf.Sin(theta);
            float rad = Mathf.Cos(theta);
            for (int s = 0; s <= segs; s++)
            {
                float u   = s / (float)segs;                // 周方向 → U（回転方向）
                float phi = u * (Mathf.PI * 2f);
                Vector3 pos = new Vector3(rad * Mathf.Cos(phi), y, rad * Mathf.Sin(phi));
                verts[vi] = pos;
                norms[vi] = pos.normalized;
                uvs[vi]   = new Vector2(u, vlat);
                vi++;
            }
        }

        int ti = 0;
        int stride = segs + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = r * stride + s;
                int b = a + stride;
                tris[ti++] = a;     tris[ti++] = b;     tris[ti++] = a + 1;
                tris[ti++] = a + 1; tris[ti++] = b;     tris[ti++] = b + 1;
            }

        s_domeMesh = new Mesh { name = "B1_ManaBoltDome" };
        s_domeMesh.vertices  = verts;
        s_domeMesh.normals   = norms;
        s_domeMesh.uv        = uvs;
        s_domeMesh.triangles = tris;
        s_domeMesh.RecalculateBounds();
        return s_domeMesh;
    }

    // 球面のシェルから法線方向（中心→外）へ Spark を放出する ParticleSystem を組む。
    private void BuildSparks()
    {
        if (p.sparkCount <= 0) return;

        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.duration        = p.sparkDuration;
        main.startLifetime   = p.sparkDuration;
        main.startSpeed      = p.sparkSpeed;
        main.startSize       = p.sparkSize;
        main.startColor      = p.color;
        main.maxParticles    = Mathf.Max(1, p.sparkCount);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.15f; // ほんの少し落として撒き散らし感を出す

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.sparkCount) });

        // Sphere シェイプ＝球面（radiusThickness 0 でシェルのみ）から放出。
        // 放出速度が中心から外向き＝球面の法線方向になる。
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = p.radius;
        shape.radiusThickness = 0f;

        // 寿命で薄く消える。
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode        = ParticleSystemRenderMode.Stretch; // 進行方向（＝法線）へ伸ばす
        renderer.lengthScale       = 2.5f;
        renderer.velocityScale     = 0.1f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows    = false;
        renderer.sharedMaterial    = ResolveSparkMaterial();

        ps.Play();
    }

    private Material ResolveSparkMaterial()
    {
        if (p.sparkMaterial != null) return p.sparkMaterial;
        Shader sh = Shader.Find("Sprites/Default"); // 頂点カラーを反映する常設シェーダ
        ownedSpark = sh != null ? new Material(sh) : null;
        return ownedSpark;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        // 半球：前半で一気に AoE 半径まで膨らみ、後半でフェード。
        if (dome != null)
        {
            float st   = Mathf.Clamp01(elapsed / p.sphereDuration);
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(st / 0.3f)); // 前半30%で半径いっぱいまで
            // 半径1の半球メッシュを AoE 半径そのものへスケール（＝半球の半径 = AoE Radius）。
            dome.localScale = Vector3.one * (p.radius * grow);

            // MaterialPropertyBlock で全体の濃さをフェード（割り当てマテリアルでも共有を汚さない）。
            float fade = 1f - Mathf.Clamp01((st - 0.4f) / 0.6f);
            if (fadeByAlphaProp)
                domeMpb.SetFloat("_Alpha", domeBaseAlpha * fade);
            else
            {
                Color c = domeBaseColor; c.a = domeBaseColor.a * fade;
                domeMpb.SetColor("_BaseColor", c);
            }
            domeMr.SetPropertyBlock(domeMpb);
        }

        if (elapsed >= lifetime) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (ownedSphere != null) { B1_CinematicUtil.SafeDestroy(ownedSphere); ownedSphere = null; }
        if (ownedSpark  != null) { B1_CinematicUtil.SafeDestroy(ownedSpark);  ownedSpark  = null; }
    }
}
