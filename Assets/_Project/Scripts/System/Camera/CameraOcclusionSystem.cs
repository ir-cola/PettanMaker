using System.Collections.Generic;
using UnityEngine;

/**===========================================================================

 File   : カメラオクルージョンシステム [CameraOcclusionSystem.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 カメラからターゲット(プレイヤー)へ Ray を飛ばし、間を遮るオブジェクトを
 ディザ透過でフェードさせる。対象マテリアルは _OcclusionFade (0=不透明,
 1=完全透過) を持つシェーダー (Custom/Toon 等) が必要。値は Renderer 単位で
 MaterialPropertyBlock 経由で書き込むため、マテリアルは共有のまま個別制御できる。
 ===========================================================================**/
[DisallowMultipleComponent]
public sealed class CameraOcclusionSystem : MonoBehaviour
{
    static readonly int OcclusionFadeId  = Shader.PropertyToID("_OcclusionFade");
    static readonly int HoleCenterId     = Shader.PropertyToID("_OccludeHoleCenter");
    static readonly int HoleRadiusId     = Shader.PropertyToID("_OccludeHoleRadius");
    static readonly int HoleShapeId      = Shader.PropertyToID("_OccludeHoleShape");
    static readonly int LineStartId      = Shader.PropertyToID("_OccludeLineStart");
    static readonly int LineEndId        = Shader.PropertyToID("_OccludeLineEnd");
    static readonly int AutoRadiusId     = Shader.PropertyToID("_OccludeAutoRadius");
    static readonly int AutoStrengthId   = Shader.PropertyToID("_OccludeAutoStrength");

    [Header("References")]
    [SerializeField] Camera    targetCamera;   // 未指定なら Camera.main を自動取得
    [SerializeField] Transform target;         // 遮蔽判定の終点。未指定なら Player タグを自動取得

    [Header("プレイヤー周辺の透過窓 (OccludablePolka 用)")]
    [Tooltip("プレイヤーのスクリーン位置を中心に、この半径 (画面高さ比) の窓内だけ水玉が開く。楕円の縦横は下の Stretch で調整")]
    [SerializeField, Range(0.05f, 1f)] float holeRadiusScreen = 0.38f;
    [Tooltip("穴の中心をプレイヤーの足元からどれだけ上にするか (m)。胴体中心に合わせる (1.0 だと頭に寄り、全身が全開ゾーンに入らない)")]
    [SerializeField] float holeHeightOffset = 0.6f;
    [Tooltip("窓（楕円）の横の広がり。大きいほど横長。縦(Y)より大きくすると横長楕円になる")]
    [SerializeField, Range(0.3f, 4f)] float holeStretchX = 2.0f;
    [Tooltip("窓（楕円）の縦の広がり。大きいほど縦長。横(X)より大きくすると縦長楕円になる")]
    [SerializeField, Range(0.3f, 4f)] float holeStretchY = 1.35f;
    [Tooltip("窓を画面下へずらす量（半径比）。プラスで下、マイナスで上")]
    [SerializeField, Range(-1f, 1f)] float holeDownOffset = 0.22f;

    [Header("自動シリンダー透過 (Collider 不要)")]
    [Tooltip("カメラ→プレイヤーを結ぶ円柱内のフラグメントをシェーダー側で自動フェードさせる半径 (m)。0 でオフ")]
    [SerializeField, Min(0f)] float autoOccludeRadius = 0.9f;
    [Tooltip("シリンダー始点/終点(カメラ/プレイヤー)の追従を時間平滑する速さ。小さいほど滑らか、大きいほど機敏。" +
        "カメラのカクつき(clampToGround のスナップ等)で壁が『バッと』瞬間透過するのを防ぐ。0 で平滑オフ(即時)")]
    [SerializeField, Min(0f)] float cylinderSmoothing = 16f;

    [Header("Detection")]
    [SerializeField] LayerMask occluderMask = ~0;                                     // 遮蔽対象レイヤー（プレイヤーは除外推奨）
    [SerializeField, Min(0f)]  float sphereRadius  = 0.3f;                            // 0 で Ray、>0 で SphereCast（太さ）
    [SerializeField, Min(0f)]  float targetPadding = 0.5f;                            // ターゲット手前で判定を打ち切る距離
    [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] float fadeAmount   = 0.75f;   // 遮蔽時の透過量（0=不透明, 1=完全透過）
    [SerializeField, Min(0.01f)]    float fadeInSpeed  = 8f;      // 透過へ向かう速度
    [SerializeField, Min(0.01f)]    float fadeOutSpeed = 4f;      // 不透明へ復帰する速度

