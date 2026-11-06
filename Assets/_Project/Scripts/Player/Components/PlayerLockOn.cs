/**===========================================================================

 File   : 攻撃中ロックオン [PlayerLockOn.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------
 攻撃中に近くのエネミー (IEnemy) を自動ロックし、カメラ
 (TPSCameraController) をその敵方向へ追尾させ、敵の位置にビルボードの
 レティクルを表示する。一度ロックしたら乗り移らず、死亡/破棄/離れすぎで
 解除。StateAttack (チャージ) / StateComboAttack (コンボ) から Ensure()
 経由でランタイム装着される。
 コンボ用に「攻撃入力が一定秒あくと自動解除」する保持時間と、索敵半径の
 オーバーライドを SetActive で指定できる (チャージは従来通り無期限保持)。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public class PlayerLockOn : MonoBehaviour {

    // チューニング値は Resources/PlayerLockOnSettings.asset (ScriptableObject) で調整する。
    // 以下はアセットが無い場合のコード既定値 (Awake でアセット値に上書き)。
    private float lockOnRadius = 12f;                              // ロック対象を探す半径 (m)
    private float lockOnBreakRadius = 18f;                         // これより離れたらロック解除 (m)
    private float scanInterval = 0.15f;                            // 未ロック時の索敵間隔 (秒)
    private Color reticleColor = new Color(1f, 0.3f, 0.45f, 1f);   // レティクル色
    private float reticleSize = 1.4f;                              // レティクルの一辺サイズ (m)
    private float reticlePulse = 0.07f;                            // サイズのパルス振幅 (割合)

    private void Awake() {
        // ランタイム装着 (Ensure) されるため Inspector 参照は持たず、Resources から自動ロードする。
        var settings = Resources.Load<PlayerLockOnSettings>("PlayerLockOnSettings");
        if (settings == null) {
            Debug.LogWarning("[PlayerLockOn] Resources/PlayerLockOnSettings.asset が見つかりません。コード既定値で動作します。", this);
            return;
        }
        lockOnRadius      = settings.LockOnRadius;
        lockOnBreakRadius = settings.LockOnBreakRadius;
        scanInterval      = settings.ScanInterval;
        reticleColor      = settings.ReticleColor;
        reticleSize       = settings.ReticleSize;
        reticlePulse      = settings.ReticlePulse;
    }

    private bool active;
    private IEnemy lockedEnemy;
    private Transform lockedTf;
    private Collider lockedCol;      // レティクルの位置基準 (バウンズ中心)
    private EnemyAimAnchor lockedAim; // 敵ごとの Aim 位置指定 (あれば優先)
    private float nextScanTime;
    private float autoReleaseAfter;  // >0 なら最終 RefreshHold からこの秒数で自動解除 (コンボ用)
    private float lastHoldTime;      // 直近の攻撃入力 (RefreshHold) 時刻
    private float scanRadiusOverride = -1f;   // >0 なら索敵半径を上書き (コンボは狭め)

    private GameObject reticleGO;
    private Material reticleMat;
    private float reticleShownTime;

    private readonly HashSet<IEnemy> scanDedupe = new HashSet<IEnemy>();

    /// <summary>プレイヤーに装着済みの本コンポーネントを返す。無ければ追加する。</summary>
    public static PlayerLockOn Ensure(GameObject playerGO) {
        var l = playerGO.GetComponent<PlayerLockOn>();
        if (l == null) l = playerGO.AddComponent<PlayerLockOn>();
        return l;
    }

    public bool HasTarget => lockedTf != null;
    public Transform Target => lockedTf;

    /// <summary>チャージ開始/終了で呼ぶ。OFF で即ロック解除。</summary>
    public void SetActive(bool on) => SetActive(on, 0f);

    /// <summary>攻撃ロックオンの開始/終了。
    /// autoRelease : >0 なら最終 RefreshHold からこの秒数、攻撃入力が無いと自動解除 (コンボ用)。
    ///               0 以下なら SetActive(false) まで保持 (チャージ用)。
    /// scanRadius  : >0 なら索敵半径を上書き (コンボは近接だけ拾う)。</summary>
    public void SetActive(bool on, float autoRelease, float scanRadius = -1f) {
        if (on) {
            autoReleaseAfter = autoRelease;
            scanRadiusOverride = scanRadius;
            lastHoldTime = Time.time;
            active = true;
            // 未ロックなら「その場で」同期スキャンする。SetActive の直後 (同フレーム) に
            // 攻撃が発動しても敵へ即アイム/ホーミングできるようにするため (Update 待ちにしない)。
            // コンボ1段目やチャージ開始のロックがワンテンポ遅れる不具合の対策。
            if (lockedTf == null) {
                nextScanTime = Time.time + scanInterval;
                Scan();
            }
        } else if (active) {
            active = false;
            Unlock();
        }
    }

    /// <summary>攻撃入力のたびに呼び、自動解除タイマーを巻き戻す (コンボ用)。</summary>
    public void RefreshHold() => lastHoldTime = Time.time;

    private void Update() {
        if (!active) return;

        // コンボ用: 攻撃入力が autoReleaseAfter 秒あいたら自動でロック解除
        if (autoReleaseAfter > 0f && Time.time - lastHoldTime >= autoReleaseAfter) {
            SetActive(false);
            return;
        }

        if (lockedTf != null) ValidateLock();

        if (lockedTf == null && Time.time >= nextScanTime) {
            nextScanTime = Time.time + scanInterval;
            Scan();
        }
    }

    private void LateUpdate() {
        UpdateReticle();
    }

    /**=======================================================================
      索敵: 半径内の最近接 IEnemy をロック。
      一度ロックしたらここへは戻らない (乗り移り防止は ValidateLock 側で担保)。
    =======================================================================**/
    private void Scan() {
        float radius = scanRadiusOverride > 0f ? scanRadiusOverride : lockOnRadius;
        Collider[] cols = Physics.OverlapSphere(transform.position, radius);
        scanDedupe.Clear();

        IEnemy best = null;
        Collider bestCol = null;
        float bestSqr = float.MaxValue;

        foreach (Collider col in cols) {
            IEnemy enemy = col.GetComponentInParent<IEnemy>();
            if (enemy == null || enemy.IsDead) continue;
            if (!scanDedupe.Add(enemy)) continue;

            var mb = enemy as MonoBehaviour;
            if (mb == null) continue;

            float sqr = (mb.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) {
                bestSqr = sqr;
                best = enemy;
                bestCol = col;
            }
        }

        if (best != null) Lock(best, bestCol);
    }

    private void Lock(IEnemy enemy, Collider col) {
        lockedEnemy = enemy;
        lockedTf = (enemy as MonoBehaviour).transform;
        lockedCol = col;
        // 敵ごとの Aim 位置指定 (子も含めて探す)。無ければ従来のバウンズ中心を使う。
        lockedAim = (enemy as MonoBehaviour).GetComponentInChildren<EnemyAimAnchor>(true);
        TPSCameraController.Instance?.SetLockOnTarget(lockedTf);
        ShowReticle();
    }

    /**=======================================================================
      ロック維持判定: 死亡 / 破棄 / 離れすぎ のときだけ解除。
      「より近い敵が来ても乗り移らない」= ここで距離比較の再選定はしない。
      解除後もチャージ中 (active) なら Update の再スキャンで次の敵を拾う。
    =======================================================================**/
    private void ValidateLock() {
        bool lost = lockedTf == null
                 || lockedEnemy == null || lockedEnemy.IsDead
                 || (lockedTf.position - transform.position).sqrMagnitude
                    > lockOnBreakRadius * lockOnBreakRadius;
        if (lost) Unlock();
    }

    private void Unlock() {
        if (lockedTf != null) TPSCameraController.Instance?.ClearLockOnTarget(lockedTf);
        else TPSCameraController.Instance?.ClearLockOnTarget();
        lockedEnemy = null;
        lockedTf = null;
        lockedCol = null;
        lockedAim = null;
        if (reticleGO != null) reticleGO.SetActive(false);
    }

    /**=======================================================================
      レティクル (ビルボード)
    =======================================================================**/
    private void ShowReticle() {
        if (reticleGO == null) BuildReticle();
        if (reticleGO == null) return;
        reticleGO.SetActive(true);
        reticleShownTime = Time.unscaledTime;
    }

    private void BuildReticle() {
        Material src = Resources.Load<Material>("M_LockOnReticle");
        if (src == null) {
            // Resources 未整備でも最低限動くフォールバック (ビルドでは必ず .mat を用意する)
            Shader sh = Shader.Find("Effects/LockOnReticle");
            if (sh == null) {
                Debug.LogWarning("[PlayerLockOn] M_LockOnReticle が見つからずレティクル非表示");
                return;
            }
            reticleMat = new Material(sh) { name = "M_LockOnReticle (Auto)" };
        } else {
            reticleMat = new Material(src) { name = "M_LockOnReticle (Auto)" };
        }
        reticleMat.SetColor("_Color", reticleColor);

        reticleGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        reticleGO.name = "LockOnReticle";
        Destroy(reticleGO.GetComponent<Collider>());
        var mr = reticleGO.GetComponent<MeshRenderer>();
        mr.sharedMaterial = reticleMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        reticleGO.SetActive(false);
    }

    private void UpdateReticle() {
        if (reticleGO == null || !reticleGO.activeSelf) return;
        if (lockedTf == null) { reticleGO.SetActive(false); return; }

        // 位置: 敵ごとの Aim 指定 (EnemyAimAnchor) があれば最優先。
        // 無ければコライダーのバウンズ中心 (さらに無ければ Transform + 0.8m)。
        Vector3 pos = (lockedAim != null)
            ? lockedAim.Position
            : (lockedCol != null)
                ? lockedCol.bounds.center
                : lockedTf.position + Vector3.up * 0.8f;
        reticleGO.transform.position = pos;

        // ビルボード: カメラの回転をそのまま使う (完全に画面平行)
        Camera cam = Camera.main;
        if (cam != null) reticleGO.transform.rotation = cam.transform.rotation;

        // 出現ポップ (0.15s EaseOutBack) + パルス
        float appear = Mathf.Clamp01((Time.unscaledTime - reticleShownTime) / 0.15f);
        const float bk = 1.70158f;
        float ease = 1f + (bk + 1f) * Mathf.Pow(appear - 1f, 3f) + bk * Mathf.Pow(appear - 1f, 2f);
        float pulse = 1f + reticlePulse * Mathf.Sin(Time.unscaledTime * 6f);
        reticleGO.transform.localScale = Vector3.one * (reticleSize * ease * pulse);
    }

    private void OnDestroy() {
        Unlock();
        if (reticleGO != null) Destroy(reticleGO);
        if (reticleMat != null) Destroy(reticleMat);
    }

    private void OnDrawGizmosSelected() {
        Gizmos.color = new Color(1f, 0.3f, 0.45f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, lockOnRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.45f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, lockOnBreakRadius);
    }
}
