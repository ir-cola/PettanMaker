using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// マナバーストの各着弾で空から落ちる紫の魔力の雷「マナボルト」。
///
/// 既存の地面爆発（B1_ManaBurstCross）はそのままに、着弾点へ上空から
/// ギザギザの雷を一撃落とす（水平演出だけだった爆発に縦の軸を足す）。
/// 雷の閃光が消えたあと、その "同じ雷の形" のままマテリアルを
/// 歪み（屈折）へ差し替えて一定時間残し、フェードして自壊する
/// ＝「空から地へ裂けた傷（空間の裂け目）」。チェシャ猫が空間を裂いて
/// 出入りするモチーフに寄せ、稲妻そのものではなく魔力の裂傷として見せる。
///
/// 「立ての雷」を成立させるため、LineRenderer のカメラ追従ビルボードではなく
/// Mesh を生成する：縦にジグザグする折れ線に沿って直交する2枚のリボン（十字断面）
/// を張り、どの水平角度から見ても立体的な縦の雷に見せる。生成メッシュを
/// 相をまたいで使い回すので、雷と裂け目の形は必ず一致する：
///   ・Strike 相 : boltMaterial で明るく発光、太さのちらつきで一瞬の一撃
///   ・Rift   相 : riftMaterial（屈折推奨）へ差し替え、少し太らせて背後を歪ませ、
///                 riftDuration かけて細く・薄くフェードして消える
/// マテリアルは Inspector 差し込み前提。未設定なら Sprites/Default の簡易
/// フォールバックで色だけでも見えるようにする（B1_GroundScarDecal と同思想）。
///
/// B1_GroundScarDecal / B1_ImpactLight と同じ自己完結・自己破棄パターン。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_ManaBolt : MonoBehaviour
{
    /// <summary>雷1本ぶんのパラメータ。</summary>
    public struct Params
    {
        public Material boltMaterial;   // 雷本体（発光/Kenney）。null で簡易フォールバック
        public Material riftMaterial;   // 着弾後に残る裂け目（屈折/歪み）。null で簡易フォールバック
        public Color    color;          // 雷・裂け目の色（紫の魔力推奨）
        public float    height;         // 上空からの落下高さ（m）
        public float    width;          // 雷の太さ（m）
        public float    strikeDuration; // 閃光が残る時間（秒）
        public float    riftDuration;   // 裂け目が残る時間（秒）
    }

    private const int   MinSegments = 6;
    private const int   MaxSegments = 24;
    private const float JitterFalloff = 0.5f; // 雷の中腹ほど横へ振れる量の係数

    private MeshFilter   mf;
    private MeshRenderer mr;
    private Mesh         mesh;        // 動的生成。OnDestroy で破棄する
    private Material     ownedBolt;   // フォールバック生成時のみ破棄対象
    private Material     ownedRift;   // 同上
    private Material     riftSource;  // Rift 相で使う元マテリアル（差し替え用）

    // 折れ線の中心（相をまたいで不変＝形が一致）と、断面を張る水平基底。
    private Vector3[] center;
    private float[]   profile; // 各点の太さプロファイル（0..1。上太・下細）
    private Vector3   axisRight;
    private Vector3   axisForward;

    private Vector3[] verts;
    private Color[]   colors;
    private Vector2[] uvs;
    private int[]     tris;

    private Params p;
    private float  elapsed;
    private bool   inRift;
    private float  baseHalfWidth;

    /// <summary>着弾点(groundPos)へ上空から雷を1本落とす。scale で太さ・高さを倍化（フィニッシュ用）。</summary>
    public static B1_ManaBolt Spawn(Vector3 groundPos, in Params src, float scale = 1f)
    {
        // DontSave は付けない（Playモード終了時に残骸が残るため）。演出GOは通常破棄に任せる。
        var go = new GameObject("B1_ManaBolt");
        go.transform.position = groundPos;
        var self = go.AddComponent<B1_ManaBolt>();
        self.Init(src, Mathf.Max(0.1f, scale));
        return self;
    }

    private void Init(in Params src, float scale)
    {
        p = src;
        p.height         = Mathf.Max(1f, p.height) * scale;
        p.width          = Mathf.Max(0.02f, p.width) * scale;
        p.strikeDuration = Mathf.Max(0.02f, p.strikeDuration);
        p.riftDuration   = Mathf.Max(0.05f, p.riftDuration);
        baseHalfWidth    = p.width * 0.5f;

        BuildPolyline();
        BuildMesh();
        BuildRenderer();
        RebuildVertices(1f);            // 初期の太さ
        ApplyStrikeColor();
    }

    // 上空から着弾点へ、中腹ほど横へ振れるギザギザの折れ線を作る。
    // 両端は着弾点と真上に固定（sinで端の振れを0に）し、雷が着弾点へ正確に落ちるようにする。
    private void BuildPolyline()
    {
        int segments = Mathf.Clamp(Mathf.RoundToInt(p.height * 1.2f), MinSegments, MaxSegments);
        center  = new Vector3[segments + 1];
        profile = new float[segments + 1];

        Vector3 top    = Vector3.up * p.height; // ローカル原点＝着弾点
        Vector3 ground = Vector3.zero;
        float   jitter = Mathf.Min(1.5f, p.height * 0.12f);

        // 横振れの平面基底（毎回ランダムな向きの縦割れに見せる）。
        float ang = Random.Range(0f, Mathf.PI * 2f);
        axisRight   = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        axisForward = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));

        for (int i = 0; i <= segments; i++)
        {
            float f = i / (float)segments;             // 0=真上, 1=着弾点
            Vector3 basePt = Vector3.Lerp(top, ground, f);
            float taper = Mathf.Sin(f * Mathf.PI);      // 両端0・中腹最大
            Vector2 r = Random.insideUnitCircle * (jitter * taper * JitterFalloff);
            center[i]  = basePt + axisRight * r.x + axisForward * r.y;
            // 上空側を細く・着弾点を最も太く。以前は着弾点ですぼめていたため
            // 地面に届く前に消えて見えていた。ここを反転して地面まで太く残す。
            profile[i] = Mathf.Lerp(0.55f, 1.15f, f);
        }
    }

    // 十字断面：直交する2枚のリボン（axisRight 面 / axisForward 面）を折れ線に沿って張る。
    // これで横のどの角度から見ても立体的な縦の雷になる。頂点は毎フレーム太さを更新するので
    // ここでは配列だけ確保し、トポロジ（三角形・UV）を固定で作る。
    private void BuildMesh()
    {
        int n = center.Length;
        verts  = new Vector3[n * 4];       // 点ごとに R±, F± の4頂点
        colors = new Color[n * 4];
        uvs    = new Vector2[n * 4];
        tris   = new int[(n - 1) * 2 * 6]; // リボン2枚 × 1区間2三角形

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);  // 長さ方向 → テクスチャU（spark_07 の横ストリークが長手に流れる）
            int v = i * 4;
            // 断面幅方向 → V。spark_07 の明るい芯（V=0.5）がリボン中心に来る。
            uvs[v + 0] = new Vector2(t, 0f); // R-
            uvs[v + 1] = new Vector2(t, 1f); // R+
            uvs[v + 2] = new Vector2(t, 0f); // F-
            uvs[v + 3] = new Vector2(t, 1f); // F+
        }

        int ti = 0;
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 4;
            int b = (i + 1) * 4;
            // リボンA（R面）: a+0,a+1,b+1,b+0
            AddQuad(ref ti, a + 0, a + 1, b + 1, b + 0);
            // リボンB（F面）: a+2,a+3,b+3,b+2
            AddQuad(ref ti, a + 2, a + 3, b + 3, b + 2);
        }

        mesh = new Mesh { name = "B1_ManaBoltMesh" };
        mesh.MarkDynamic();
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
    }

    private void AddQuad(ref int ti, int v0, int v1, int v2, int v3)
    {
        tris[ti++] = v0; tris[ti++] = v1; tris[ti++] = v2;
        tris[ti++] = v0; tris[ti++] = v2; tris[ti++] = v3;
    }

    private void BuildRenderer()
    {
        mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        mr = gameObject.AddComponent<MeshRenderer>();
        mr.shadowCastingMode   = ShadowCastingMode.Off;
        mr.receiveShadows      = false;
        mr.lightProbeUsage     = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        riftSource = p.riftMaterial;
        mr.sharedMaterial = ResolveMaterial(p.boltMaterial, ref ownedBolt);
    }

    // 差し替えマテリアルはそのまま、null なら Sprites/Default で色だけでも見えるフォールバックを作る。
    private Material ResolveMaterial(Material source, ref Material owned)
    {
        if (source != null) return source;
        Shader sh = Shader.Find("Sprites/Default"); // 頂点カラーを反映する常設シェーダ
        owned = sh != null ? new Material(sh) : null;
        return owned;
    }

    // 中心線＋太さプロファイルから、現在の太さ倍率で断面頂点を張り直す。
    private void RebuildVertices(float widthMul)
    {
        float hw = baseHalfWidth * widthMul;
        for (int i = 0; i < center.Length; i++)
        {
            Vector3 c = center[i];
            float w = hw * profile[i];
            int v = i * 4;
            verts[v + 0] = c - axisRight   * w;
            verts[v + 1] = c + axisRight   * w;
            verts[v + 2] = c - axisForward * w;
            verts[v + 3] = c + axisForward * w;
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
    }

    private void ApplyStrikeColor() => SetColor(p.color, 1f);

    private void SetColor(Color c, float alpha)
    {
        c.a = alpha;
        for (int i = 0; i < colors.Length; i++) colors[i] = c;
        mesh.colors = colors;
    }

    // Rift 相へ移行：同じメッシュのまま屈折マテリアルへ差し替え、少し太らせて背後を歪ませる。
    private void EnterRift()
    {
        inRift  = true;
        elapsed = 0f;
        mr.sharedMaterial = ResolveMaterial(riftSource, ref ownedRift);
        // 屈折マテリアルが無いフォールバック時は暗い紫の残り裂けとして見せる。
        if (riftSource == null) SetColor(p.color * 0.6f, 0.7f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        if (!inRift)
        {
            // 閃光：太さを小刻みに揺らして電撃のちらつきを出す。
            float flick = 0.75f + Random.value * 0.5f;
            RebuildVertices(flick);
            if (elapsed >= p.strikeDuration) EnterRift();
            return;
        }

        // 裂け目：riftDuration かけて細く・薄くフェードして消える。
        float t = Mathf.Clamp01(elapsed / p.riftDuration);
        float k = 1f - t;
        RebuildVertices(1.25f * k); // 裂傷として少し太らせてから細く
        if (riftSource == null) SetColor(p.color * 0.6f, 0.7f * k);

        if (t >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (mesh != null)      { B1_CinematicUtil.SafeDestroy(mesh);      mesh = null; }
        if (ownedBolt != null) { B1_CinematicUtil.SafeDestroy(ownedBolt); ownedBolt = null; }
        if (ownedRift != null) { B1_CinematicUtil.SafeDestroy(ownedRift); ownedRift = null; }
    }
}
