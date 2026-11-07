/**===========================================================================

 File   : プレイヤー攻撃 - タップ3段コンボ [PlayerAttack.Combo.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 PlayerAttack の partial 分割 (コンボ担当)。タップで3段コンボ:
 斜め斬撃スタンプを「生成位置(始点)→着弾位置(終点)」へ直線で飛ばし、掃引カプセルで
 命中判定 + 前方へ一歩ステップ。生成/着弾位置は空オブジェクトで指定。
 共有機構 (スタンプ命中/スカッシュ/消滅/状態) は PlayerAttack.cs 本体を参照。
 ===========================================================================**/

using System.Collections;
using UnityEngine;

public partial class PlayerAttack {

    /// <summary>コンボ1段ぶんの斬撃ジオメトリ。生成位置(始点)/着弾位置(終点)を空オブジェクトで指定。
    /// プレイヤーの子に置けば向き・移動に自動追従する。</summary>
    [System.Serializable]
    public class ComboSlashDef {
        public string label;
        [Tooltip("生成位置(始点) の空オブジェクト")]
        public Transform startPoint;
        [Tooltip("着弾位置(終点) の空オブジェクト")]
        public Transform endPoint;
    }

    // チューニング値は PlayerAttackSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値 (= Cheshire シーンの調整値)。
    // ※ コンボの再生速度(段ごと/速度カーブ)は PlayerMotionSpeeds へ集約。

    // コンボ (タップ攻撃 = 3段)
    private float tapThreshold = 0.15f;      // これ未満で離す=タップ=コンボ。以上=長押し=チャージ (秒)
    private float comboWindow = 0.6f;        // コンボの持続 / 次段受付時間 (秒)
    private float comboMinInterval = 0.3f;   // 各段の最短間隔 (秒)。超高速コンボ防止。早押しはバッファ
    private float comboCooldown = 0f;        // コンボ終了後のクールタイム (秒)
    private int[] comboStageDamages = { 15, 15, 20 };   // コンボ各段のダメージ (要素0=1段目...)
    private float[] comboStageHitstops = { 0.09f, 0.09f, 0.12f };   // コンボ各段の全体ヒットストップ (秒)
    private float comboFlightTime = 0.12f;   // 斬撃(スタンプ)の飛行時間 (秒)
    private float slashHitRadius = 1.6f;     // 斬撃の当たり判定の太さ (掃引カプセル半径 m)
    // 段ごとのスタンプ生成ディレイ (発動から何秒後に飛ばすか)
    private float combo1SpawnDelay = 0f;
    private float combo2SpawnDelay = 0f;
    private float combo3SpawnDelay = 0.3f;
    // 前進ステップ (一歩踏み出す)
    private float comboStepDistance = 1.2f;  // 1段あたりの前進距離 (m)
    private float comboStepTime = 0.18f;     // 前進にかける時間 (秒)
    // ロックオン (近くの敵へ自動ロック)
    private float comboLockRadius = 7f;      // 開始時にこの半径内の最近接エネミーへロックオン (m)。0以下で無効
    private float comboLockLinger = 1.2f;    // 攻撃入力がこの秒数あくとロックオン解除 (秒)
    private float[] comboLockStepMul = { 1.0f, 1.6f, 2.2f };   // ロック中の前進ステップ距離倍率 (段ごと)
    private float comboLockStopRange = 1.2f; // 敵のこの距離手前でステップ頭打ち (m)
    // カメラ演出 (発動準備で引き → 攻撃開始でズーム)
    private float comboWindupPull = 0.6f;    // 発動準備のカメラ引き量 (m)。0で無効
    private float comboZoomIn = 1f;          // 攻撃開始のカメラ寄せ量 (m)
    private float comboZoomFov = 6f;         // 攻撃開始のFOV望遠 (度)
    private float comboZoomDur = 0.18f;      // ズーム(寄り)にかける時間 (秒)
    private float comboZoomReturn = 0.55f;   // 元の画角へ戻す時間 (秒)

    [Header("デバッグ")]
    [Tooltip("シーンビューでコンボ斬撃の生成位置(始点)/着弾位置(終点)スフィアを描く")]
    [SerializeField] private bool debugDrawSlashGizmos = true;

    [Header("コンボ斬撃ジオメトリ (段ごとに 生成位置/着弾位置 の空オブジェクトを指定)")]
    [Tooltip("各段の始点(生成位置)/終点(着弾位置)を空オブジェクトで指定。シーンでその空オブジェクトを" +
             "動かして調整する。Player 選択でギズモ表示。未設定の段はプレイヤー正面へフォールバック")]
    [SerializeField] private ComboSlashDef[] comboSlashes = new ComboSlashDef[] {
        new ComboSlashDef { label = "0 右上→左下" },
        new ComboSlashDef { label = "1 左上→右下" },
        new ComboSlashDef { label = "2 頭上→正面" },
    };