    const int MaxHits = 32;
    readonly RaycastHit[] _hitBuffer = new RaycastHit[MaxHits];

    sealed class FadeEntry
    {
        public Renderer[] renderers;   // 対象 Collider に紐づく Renderer 群
        public float      fade;        // 現在の透過量
        public bool       occluding;   // 今フレーム遮蔽しているか
    }

    readonly Dictionary<Collider, FadeEntry> _entries    = new Dictionary<Collider, FadeEntry>(32);
    readonly List<Collider>                  _removeList = new List<Collider>(16);
    MaterialPropertyBlock _mpb;

    // シリンダー始点/終点の時間平滑用 (カメラのカクつき吸収)
    Vector3 _smoothLineStart, _smoothLineEnd;
    bool    _lineSmoothInit;

    void OnEnable()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        // 窓形状の初期値を送っておく (最初の描画で _OccludeHoleShape が 0 → 0除算になるのを防ぐ)
        Shader.SetGlobalVector(HoleShapeId,
            new Vector4(Mathf.Max(0.01f, holeStretchX), Mathf.Max(0.01f, holeStretchY), holeDownOffset, 0f));
    }

    void OnDisable()
    {
        // 追跡中の透過を全て元に戻してから破棄する
        foreach (var kv in _entries)
            ApplyFade(kv.Value, 0f);
        _entries.Clear();
        Shader.SetGlobalFloat(HoleRadiusId, 0f);   // 透過窓も閉じる
        Shader.SetGlobalFloat(AutoRadiusId, 0f);   // 自動シリンダー透過も止める
        _lineSmoothInit = false;                   // 再有効化時にシリンダー平滑を今の姿勢から初期化
    }

    void LateUpdate()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        if (targetCamera == null) targetCamera = Camera.main;

        // ターゲット未設定ならプレイヤーを自動取得 (未設定のまま何もしない事故を防ぐ)
        if (target == null) {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
        if (targetCamera == null || target == null)
        {
            Shader.SetGlobalFloat(HoleRadiusId, 0f);
            Shader.SetGlobalFloat(AutoRadiusId, 0f);
            return;
        }

        // 今フレームの遮蔽フラグをリセット
        foreach (var kv in _entries)
            kv.Value.occluding = false;

        DetectOccluders();
        UpdateFades();
        UpdateHoleWindow();
    }

    // プレイヤー周辺だけ水玉が開くよう、プレイヤーのスクリーン位置と半径を
    // グローバルシェーダー値で OccludablePolka へ渡す。
    // 併せてカメラ→プレイヤー胴体の線分をシェーダーへ渡し、Collider を持たない
    // オブジェクトでも円柱内に入ったフラグメントが自動フェードするようにする。
    void UpdateHoleWindow()
    {
        Vector3 targetPoint = target.position + Vector3.up * holeHeightOffset;
        Vector3 camPos      = targetCamera.transform.position;

        // カメラのカクつき (clampToGround のスナップ・ヒッチ等) がそのままシリンダーに乗ると、
        // 壁が「バッと」瞬間的に透過してしまう。始点/終点を指数平滑して急な移動を吸収し、
        // 壁が空間的になめらかにフェードイン/アウトするようにする。
        // dt はヒッチ時に平滑が無効化されるのを防ぐため上限クランプ (大きな1フレームで
        // シリンダーが一気に飛ぶのを抑える)。
        if (!_lineSmoothInit) { _smoothLineStart = camPos; _smoothLineEnd = targetPoint; _lineSmoothInit = true; }
        if (cylinderSmoothing > 0f) {
            float sdt = Mathf.Min(Time.deltaTime, 0.033f);
            float k   = 1f - Mathf.Exp(-cylinderSmoothing * sdt);
            _smoothLineStart = Vector3.Lerp(_smoothLineStart, camPos,      k);
            _smoothLineEnd   = Vector3.Lerp(_smoothLineEnd,   targetPoint, k);
        } else {
            _smoothLineStart = camPos;
            _smoothLineEnd   = targetPoint;
        }

        Shader.SetGlobalVector(LineStartId, _smoothLineStart);
        Shader.SetGlobalVector(LineEndId, _smoothLineEnd);
        Shader.SetGlobalFloat(AutoRadiusId, autoOccludeRadius);
        Shader.SetGlobalFloat(AutoStrengthId, fadeAmount);

        // 窓（楕円）の形状: x=横の広がり, y=縦の広がり, z=下げ量(半径比)。0除算防止に下限クランプ。
        Shader.SetGlobalVector(HoleShapeId,
            new Vector4(Mathf.Max(0.01f, holeStretchX), Mathf.Max(0.01f, holeStretchY), holeDownOffset, 0f));

        Vector3 sp = targetCamera.WorldToScreenPoint(targetPoint);
        if (sp.z <= 0f) {           // カメラの背後にいる場合は窓なし
            Shader.SetGlobalFloat(HoleRadiusId, 0f);
            return;
        }
        // 正規化スクリーン座標 (0..1) と正規化半径 (画面高さ比) で渡す。
        // シェーダー側で実レンダーターゲット px へ復元するため、URP レンダースケール
        // (Universal Render Pipeline Asset の RenderScale=1.5 等) があっても位置がズレない。
        float pw = Mathf.Max(1f, targetCamera.pixelWidth);
        float ph = Mathf.Max(1f, targetCamera.pixelHeight);
        Shader.SetGlobalVector(HoleCenterId, new Vector4(sp.x / pw, sp.y / ph, 0f, 0f));
        Shader.SetGlobalFloat(HoleRadiusId, holeRadiusScreen);
    }

    // カメラ→ターゲット間を Cast し、ヒットした Collider を遮蔽対象として登録する
    void DetectOccluders()
    {
        Vector3 origin   = targetCamera.transform.position;
        Vector3 toTarget = target.position - origin;
        float   full     = toTarget.magnitude;
        float   dist     = full - targetPadding;
        if (dist <= 0f) return;

        Vector3 dir = toTarget / full;

        int count = sphereRadius > 0f
            ? Physics.SphereCastNonAlloc(origin, sphereRadius, dir, _hitBuffer, dist, occluderMask, triggerInteraction)
            : Physics.RaycastNonAlloc(origin, dir, _hitBuffer, dist, occluderMask, triggerInteraction);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hitBuffer[i].collider;
            if (col == null) continue;

            // ターゲット (プレイヤー) 自身はフェードさせない
            if (col.transform.IsChildOf(target)) continue;

            // 敵はフェードさせない。白兎などがカメラ→プレイヤーの線分を横切るたびに
            // ディザで抜かれ、その穴にフォグが乗って「敵が消える」ように見えるため。
            // 遮蔽フェードは壁・柱など環境オブジェクト専用とする。
            if (col.GetComponentInParent<IEnemy>() != null) continue;

            if (!_entries.TryGetValue(col, out FadeEntry entry))
            {
                Renderer[] rends = col.GetComponentsInChildren<Renderer>(true);
                if (rends == null || rends.Length == 0) continue;
                entry = new FadeEntry { renderers = rends, fade = 0f };
                _entries.Add(col, entry);
            }
            entry.occluding = true;
        }
    }

    // 遮蔽状態に応じて透過量を補間し、追跡不要になったエントリを破棄する
    void UpdateFades()
    {
        _removeList.Clear();
        float dt = Time.deltaTime;

        foreach (var kv in _entries)
        {
            FadeEntry e = kv.Value;
            float targetFade = e.occluding ? fadeAmount   : 0f;
            float speed      = e.occluding ? fadeInSpeed  : fadeOutSpeed;
            e.fade = Mathf.MoveTowards(e.fade, targetFade, speed * dt);

            ApplyFade(e, e.fade);

            // 完全に復帰したものは追跡対象から外す
            if (!e.occluding && e.fade <= 0.0001f)
                _removeList.Add(kv.Key);
        }

        for (int i = 0; i < _removeList.Count; i++)
            _entries.Remove(_removeList[i]);
    }

    // 既存の MaterialPropertyBlock を保持したまま _OcclusionFade だけを上書きする
    void ApplyFade(FadeEntry e, float value)
    {
        if (e.renderers == null) return;
        for (int i = 0; i < e.renderers.Length; i++)
        {
            Renderer r = e.renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(OcclusionFadeId, value);
            r.SetPropertyBlock(_mpb);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawLine(cam.transform.position, target.position);
    }
}
