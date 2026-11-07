using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 突進などの直線攻撃の予告に地面へ表示する帯状テレグラフ。
/// チェック柄の明滅や輪郭線などの装飾はシェーダ（PettanMaker/Boss_01/TelegraphLine）が担当する。
///
/// 出現は従来どおり（フェードイン＋チャージワイプでタメ進行度を表示）。消滅時だけ「シールを剥がす」で見せる：
/// Sticker/World と同じ頂点カール（_Peel 0→1）で手前（ボス側）からめくれ上げ、フェードして自壊する。
/// 剥がしに備え、メッシュは分割グリッド（StickerQuadMesh.Grid）＋ Sticker 座標系（XY 平面・表 −Z）を使う。
/// 出現中は _Peel=0（平ら）なので見た目は従来と同じ。
///
/// Spawn() で生成し、SetLine() で毎フレーム経路（始点→終点）を更新できる。
/// 消すときは Detonate()（発射＝剥がして自壊）または Dismiss()（キャンセル＝剥がして自壊）。
/// 横幅はワールド単位（m）で、当たり判定とは独立に見た目サイズだけを指定できる。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_TelegraphLine : MonoBehaviour
{
    private const string ShaderName = "PettanMaker/Boss_01/TelegraphLine";

    private const float GroundOffsetY = 0.05f; // 地面へのめり込み防止の浮かせ量
    private const float FadeInTime    = 0.15f; // 出現フェード時間（秒）
    private const float MinLength     = 0.05f; // これ未満の長さでは向きが定まらないため非表示にする

    // ---- 消滅時のシール剥がし（Sticker/World の頂点カールを流用。出現は従来どおりチャージワイプ） ----
    private const float PeelOutTime  = 0.50f; // 「剥がす」（_Peel 0→1→フェード）の時間（秒）
    private const float CurlRadius   = 0.24f; // カール半径（板ローカル）
    private const float FlashOutTime = 0.12f; // 剥がし冒頭の発射フラッシュ余韻（秒）

    private static readonly int FadeId       = Shader.PropertyToID("_Fade");
    private static readonly int EdgeColorId  = Shader.PropertyToID("_EdgeColor");
    private static readonly int AspectId     = Shader.PropertyToID("_Aspect");
    private static readonly int UrgencyId    = Shader.PropertyToID("_Urgency");
    private static readonly int FlashId      = Shader.PropertyToID("_Flash");
    private static readonly int PeelId       = Shader.PropertyToID("_Peel");
    private static readonly int CurlRadiusId = Shader.PropertyToID("_CurlRadius");
    private static readonly int DirXId       = Shader.PropertyToID("_DirX");
    private static readonly int DirYId       = Shader.PropertyToID("_DirY");
    private static readonly int HideUnpeeledId = Shader.PropertyToID("_HideUnpeeled");

    private MeshRenderer          meshRenderer;
    private Material              ownedMaterial; // フォールバック生成時のみ破棄対象
    private MaterialPropertyBlock mpb;
    private float                 width;
    private float                 elapsed;      // 出現フェード用タイマー
    private float                 urgency = 1f; // タメ進行度 0..1。未設定なら常にチャージ満タン表示
    private bool                  peeling;      // 剥がし（消滅）中。以後は自己で剥がれて自壊する
    private float                 peelElapsed;

    /// <summary>
    /// 始点→終点の予告ラインを表示する。width は見た目の横幅（m）。
    /// edgeColor は基準色（横幅の中心は透明。輪郭線・チェック柄もこの色から作られる）。
    /// material が null ならシェーダから自動生成する。
    /// </summary>
    public static B1_TelegraphLine Spawn(Vector3 start, Vector3 end, float width, Color edgeColor, Material material = null)
    {
        var go = new GameObject("B1_TelegraphLine");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト
        go.transform.position = start;

        var self = go.AddComponent<B1_TelegraphLine>();
        self.Init(Mathf.Max(0.1f, width), edgeColor, material);
        self.SetLine(start, end);
        return self;
    }

    private void Init(float w, Color edge, Material material)
    {
        width = w;

        BuildGridRenderer();

        meshRenderer.sharedMaterial = ResolveMaterial(material);
        if (meshRenderer.sharedMaterial == null) return;

        mpb = new MaterialPropertyBlock();
        mpb.SetColor(EdgeColorId, edge);
        mpb.SetFloat(FadeId, 0f);            // 出現はフェードイン（従来どおり）
        mpb.SetFloat(PeelId, 0f);            // 出現時は平ら（密着）。剥がしは消滅時のみ
        mpb.SetFloat(HideUnpeeledId, 0f);    // 出現の接着ゲートは使わない（チャージワイプに戻す）
        mpb.SetFloat(CurlRadiusId, CurlRadius);
        mpb.SetFloat(DirXId, 0f);            // 剥がしは帯の長さ方向（板 Y）に沿う
        mpb.SetFloat(DirYId, -1f);           // 手前（ボス足元）側からめくれ上がる
        meshRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// 経路を更新する（毎フレーム呼んでよい）。高さは始点の Y に合わせて水平に張る。
    /// 追尾する予告（タメ中にプレイヤーを狙い続ける等）はこれで向き・長さを追従させる。
    /// </summary>
    public void SetLine(Vector3 start, Vector3 end)
    {
        if (meshRenderer == null || peeling) return; // 剥がし中は SetLine に transform を触らせない

        Vector3 dir = end - start;
        dir.y = 0f; // 地面に水平な帯として張る
        float length = dir.magnitude;

        if (length < MinLength)
        {
            meshRenderer.enabled = false; // 向きが定まらない瞬間だけ隠す
            return;
        }
        meshRenderer.enabled = true;
        dir /= length;

        // 分割グリッド（XY 平面・表 −Z）を Euler(90,0,0) で寝かせ、LookRotation で長さ方向を dir に向ける。
        // グリッドは中心原点なので位置は始点→終点の中点。スケールは X=横幅・Y=長さ・Z=カール持ち上げ量。
        Vector3 mid = (start + end) * 0.5f;
        mid.y = start.y + GroundOffsetY;
        // Z（板の法線＝カールの持ち上げ）は横幅の 0.6 倍ほどに抑えて、剥がれの跳ね上がりを控えめにする。
        transform.position   = mid;
        transform.rotation   = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
        transform.localScale = new Vector3(width, length, width * 0.6f);

        if (mpb != null)
        {
            mpb.SetFloat(AspectId, length / Mathf.Max(0.001f, width)); // チェックのセルを正方形に保つ
            meshRenderer.SetPropertyBlock(mpb);
        }
    }

    // カール可能な分割グリッド（共有メッシュ）を貼る。メッシュは共有なので破棄しない。
    private void BuildGridRenderer()
    {
        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = StickerQuadMesh.Grid;

        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode    = ShadowCastingMode.Off;
        meshRenderer.receiveShadows       = false;
        meshRenderer.lightProbeUsage      = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private Material ResolveMaterial(Material material)
    {
        if (material != null) return material; // 差し替えマテリアルはそのまま共有利用

        Shader sh = Shader.Find(ShaderName);
        if (sh == null)
        {
            Debug.LogWarning("[B1_TelegraphLine] シェーダ " + ShaderName + " が見つかりません。予告ラインを表示できません。");
            meshRenderer.enabled = false;
            return null;
        }

        ownedMaterial = new Material(sh);
        return ownedMaterial;
    }

    /// <summary>
    /// タメ進行度 0..1 を設定する（毎フレーム呼んでよい）。
    /// 0=タメ開始（手前だけチャージ）、1=発射直前（先端までチャージ満タン）。
    /// 呼ばなければ常に 1（満タン）で表示される。
    /// </summary>
    public void SetUrgency(float value) => urgency = Mathf.Clamp01(value);

    /// <summary>
    /// 突進開始の瞬間に呼ぶ。一瞬の発射フラッシュのあと、シールが剥がれるように
    /// めくれ上がってフェードして自壊する（呼び出し側で Destroy 不要）。
    /// 既に剥がし中なら何もしない。
    /// </summary>
    public void Detonate() => BeginPeel();

    /// <summary>
    /// 予告をキャンセルして消すときに呼ぶ。Detonate と同じくシールを剥がすように消える。
    /// </summary>
    public void Dismiss() => BeginPeel();

    private void BeginPeel()
    {
        if (peeling) return;
        peeling     = true;
        peelElapsed = 0f;
        urgency     = 1f; // 満タン状態で剥がれる

        if (mpb == null) Destroy(gameObject); // 非表示（シェーダ未発見）なら即消す
    }

    private void Update()
    {
        if (mpb == null) return;

        if (peeling) { UpdatePeel(); return; }

        // ---- 出現：フェードインしつつ、タメ進行度をチャージワイプへ反映（従来どおり） ----
        elapsed += Time.deltaTime;
        mpb.SetFloat(FadeId, Mathf.Clamp01(elapsed / FadeInTime));
        mpb.SetFloat(UrgencyId, urgency);
        mpb.SetFloat(FlashId, 0f);
        meshRenderer.SetPropertyBlock(mpb);
    }

    // 剥がし：_Peel 0→1 でめくれ上げ、後半でフェードして自壊する。冒頭だけ発射フラッシュを残す。
    private void UpdatePeel()
    {
        peelElapsed += Time.deltaTime;
        float k = Mathf.Clamp01(peelElapsed / PeelOutTime);

        float flash = Mathf.Clamp01(1f - peelElapsed / FlashOutTime);
        float fade  = 1f - Mathf.Clamp01((k - 0.55f) / 0.45f); // 後半でフェード

        mpb.SetFloat(PeelId, Mathf.SmoothStep(0f, 1f, k)); // 平ら(0)からめくれ上げ(1)
        mpb.SetFloat(UrgencyId, 1f);
        mpb.SetFloat(FlashId, flash);
        mpb.SetFloat(FadeId, fade);
        meshRenderer.SetPropertyBlock(mpb);

        if (k >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // メッシュは共有（StickerQuadMesh.Grid）なので破棄しない。生成した Material のみ破棄する。
        if (ownedMaterial != null) { B1_CinematicUtil.SafeDestroy(ownedMaterial); ownedMaterial = null; }
    }
}