    // コンボ振り分け・受付猶予 (StateAttack / StateComboAttack が参照)
    public float TapThreshold    => tapThreshold;
    public float ComboWindow     => comboWindow;
    public float ComboMinInterval => comboMinInterval;   // 各段の最短間隔 (連打の超高速化防止)

    // コンボロックオン設定 (StateComboAttack が参照)
    public float ComboLockRadius => comboLockRadius;
    public float ComboLockLinger => comboLockLinger;

    // コンボのクールタイム (StateAttack が発動可否を判定 / StateComboAttack が終了を通知)
    private float lastComboEndTime = -10f;
    public bool ComboReady => Time.time - lastComboEndTime >= comboCooldown;
    public void NotifyComboEnded() => lastComboEndTime = Time.time;

    private float ComboSpawnDelay(int i) => i == 0 ? combo1SpawnDelay : i == 1 ? combo2SpawnDelay : combo3SpawnDelay;

    /// <summary>コンボ段(index)のダメージ。要素が足りなければ末尾値、未設定なら 15 にフォールバック。</summary>
    private int ComboDamageFor(int index) {
        if (comboStageDamages == null || comboStageDamages.Length == 0) return 15;
        return comboStageDamages[Mathf.Clamp(index, 0, comboStageDamages.Length - 1)];
    }

    /// <summary>コンボ段(index)の全体ヒットストップ時間。未設定なら共有既定 (hitGlobalHitstop)。</summary>
    private float ComboHitstopFor(int index) {
        if (comboStageHitstops == null || comboStageHitstops.Length == 0) return hitGlobalHitstop;
        return comboStageHitstops[Mathf.Clamp(index, 0, comboStageHitstops.Length - 1)];
    }

    /**=======================================================================
      コンボ1段を発動 (StateComboAttack から呼ばれる):
        斜め斬撃スタンプをスポーン + 前方へ一歩ステップ。
      斬撃は直線を素早く飛ばし (スパッ)、掃引カプセルで命中判定する。
    =======================================================================**/
    public void DoComboHit(int index) {
        if (isUIMode || weaponPrefab == null) return;
        // ※ コンボの再生速度(段ごと/速度カーブ)は PlayerMotionSpeeds が ComboSpeed へ流し込む。

        // カメラ: 発動準備でスッと引く。生成ディレイ(=溜め)の2倍かけて引き切り、攻撃開始で寄せに切替。
        // ディレイがほぼ無い段は寄せが同フレームで上書きするため引きを出さない (無駄なガタつき防止)。
        if (comboWindupPull > 0f && ComboSpawnDelay(index) >= 0.05f) {
            float windupDur = ComboSpawnDelay(index) * 2f;
            TPSCameraController.Instance?.PunchZoom(-comboWindupPull, windupDur);   // 負=引き(距離増)
        }

        // 前進ステップは即時 (踏み込み)。スタンプ生成は段ごとのタイミングで遅延。
        // ロックオン中は距離が伸びるため、前進時間も距離に比例させる (参考実装: 約12m/s)。
        Transform lockTarget = TryGetComponent(out PlayerLockOn lockOn) && lockOn.HasTarget
                             ? lockOn.Target : null;
        float stepDist = ComboStepDistanceFor(index, lockTarget);
        float stepTime = Mathf.Clamp(stepDist / LungeSpeed, comboStepTime, MaxLungeTime);
        StartCoroutine(LungeForward(stepDist, stepTime, lockTarget));
        StartCoroutine(SpawnComboSlashDelayed(index, ComboSpawnDelay(index)));
    }

    // 踏み込みの基準速度 / 最長時間 (参考実装 player.cpp StartComboStep: 12 units/s, 0.15〜0.45s)
    private const float LungeSpeed   = 12f;
    private const float MaxLungeTime = 0.45f;

    /**=======================================================================
      コンボ1段の前進距離。ロックオン中は段が進むほど距離を伸ばし (段階倍率)、
      早く敵のもとへ届くようにする。敵の手前 comboLockStopRange で頭打ち。
    =======================================================================**/
    private float ComboStepDistanceFor(int index, Transform lockTarget) {
        float dist = comboStepDistance;
        if (lockTarget == null) return dist;

        if (comboLockStepMul != null && comboLockStepMul.Length > 0)
            dist *= comboLockStepMul[Mathf.Clamp(index, 0, comboLockStepMul.Length - 1)];

        Vector3 to = lockTarget.position - transform.position;
        to.y = 0f;
        return Mathf.Clamp(dist, 0f, Mathf.Max(0f, to.magnitude - comboLockStopRange));
    }

