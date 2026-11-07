/**===========================================================================

 File   : 攻撃の落下予想サークル [AttackLandingIndicator.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------
 チャージ中〜スタンプ着地までの間、落下予想位置の地面に紫のサークルを表示する。
 見た目は Effects/AttackLandingCircle シェーダーが担当し、本コンポーネントは
 位置・サイズ・ディゾルブ値 (出現/消滅) だけを制御する。
 マテリアルは Resources/M_AttackLandingCircle.mat 経由 (ビルド削除対策)。
 ===========================================================================**/

using UnityEngine;

public class AttackLandingIndicator : MonoBehaviour {

    private static AttackLandingIndicator instance;

    [Tooltip("出現ディゾルブの時間 (秒)")]
    [SerializeField] private float showDuration = 0.22f;
    [Tooltip("消滅ディゾルブの時間 (秒)")]
    [SerializeField] private float hideDuration = 0.30f;
    [Tooltip("attackRadius に対するサークルの倍率 (見た目は実範囲より控えめに)")]
    [SerializeField] private float radiusScale = 0.8f;
    [Tooltip("地面からの浮かせ量 (m)。Zファイト防止")]
    [SerializeField] private float groundLift = 0.03f;
    [Tooltip("表示されたまま更新が止まった場合の自動消滅 (秒)。リーク保険")]
    [SerializeField] private float staleTimeout = 6f;
    [Tooltip("予測点への追従速度 (1/s)。移動中に予測点が跳ねてもガクつかないよう平滑化")]
    [SerializeField] private float followSpeed = 10f;

    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    private GameObject quad;
    private Material mat;
    private bool visible;
    private float lastTouchTime;
    // ディゾルブは Update 駆動 (コルーチンは実行時生成GOで止まる事故があり得るため使わない)
    private float dissolveCurrent = 1f;
    private float dissolveTarget = 1f;
    private float dissolveSpeed = 5f;      // 1/秒 (Show/Hide が duration から設定)

    // 予測点はレイキャスト結果が段差/小物でフレーム毎に跳ねるため、
    // SetPoint は目標だけ受け取り Update で指数平滑して追従する (ガクつき防止)
    private Vector3 targetPos;
    private float targetDiameter;
    private bool hasPose;                  // false の間は次の SetPoint でスナップ

    /// <summary>シーンに1つだけ自動生成して返す。</summary>
    public static AttackLandingIndicator Ensure() {
        if (instance == null) {
            var go = new GameObject("AttackLandingIndicator (Auto)");
            instance = go.AddComponent<AttackLandingIndicator>();
        }
        return instance;
    }

    /// <summary>存在すれば消滅ディゾルブ。未生成なら何もしない (攻撃キャンセル等の後片付け用)。</summary>
    public static void TryHide() {
        if (instance != null) instance.Hide();
    }

    public bool Visible => visible;

    private void Awake() {
        if (instance == null) instance = this;
        BuildQuad();
    }

    private void OnDestroy() {
        if (instance == this) instance = null;
        if (mat != null) Destroy(mat);
    }

    private void BuildQuad() {
        Material src = Resources.Load<Material>("M_AttackLandingCircle");
        if (src == null) {
            Shader sh = Shader.Find("Effects/AttackLandingCircle");
            if (sh == null) {
                Debug.LogWarning("[AttackLandingIndicator] M_AttackLandingCircle が見つかりません");
                return;
            }
            mat = new Material(sh) { name = "M_AttackLandingCircle (Auto)" };
        } else {
            mat = new Material(src) { name = "M_AttackLandingCircle (Auto)" };
        }

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "LandingCircle";
        quad.transform.SetParent(transform, false);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 地面へ寝かせる
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        quad.SetActive(false);
    }

    private void Update() {
        // リーク保険: 出しっぱなしで staleTimeout 更新が無ければ勝手に消える
        if (visible && Time.unscaledTime - lastTouchTime > staleTimeout) Hide();

        // 予測点へ指数平滑で追従 (移動チャージ中のガクつき防止)
        if (hasPose && quad != null) {
            float k = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, k);
            float d = Mathf.Lerp(quad.transform.localScale.x, targetDiameter, k);
            quad.transform.localScale = new Vector3(d, d, 1f);
        }

        // ディゾルブ進行 (ヒットストップ中も動くよう非スケール時間)
        if (mat != null && !Mathf.Approximately(dissolveCurrent, dissolveTarget)) {
            dissolveCurrent = Mathf.MoveTowards(dissolveCurrent, dissolveTarget,
                                                dissolveSpeed * Time.unscaledDeltaTime);
            mat.SetFloat(DissolveId, dissolveCurrent);
            // 消滅しきったらクワッドを眠らせる
            if (!visible && dissolveCurrent >= 1f && quad != null) quad.SetActive(false);
        }
    }

    /// <summary>ディゾルブしながら出現。既に表示中なら何もしない。</summary>
    public void Show() {
        if (quad == null || mat == null) return;
        lastTouchTime = Time.unscaledTime;
        if (visible) return;
        visible = true;
        hasPose = false;   // 次の SetPoint は平滑せずスナップ (前回位置から滑ってこない)
        quad.SetActive(true);
        dissolveCurrent = 1f;
        mat.SetFloat(DissolveId, dissolveCurrent);
        dissolveTarget = 0f;
        dissolveSpeed = 1f / Mathf.Max(0.01f, showDuration);
    }

    /// <summary>ディゾルブしながら消滅。</summary>
    public void Hide() {
        if (!visible || quad == null || mat == null) return;
        visible = false;
        dissolveTarget = 1f;
        dissolveSpeed = 1f / Mathf.Max(0.01f, hideDuration);
    }

    /// <summary>予測着地点とサークル半径を更新する (チャージ中は毎フレーム呼ぶ)。
    /// 実際の移動は Update の平滑追従が行う (レイキャスト結果の跳ねを吸収)。</summary>
    public void SetPoint(Vector3 groundPoint, float radius) {
        if (quad == null) return;
        lastTouchTime = Time.unscaledTime;
        targetPos = groundPoint + Vector3.up * groundLift;
        targetDiameter = Mathf.Max(0.1f, radius * radiusScale) * 2f;
        if (!hasPose) {   // 出現直後はスナップ
            transform.position = targetPos;
            quad.transform.localScale = new Vector3(targetDiameter, targetDiameter, 1f);
            hasPose = true;
        }
    }

}
