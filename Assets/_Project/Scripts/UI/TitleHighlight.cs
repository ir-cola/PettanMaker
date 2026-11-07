/**===========================================================================

 File   : タイトル用プロシージャル・ハイライト [TitleHighlight.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 選択肢の下に敷くピンクの筆跡を「シェーダーでプロシージャル生成」する Graphic。

 - メッシュは UV 0..1 の単純なクワッドのみ。形(テーパー/塗り進み/シアー)は
   UI/TitleHighlightStroke シェーダーのフラグメントが計算する。
 - Fill (0..1): ローラーが左→右へ転がって塗った位置。TitleCursor が毎フレーム駆動。
 - テーパー: ローラー側(先端=右)ほど細い (WideHalf → ThinHalf)。
 - 色は Graphic.color (頂点カラー) を使う。
 - マテリアルはシェーダーから生成した非保存インスタンス (アセットを汚さない)。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class TitleHighlight : MaskableGraphic {

    [Tooltip("筆跡シェーダー。未設定なら Shader.Find(\"UI/TitleHighlightStroke\")。")]
    [SerializeField] private Shader strokeShader;

    [Header("形状パラメータ")]
    [Range(0f, 1f)]    [SerializeField] private float fill = 1f;
    [Range(0f, 0.5f)]  [SerializeField] private float wideHalf = 0.46f;   // 左側(太い)
    [Range(0f, 0.5f)]  [SerializeField] private float thinHalf = 0.15f;   // 右側(細い辺。0で点)
    [Range(0.0001f, 0.2f)] [SerializeField] private float softness = 0.03f;
    [Range(0.0001f, 0.3f)] [SerializeField] private float tipSoftness = 0.05f;
    [Range(-0.6f, 0.6f)]   [SerializeField] private float skew = 0f;
    [Range(-1.5f, 1.5f)]   [SerializeField] private float tailSlant = 0.3f;   // 平行四辺形の左右辺の傾き

    /// <summary>平行四辺形の傾き。ローラー位置の inset 計算に TitleCursor が参照する。</summary>
    public float TailSlant => tailSlant;

    [Header("4頂点のうようよ揺れ (常時, イージング)")]
    [Tooltip("各頂点の揺れ幅(px)。")]
    [SerializeField] private float wiggleAmp = 6f;
    [Tooltip("揺れの速さ。")]
    [SerializeField] private float wiggleFreq = 2.2f;
    [Tooltip("揺れの位相シード。別オブジェクト(影)と別々に動かすため変える。")]
    [SerializeField] private float wiggleSeed = 0f;

    [Header("ズレ影レイヤー (ペルソナ風重ね影)")]
    [Tooltip("重ねる影/マスクの色。")]
    [SerializeField] private Color shadowColor = Color.black;
    [Tooltip("影のUVオフセット(x:右+ / y:下-)。")]
    [SerializeField] private Vector2 shadowOffset = new Vector2(0.012f, -0.07f);

    private float animTime = 0f;

    // 4頂点それぞれに別位相を与えて、四角形が踊るように(うようよ)揺らす。
    private static readonly Vector2[] CornerPhase = {
        new Vector2(0.0f, 1.7f),
        new Vector2(2.1f, 3.9f),
        new Vector2(4.2f, 0.6f),
        new Vector2(5.5f, 2.8f),
    };

    private Material _mat;
    private static readonly int ID_Fill        = Shader.PropertyToID("_Fill");
    private static readonly int ID_WideHalf    = Shader.PropertyToID("_WideHalf");
    private static readonly int ID_ThinHalf    = Shader.PropertyToID("_ThinHalf");
    private static readonly int ID_Softness    = Shader.PropertyToID("_Softness");
    private static readonly int ID_TipSoftness = Shader.PropertyToID("_TipSoftness");
    private static readonly int ID_Skew        = Shader.PropertyToID("_Skew");
    private static readonly int ID_TailSlant   = Shader.PropertyToID("_TailSlant");
    private static readonly int ID_ShadowColor  = Shader.PropertyToID("_ShadowColor");
    private static readonly int ID_ShadowOffset = Shader.PropertyToID("_ShadowOffset");

    /// <summary>塗り進み 0..1。ローラーの転がり位置。</summary>
    public float Fill {
        get => fill;
        set {
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(v, fill)) return;
            fill = v;
            var m = EnsureMaterial();
            if (m != null) m.SetFloat(ID_Fill, fill);
        }
    }

    // 単色塗りなのでテクスチャは無し。
    public override Texture mainTexture => null;

    protected override void OnEnable() {
        base.OnEnable();
        EnsureMaterial();
        ApplyAll();
    }

    // 実行時生成した HideAndDontSave マテリアルは GC/UnloadUnusedAssets で回収されない。
    // 破棄(およびエディタのドメインリロード)時に明示的に Destroy しないとリークする。
    protected override void OnDestroy() {
        if (_mat != null) {
            if (Application.isPlaying) Destroy(_mat);
            else DestroyImmediate(_mat);
            _mat = null;
        }
        base.OnDestroy();
    }

    private Material EnsureMaterial() {
        if (_mat == null) {
            Shader s = strokeShader != null ? strokeShader : Shader.Find("UI/TitleHighlightStroke");
            if (s == null) return null;
            _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (material != _mat) material = _mat;   // Graphic に筆跡マテリアルを使わせる
        return _mat;
    }

    private void ApplyAll() {
        var m = EnsureMaterial();
        if (m == null) return;
        m.SetFloat(ID_Fill, fill);
        m.SetFloat(ID_WideHalf, wideHalf);
        m.SetFloat(ID_ThinHalf, thinHalf);
        m.SetFloat(ID_Softness, softness);
        m.SetFloat(ID_TipSoftness, tipSoftness);
        m.SetFloat(ID_Skew, skew);
        m.SetFloat(ID_TailSlant, tailSlant);
        m.SetColor(ID_ShadowColor, shadowColor);
        m.SetVector(ID_ShadowOffset, new Vector4(shadowOffset.x, shadowOffset.y, 0f, 0f));
    }

    private void Update() {
        if (!Application.isPlaying) return;   // 揺れはプレイ時のみ駆動
        animTime += Time.unscaledDeltaTime;
        SetVerticesDirty();                   // 毎フレーム再構築して4頂点をうようよ動かす
    }

    /// <summary>
    /// 矩形を作る4頂点のみ。各頂点に別位相のサイン(=滑らかなイージング)をかけて
    /// 常時うようよと揺らす。辺は直線のまま四角形ごと踊る。形はシェーダー任せ。
    /// </summary>
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Color32 c = color;

        Vector2[] pos = {
            new Vector2(r.xMin, r.yMin),   // BL
            new Vector2(r.xMin, r.yMax),   // TL
            new Vector2(r.xMax, r.yMax),   // TR
            new Vector2(r.xMax, r.yMin),   // BR
        };
        Vector2[] uv = {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
        };

        if (Application.isPlaying && wiggleAmp != 0f) {
            for (int k = 0; k < 4; k++) {
                Vector2 ph = CornerPhase[k];
                pos[k].x += wiggleAmp * Mathf.Sin(animTime * wiggleFreq         + ph.x + wiggleSeed);
                pos[k].y += wiggleAmp * Mathf.Sin(animTime * wiggleFreq * 0.83f + ph.y + wiggleSeed);
            }
        }

        for (int k = 0; k < 4; k++) vh.AddVert(new Vector3(pos[k].x, pos[k].y, 0f), c, uv[k]);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

#if UNITY_EDITOR
    protected override void OnValidate() {
        base.OnValidate();
        ApplyAll();
        SetVerticesDirty();
    }
#endif
}