    /// <summary>段ごとの生成タイミング(spawnDelay)で斜め斬撃スタンプを飛ばす。先端(Down)が着弾へ向く。</summary>
    private IEnumerator SpawnComboSlashDelayed(int index, float delay) {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (weaponPrefab == null) yield break;

        // カメラ: 攻撃開始でグッと寄せる (引き演出から切替)。距離ズーム + FOV望遠。
        // 戻り (comboZoomReturn) は寄りより長め=ゆっくりめのイージングで元の画角へ。
        TPSCameraController.Instance?.PunchZoom(comboZoomIn, comboZoomDur, comboZoomReturn);
        if (comboZoomFov > 0f) TPSCameraController.Instance?.FovPunch(-comboZoomFov, comboZoomDur, comboZoomReturn);

        ComputeComboSlash(index, out Vector3 start, out Vector3 end);

        GameObject weapon = Instantiate(weaponPrefab, start, Quaternion.identity);
        // Up/Down 空オブジェクトで先端(Down)=着弾, 上端(Up)=発射 へ向ける。無ければ +Z を進行方向へ。
        if (!OrientStampByUpDown(weapon.transform, start, end)) {
            Vector3 wd = end - start;
            if (wd.sqrMagnitude > 0.0001f) weapon.transform.rotation = Quaternion.LookRotation(wd.normalized);
        }
        activeWeaponCount++;

        // イベントの rotation は「飛翔方向」(出現ポータルVFX が向きを合わせる)
        Vector3 flyDir = end - start;
        Quaternion flyRot = flyDir.sqrMagnitude > 0.0001f
                          ? Quaternion.LookRotation(flyDir.normalized) : weapon.transform.rotation;

        OnStampSpawned?.Invoke(new StampEvent {
            weapon = weapon, position = start, rotation = flyRot,
            attackType = AttackType.Release_Charge1, index = index, totalCount = 3
        });

        StartCoroutine(SlashWeapon(weapon, start, end, index));
    }

    /**=======================================================================
      コンボ斬撃の始点(生成位置)と終点(着弾位置)を返す。
      DoComboHit と OnDrawGizmosSelected が共用 (空オブジェクトの位置調整がギズモに即反映)。
      未設定の段はプレイヤー正面へフォールバック。
    =======================================================================**/
    private void ComputeComboSlash(int index, out Vector3 start, out Vector3 end) {
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        start = pos + Vector3.up * 1.9f;
        end   = pos + fwd * 3f + Vector3.up * 0.5f;

        if (comboSlashes == null || index < 0 || index >= comboSlashes.Length) return;
        ComboSlashDef d = comboSlashes[index];
        if (d == null) return;
        if (d.startPoint != null) start = d.startPoint.position;   // 生成位置 = 空オブジェクト
        if (d.endPoint   != null) end   = d.endPoint.position;     // 着弾位置 = 空オブジェクト
    }

    /// <summary>スタンプ内の Up/Down 空オブジェクトの軸(Up→Down)を start→end に合わせる。
    /// = 先端(Down)が着弾地点、上端(Up)が発射地点を向く。Up/Down が無ければ false。</summary>
    private bool OrientStampByUpDown(Transform weapon, Vector3 start, Vector3 end) {
        Vector3 worldDir = end - start;
        if (worldDir.sqrMagnitude < 0.0001f) return false;
        worldDir.Normalize();

        Transform up   = FindDeep(weapon, "Up");
        Transform down = FindDeep(weapon, "Down");
        if (up == null || down == null) return false;

        Vector3 axis = down.position - up.position;   // 生成直後(identity)の Up→Down ワールド向き = ローカル軸
        if (axis.sqrMagnitude < 0.0001f) return false;
        weapon.rotation = Quaternion.FromToRotation(axis.normalized, worldDir) * weapon.rotation;
        return true;
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    /**=======================================================================
      斬撃スタンプ: 始点→終点を直線で素早く飛ばし、掃引カプセルで命中判定。
    =======================================================================**/
    private IEnumerator SlashWeapon(GameObject weapon, Vector3 startPos, Vector3 endPos, int comboIndex) {
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }
        Vector3 baseScale = weapon.transform.localScale;

        float t = 0f;
        while (weapon != null && t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.02f, comboFlightTime);
            weapon.transform.position = Vector3.Lerp(startPos, endPos, Mathf.Clamp01(t));
            yield return null;
        }
        if (weapon != null) weapon.transform.position = endPos;

