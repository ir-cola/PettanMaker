/**===========================================================================

 File   : チュートリアルビルボード出現制御 [TutorialBillboard.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 WASD/SPACE/SHIFT/MOUSE_R などの操作説明ビルボードを、
 プレイヤーが近づいたときにスタンプ風のポップで出現させる。

 ・Proximity    : プレイヤーとの水平距離が appearDistance 以下で出現
 ・AfterCallout : ステージ開始バナー (シロウサギをつかまえろ！) が
                  消えた後に出現 (バナーが無い構成では接近で出る保険つき)
 ・イントロ演出 (StageIntroSequence) 中は出現しない
 ・一度出現したら出しっぱなし

===========================================================================**/

using System.Collections;
using UnityEngine;

/**===========================================================================
  チュートリアルビルボード出現制御
===========================================================================**/
[DisallowMultipleComponent]
public class TutorialBillboard : MonoBehaviour {

    public enum TriggerMode {
        Proximity,      // プレイヤー接近で出現
        AfterCallout    // 開始バナーが消えた後に出現
    }

    [Tooltip("出現条件")]
    [SerializeField] private TriggerMode mode = TriggerMode.Proximity;
    [Tooltip("出現する水平距離")]
    [SerializeField] private float appearDistance = 6f;
    [Tooltip("ポップ出現にかける時間（秒）")]
    [SerializeField] private float popDuration = 0.6f;

    private Transform _player;
    private Vector3 _baseScale;
    private bool _shown;

    void Awake() {
        ApplyRoundedPanel();                    // スケールを0にする前 (lossyScale が有効なうち) に
        _baseScale = transform.localScale;
        transform.localScale = Vector3.zero;    // 出現演出までは畳んでおく
    }

    /**=======================================================================
      背景パネル (ルートの SpriteRenderer = 半透明黒) のスプライトを
      「角丸 + 白アウトライン」を焼き込んだ実行時生成テクスチャへ差し替える。
      マテリアルは元の Sprite-Unlit のまま (カスタムシェーダーだと描画順や
      フォグ合成の相性でチカチカしたため、実績ある描画経路を維持する)。
      非一様スケールでも角が真円になるよう、SDF は「見た目 (ワールド)」空間で焼く。
    =======================================================================**/
    const int PanelTexHeight = 224;                 // 生成テクスチャの縦解像度
    static readonly Color PanelFill = new Color(0.03f, 0.025f, 0.06f, 0.86f);  // 半透明黒
    static readonly Color PanelOutline = new Color(1f, 1f, 1f, 1f);            // 白 (くっきり)

    Texture2D _panelTex;
    Sprite _panelSprite;

