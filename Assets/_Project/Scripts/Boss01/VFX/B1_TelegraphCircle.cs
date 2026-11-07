using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 攻撃の着弾予告に地面へ表示する円形テレグラフ。
/// 中心は透明で、外周へ向かって赤く濃くなるグラデーション円
/// （シェーダ PettanMaker/Boss_01/TelegraphCircle）を、シール分割グリッドへ張る。
///
/// 出現は従来どおり（フェードイン＋時計ワイプで着弾接近度を表示）。消滅時だけ「シールを剥がす」で見せる：
/// Sticker/World と同じ頂点カール（_Peel 0→1）でめくれ上げ、フェードして自壊する。
/// 剥がしに備え、メッシュは分割グリッド（StickerQuadMesh.Grid）＋ Sticker 座標系
/// （XY 平面・表 −Z を Euler(90,0,0) で寝かせる）を使う。出現中は _Peel=0（平ら）なので見た目は従来と同じ。
///
/// Spawn() で生成し、着弾したら Detonate()（＝剥がして自壊）、
/// 途中で消すだけなら Dismiss()（同じく剥がして自壊）。
/// 半径はワールド単位（m）で、ダメージ半径とは独立に見た目サイズだけを指定できる。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_TelegraphCircle : MonoBehaviour
{
    private const string ShaderName = "PettanMaker/Boss_01/TelegraphCircle";

    private const float GroundOffsetY = 0.05f; // 地面へのめり込み防止の浮かせ量
    private const float FadeInTime    = 0.15f; // 出現フェード時間（秒）

    // ---- 消滅時のシール剥がし（Sticker/World の頂点カールを流用。出現は従来どおり時計ワイプ） ----
    private const float PeelOutTime  = 0.50f; // 「剥がす」（_Peel 0→1→フェード）の時間（秒）
    private const float CurlRadius   = 0.24f; // カール半径（板ローカル。StickerPeelFX と同値）
    private const float FlashOutTime = 0.12f; // 剥がし冒頭の着弾フラッシュ余韻（秒）

    private static readonly int FadeId              = Shader.PropertyToID("_Fade");
    private static readonly int EdgeColorId         = Shader.PropertyToID("_EdgeColor");
    private static readonly int UrgencyId           = Shader.PropertyToID("_Urgency");
    private static readonly int UrgencyMinAlphaId   = Shader.PropertyToID("_UrgencyMinAlpha");
    private static readonly int UrgencyPulseBoostId = Shader.PropertyToID("_UrgencyPulseBoost");
    private static readonly int FlashId             = Shader.PropertyToID("_Flash");
    private static readonly int PeelId              = Shader.PropertyToID("_Peel");
    private static readonly int CurlRadiusId        = Shader.PropertyToID("_CurlRadius");
    private static readonly int DirXId              = Shader.PropertyToID("_DirX");
    private static readonly int DirYId              = Shader.PropertyToID("_DirY");
    private static readonly int HideUnpeeledId      = Shader.PropertyToID("_HideUnpeeled");

    private MeshRenderer          meshRenderer;
    private Material              ownedMaterial; // フォールバック生成時のみ破棄対象
    private MaterialPropertyBlock mpb;
    private Color                 edgeColor;
    private float                 elapsed;      // 出現フェード用タイマー
    private float                 urgency = 1f; // 着弾接近度 0..1。未設定なら常に最大の濃さ（単発予告用）
    private bool                  peeling;      // 剥がし（消滅）中。以後は自己で剥がれて自壊する
    private float                 peelElapsed;

    /// <summary>
    /// 指定位置に予告サークルを表示する。radius は見た目の半径（m）。
    /// edgeColor は外縁側の色（中心は透明。ハイライト色もこの色から作られる）。
    /// material が null ならシェーダから自動生成する。
    /// </summary>
    public static B1_TelegraphCircle Spawn(Vector3 worldPos, float radius, Color edgeColor, Material material = null)
    {
        var go = new GameObject("B1_TelegraphCircle");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト

        var self = go.AddComponent<B1_TelegraphCircle>();
        self.Init(worldPos, Mathf.Max(0.01f, radius), edgeColor, material);
        return self;
    }

    private void Init(Vector3 worldPos, float radius, Color edge, Material material)
    {
        edgeColor = edge;

        // 分割グリッド（XY 平面・表 −Z）を Euler(90,0,0) で地面へ寝かせる。
        // スケールは板 ±0.5 → 半径 r（直径 2r）へ写す。Z はカールの持ち上げ量。
        // Z（板の法線＝カールの持ち上げ）は半径ほど大きくすると剥がれが跳ね上がりすぎるため控えめにする。
        transform.position   = worldPos + Vector3.up * GroundOffsetY;
        transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        transform.localScale = new Vector3(2f * radius, 2f * radius, radius);

        BuildGridRenderer();

        meshRenderer.sharedMaterial = ResolveMaterial(material);
        if (meshRenderer.sharedMaterial == null) return;

        mpb = new MaterialPropertyBlock();
        mpb.SetColor(EdgeColorId, edgeColor);
        mpb.SetFloat(FadeId, 0f);            // 出現はフェードイン（従来どおり）
        mpb.SetFloat(PeelId, 0f);            // 出現時は平ら（密着）。剥がしは消滅時のみ
        mpb.SetFloat(HideUnpeeledId, 0f);    // 出現の接着ゲートは使わない（時計ワイプに戻す）
        mpb.SetFloat(CurlRadiusId, CurlRadius);
        mpb.SetFloat(DirXId, 1f);
        mpb.SetFloat(DirYId, 1f);
        meshRenderer.SetPropertyBlock(mpb);
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
            Debug.LogWarning("[B1_TelegraphCircle] シェーダ " + ShaderName + " が見つかりません。予告サークルを表示できません。");
            meshRenderer.enabled = false;
            return null;
        }

        ownedMaterial = new Material(sh);
        return ownedMaterial;
    }

    /// <summary>
    /// 着弾接近度 0..1 を設定する（毎フレーム呼んでよい）。
    /// 0=着弾はまだ先（薄く表示）、1=着弾直前（赤が濃く・明滅が速くなる）。
    /// 呼ばなければ常に 1（最大の濃さ）で表示される。
    /// </summary>
    public void SetUrgency(float value) => urgency = Mathf.Clamp01(value);

    /// <summary>
    /// 事前表示と最終表示の値差を設定する（生成直後に1回呼べばよい）。
    /// farAlpha   … 接近度0（まだ先）のときの濃さ倍率。小さいほど最終表示との差が大きい。
    /// pulseBoost … 接近度1（着弾直前）で明滅を何倍速にするか。1 で変化なし。
    /// </summary>
    public void SetUrgencyStyle(float farAlpha, float pulseBoost)
    {
        if (mpb == null) return; // シェーダ未発見で非表示のときは何もしない

        mpb.SetFloat(UrgencyMinAlphaId, Mathf.Clamp01(farAlpha));
        mpb.SetFloat(UrgencyPulseBoostId, Mathf.Max(1f, pulseBoost));
        meshRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// 着弾の瞬間に呼ぶ。一瞬の着弾フラッシュのあと、シールが剥がれるように
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

        // ---- 出現：フェードインしつつ、着弾接近度を時計ワイプ等へ反映（従来どおり） ----
        elapsed += Time.deltaTime;
        mpb.SetFloat(FadeId, Mathf.Clamp01(elapsed / FadeInTime));
        mpb.SetFloat(UrgencyId, urgency);
        mpb.SetFloat(FlashId, 0f);
        meshRenderer.SetPropertyBlock(mpb);
    }

    // 剥がし：_Peel 0→1 でめくれ上げ、後半でフェードして自壊する。冒頭だけ着弾フラッシュを残す。
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