        // 命中判定 = 斬撃の掃引カプセル (始点→終点を太さ slashHitRadius で薙ぐ)。演出は正面の着弾点で。
        // ダメージを先に確定させ、着弾イベントへ命中数を渡す (0 = 空振り → ミス着地VFX)。
        Vector3 fxPos = endPos;
        Collider[] hits = Physics.OverlapCapsule(startPos, endPos, slashHitRadius);
        int hitCount = ApplyDamageToEnemies(hits, fxPos, ComboDamageFor(comboIndex),
                                            globalHitstop: ComboHitstopFor(comboIndex));
        if (hitCount == 0) sound?.PlayAttackMiss();
        OnStampLanded?.Invoke(new StampEvent {
            weapon = weapon, position = fxPos, rotation = Quaternion.identity,
            attackType = AttackType.Release_Charge1, index = 0, totalCount = 0, hitCount = hitCount
        });

        if (weapon != null) {
            StartCoroutine(ImpactSquash(weapon, baseScale));
            StartCoroutine(DissolveAndDestroy(weapon));
        }
        ReleaseWeaponSlot();
    }

    /**=======================================================================
      前方へ一歩ステップ (ease-out で踏み込む)。CharacterController を直接動かす。
      homing 指定時 (ロックオン中) は参考実装準拠の簡易ホーミング:
      毎フレーム敵方向へ向き直し、敵の手前 comboLockStopRange に届いたら即停止。
    =======================================================================**/
    private IEnumerator LungeForward(float distance, float time, Transform homing = null) {
        if (characterController == null || distance <= 0f || time <= 0f) yield break;
        Vector3 dir = transform.forward; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;
        dir.Normalize();

        float t = 0f, prev = 0f;
        while (t < time) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / time);
            float eased = 1f - (1f - p) * (1f - p);   // easeOut: 踏み込みは出だしで速く
            float delta = (eased - prev) * distance;
            prev = eased;

            if (homing != null) {
                Vector3 to = homing.position - transform.position; to.y = 0f;
                float remain = to.magnitude - comboLockStopRange;
                if (remain <= 0f) yield break;                 // 敵の手前に到達 = 停止
                if (to.sqrMagnitude > 0.0001f) {
                    dir = to.normalized;                       // 進行方向を敵へ再計算
                    transform.rotation = Quaternion.LookRotation(dir);   // 斬撃の向きも追従
                }
                delta = Mathf.Min(delta, remain);              // 行き過ぎ防止
            }
            characterController.Move(dir * delta);
            yield return null;
        }
    }

    /**=======================================================================
      Gizmo (デバッグ用): コンボ斬撃の生成/着弾位置 + チャージの着弾範囲を可視化
    =======================================================================**/
    private void OnDrawGizmosSelected() {
        // --- コンボ斬撃: 生成位置(始点・小球) と 着弾位置(終点・当たり半径) を可視化 ---
        // 空オブジェクト(startPoint/endPoint)を動かすとこのスフィアが即動くので、見ながら調整できる。
        if (debugDrawSlashGizmos) {
            Color[] cols = {
                new Color(1f,   0.45f, 0.2f),   // 0段 橙: 右上→左下
                new Color(0.3f, 0.7f,  1f),     // 1段 水: 左上→右下
                new Color(0.95f,0.4f,  0.95f),  // 2段 桃: 頭上→正面
            };
            for (int i = 0; i < 3; i++) {
                ComputeComboSlash(i, out Vector3 s, out Vector3 e);
                Gizmos.color = cols[i];
                Gizmos.DrawLine(s, e);                    // 斬撃の軌道
                Gizmos.DrawWireSphere(s, 0.3f);           // 生成位置 (始点マーカー)
                Gizmos.DrawSphere(s, 0.08f);
                Gizmos.DrawWireSphere(e, slashHitRadius); // 着弾位置 (= OverlapCapsule の当たり半径)
                Gizmos.DrawSphere(e, 0.14f);
            }
        }

        // --- チャージ: 着弾位置 (空オブジェクト指定 or 予測着地) + 効果半径 、発射位置 ---
        Gizmos.color = Color.yellow;
        Vector3 chargeLand;
        if (chargeImpactPoint != null) chargeLand = chargeImpactPoint.position;
        else if (!TryPredictLanding(out chargeLand)) chargeLand = transform.position + transform.forward * forwardOffset;
        Gizmos.DrawWireSphere(chargeLand, attackRadius);   // 着弾位置(叩きつけの当たり範囲)
        Gizmos.DrawSphere(chargeLand, 0.12f);
        if (chargeSpawnPoint != null) {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(chargeSpawnPoint.position, 0.35f);   // 発射位置
            Gizmos.DrawSphere(chargeSpawnPoint.position, 0.1f);
            Gizmos.DrawLine(chargeSpawnPoint.position, chargeLand);    // 発射→着弾
        }
    }
}