    void ApplyRoundedPanel() {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        Bounds b = sr.sprite.bounds;
        Vector3 s = transform.lossyScale;
        float sx = Mathf.Max(1e-4f, Mathf.Abs(s.x));
        float sy = Mathf.Max(1e-4f, Mathf.Abs(s.y));
        float ex = Mathf.Max(1e-4f, b.extents.x);   // 元スプライトのローカル半サイズ
        float ey = Mathf.Max(1e-4f, b.extents.y);
        float vx = ex * sx;                          // 見た目 (ワールド) の半サイズ
        float vy = ey * sy;

        // テクスチャはローカル縦横比で作る (PPU が縦横両方に一致する条件)
        int h = PanelTexHeight;
        int w = Mathf.Clamp(Mathf.RoundToInt(h * (ex / ey)), 8, 1024);

        // 見た目空間での角半径/縁太さ (px 換算は SDF 側で吸収)
        float radius = 0.28f * Mathf.Min(vx, vy);
        float outline = 0.11f * Mathf.Min(vx, vy);   // 白縁は太めでくっきり

        _panelTex = new Texture2D(w, h, TextureFormat.RGBA32, false) {
            name = "T_RoundedPanel (Auto)",
            wrapMode = TextureWrapMode.Clamp,
        };
        float aa = 1.5f * Mathf.Max(2f * vx / w, 2f * vy / h);   // 約1.5px のソフトエッジ
        for (int y = 0; y < h; y++) {
            float py = ((y + 0.5f) / h - 0.5f) * 2f * vy;        // 見た目空間の座標
            for (int x = 0; x < w; x++) {
                float px = ((x + 0.5f) / w - 0.5f) * 2f * vx;
                // 角丸矩形の SDF (見た目空間なので角は常に真円)
                float qx = Mathf.Abs(px) - (vx - radius);
                float qy = Mathf.Abs(py) - (vy - radius);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                        + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                float inside = 1f - Mathf.Clamp01((d + aa) / (2f * aa));            // d<0 で 1
                float inner = 1f - Mathf.Clamp01((d + outline + aa) / (2f * aa));    // 縁より内側で 1
                Color c = Color.Lerp(PanelOutline, PanelFill, inner);
                c.a *= inside;
                _panelTex.SetPixel(x, y, c);
            }
        }
        _panelTex.Apply(false, true);

        // 元スプライトとローカルサイズが一致する PPU で差し替え (ワールドサイズ不変)
        float ppu = w / (2f * ex);
        _panelSprite = Sprite.Create(_panelTex, new Rect(0, 0, w, h),
                                     PivotFromBounds(sr.sprite), ppu, 0,
                                     SpriteMeshType.FullRect);
        _panelSprite.name = "S_RoundedPanel (Auto)";
        sr.sprite = _panelSprite;
        // 塗り色・白縁は生成テクスチャへ焼き込み済み。SpriteRenderer 側に
        // 「半透明黒」などのティントが残っていると、白縁まで黒く沈み (縁が黒く見える)
        // BG も余計に薄く/暗くなる。ティントを白に戻して焼いた色をそのまま出す。
        sr.color = Color.white;
    }

    // 元スプライトのピボット (正規化) を維持する (中心以外のピボットでも位置がずれない)
    static Vector2 PivotFromBounds(Sprite s) {
        Rect r = s.rect;
        Vector2 p = s.pivot;
        return new Vector2(r.width > 0 ? p.x / r.width : 0.5f,
                           r.height > 0 ? p.y / r.height : 0.5f);
    }

    void OnDestroy() {
        if (_panelSprite != null) Destroy(_panelSprite);
        if (_panelTex != null) Destroy(_panelTex);
    }

    void Start() {
        var mover = FindObjectOfType<PlayerMover>();
        if (mover != null) _player = mover.transform;
    }

    void Update() {
        if (_shown) return;
        if (StageIntroSequence.InProgress) return;   // イントロ演出中は出さない
        if (ShouldShow()) {
            _shown = true;
            StartCoroutine(Pop());
        }
    }

    bool ShouldShow() {
        bool near = false;
        if (_player != null) {
            Vector3 d = _player.position - transform.position;
            d.y = 0f;
            near = d.sqrMagnitude <= appearDistance * appearDistance;
        }

        if (mode == TriggerMode.AfterCallout) {
            // バナーを見終わったタイミングで出す (接近不要)。
            if (StampTextBanner.AnyShownYet) return StampTextBanner.ActiveCount == 0;
            return near;    // バナーが出ない構成でも接近すれば出る
        }
        return near;
    }

    // ぽよんと弾む出現 (減衰バネ: 大きく膨らむ → 少し縮む → 落ち着く)
    IEnumerator Pop() {
        const float damping = 5f;               // 減衰の強さ (小さいほど揺れが残る)
        const float omega   = 3f * Mathf.PI;    // 振動量 (1.5往復)
        float t = 0f;
        while (t < popDuration) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / popDuration);
            float ease = 1f - Mathf.Exp(-damping * p) * Mathf.Cos(omega * p);
            transform.localScale = _baseScale * ease;
            yield return null;
        }
        transform.localScale = _baseScale;
    }
}
