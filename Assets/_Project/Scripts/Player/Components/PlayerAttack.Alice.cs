/**===========================================================================

 File   : プレイヤー攻撃 - アリス専用 (ちび/巨大スタンプ) [PlayerAttack.Alice.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/16
 -----------------------------------------------------------------------------
 アリス服のサイズ別攻撃。共通のスタンプ機構 (出現ポップ / スカッシュ / ディゾルブ /
 命中処理) は PlayerAttack.cs 本体 & PlayerAttack.Charge.cs を再利用する。

   ・小さいとき  : ChibiStampBegin() … 前方の地面へ小さいスタンプを上から下へ
                    「ぺちっ」と落とすだけ。非ダメージ (敵は倒せない)。
   ・巨大化のとき : GiantStampBegin() … 大きいスタンプを叩き落とし、着地点で
                    多段ヒットの範囲ダメージ。StateAttack のチャージ解放で発動。

 分岐 (小=ちび / 巨大=チャージ→巨大 / それ以外=通常) は StateAttack が
 PlayerClothSkills.Mode を見て行う。ここは「発動そのもの」だけを担当する。
 ===========================================================================**/

using System.Collections;
using UnityEngine;

public partial class PlayerAttack {

    // チューニング値は PlayerAttackSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値 (= Cheshire シーンの調整値)。

    // アリス: ちびスタンプ (小さいとき・非ダメージ)
    private float chibiScaleMul = 0.4f;      // スタンプ倍率 (weaponPrefab 基準)
    private float chibiHeight = 3.5f;        // 召喚する高さ (m)
    private float chibiForward = 1.0f;       // 前方オフセット (m)
    private float chibiAppearTime = 0.1f;    // 出現ポップの時間 (s)
    private float chibiFallSpeed = 16f;      // 最大落下速度 (m/s)
    private float chibiFallGravity = 120f;   // 落下加速度 (m/s^2)。大きいほど鋭く『ぺちっ』
    // アリス: 巨大スタンプ (巨大化チャージ・多段ヒット)
    private float giantScaleMul = 2.6f;      // スタンプ倍率 (weaponPrefab 基準)
    private float giantHeight = 7f;          // 召喚する高さ (m)
    private float giantForward = 1.5f;       // 前方オフセット (m)
    private float giantAppearTime = 0.16f;   // 出現ポップの時間 (s)
    private float giantSlamTime = 0.22f;     // 叩き落としの時間 (s)。短いほど鋭い『ズドン』
    private float giantRadius = 4.5f;        // 着地の効果半径 (m)
    private int giantDamagePerHit = 25;      // 1ヒットぶんのダメージ (2発目以降)
    private int giantFirstHitDamage = 40;    // 多段ヒットの1発目だけのダメージ (0以下で giantDamagePerHit)
    private int giantHitCount = 4;           // 多段ヒットの回数
    private float giantHitInterval = 0.09f;  // 多段ヒットの間隔 (s)
    private float giantHitstop = 0.12f;      // チャージ/巨大スタンプ着地の全体ヒットストップ (秒。最後の一撃で1回)
    // アリス: 巨大化チャージ カメラ (溜め完了時に1回)
    private float giantChargeZoomIn = 2.2f;  // 溜め完了時のパンチズーム量 (m。正で寄る→戻る)
    private float giantChargeFovPunch = -7f; // 溜め完了時の FOV パンチ (度。負で望遠=寄る)

    // StateAttack.UpdateGiantCharge が参照する
    public float GiantChargeZoomIn   => giantChargeZoomIn;
    public float GiantChargeFovPunch => giantChargeFovPunch;

    // ======================================================================
    //  小さいとき: ちびスタンプ (非ダメージ)
    // ======================================================================
    /// <summary>前方の地面へ小さいスタンプを上から下へ落とすだけ (敵は倒せない)。</summary>
    public void ChibiStampBegin() {
        if (isUIMode || weaponPrefab == null) return;

        Vector3 origin = transform.position + transform.forward * chibiForward + Vector3.up * chibiHeight;
        Vector3 spawnPos = RaycastGround(origin, out Vector3 ground)
                         ? ground + Vector3.up * chibiHeight
                         : origin;

        StartCoroutine(ChibiSlam(spawnPos));
    }

    private IEnumerator ChibiSlam(Vector3 spawnPos) {
        GameObject weapon = Instantiate(weaponPrefab, spawnPos, Quaternion.identity);
        weapon.transform.localScale *= chibiScaleMul;
        activeWeaponCount++;

        OnStampSpawned?.Invoke(new StampEvent {
            weapon = weapon, position = spawnPos, rotation = Quaternion.LookRotation(transform.forward),
            attackType = AttackType.Charge, index = 0, totalCount = 1
        });

        // スケール0 → EaseOutBack で弾けるように出現 (据え置きで上に湧く)
        yield return SpawnAppearAnimation(weapon, chibiAppearTime);
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }

        // 上から下へ落下 (掃引 Ray で床にスナップ)。命中処理は呼ばない = 非ダメージ。
        int mask = ~(1 << weapon.layer);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        float velocity = 0f;
        bool  landed   = false;
        const float groundSkin = 0.25f;
        while (weapon != null && !landed) {
            velocity = Mathf.Min(velocity + chibiFallGravity * Time.deltaTime, chibiFallSpeed);
            float step = velocity * Time.deltaTime;
            if (Physics.Raycast(weapon.transform.position, Vector3.down, out RaycastHit hit,
                                step + groundSkin, mask, QueryTriggerInteraction.Ignore)) {
                weapon.transform.position = hit.point;   // 地面ちょうどへ
                landed = true;
            } else {
                weapon.transform.position += Vector3.down * step;
                if (weapon.transform.position.y < -10f) landed = true;
            }
            yield return null;
        }

        // 『ぺちっ』と潰れて戻る → ディゾルブ消滅。命中(OnWeaponLanded)は行わない。
        if (weapon != null) {
            StartCoroutine(ImpactSquash(weapon, weapon.transform.localScale));
            StartCoroutine(DissolveAndDestroy(weapon));
        }
        ReleaseWeaponSlot();
    }

    // ======================================================================
    //  巨大化のとき: 巨大スタンプ (多段ヒット)
    // ======================================================================
    /// <summary>大きいスタンプを叩き落とし、着地点で多段ヒットの範囲ダメージを与える。</summary>
    public void GiantStampBegin() {
        if (isUIMode || weaponPrefab == null) return;

        // チャージ発動の共通演出 (収束→解放フレア + 発動SE)。AttackBegin と揃える。
        attackVfx?.ReleaseChargeFX();
        sound?.PlayChargeRelease();

        Vector3 origin = transform.position + transform.forward * giantForward + Vector3.up * giantHeight;
        Vector3 land = RaycastGround(origin, out Vector3 ground)
                     ? ground
                     : transform.position + transform.forward * giantForward;
        Vector3 start = land + Vector3.up * giantHeight;

        TPSCameraController.Instance?.SetSlamZoom(true);   // 落下に合わせてカメラが寄る
        StartCoroutine(GiantSlam(start, land));
    }

    private IEnumerator GiantSlam(Vector3 startPos, Vector3 landPos) {
        GameObject weapon = Instantiate(weaponPrefab, startPos, Quaternion.identity);
        weapon.transform.localScale *= giantScaleMul;
        Vector3 baseScale = weapon.transform.localScale;
        activeWeaponCount++;

        OnStampSpawned?.Invoke(new StampEvent {
            weapon = weapon, position = startPos, rotation = Quaternion.LookRotation(transform.forward),
            attackType = AttackType.Release_Charge2, index = 0, totalCount = 1
        });

        yield return SpawnAppearAnimation(weapon, giantAppearTime);
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }

        // 叩き落とし (easeInQuad で終端加速『ズドン』)
        float t = 0f;
        while (weapon != null && t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.05f, giantSlamTime);
            float e = Mathf.Clamp01(t); e = e * e;
            weapon.transform.position = Vector3.LerpUnclamped(startPos, landPos, e);
            yield return null;
        }
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }
        weapon.transform.position = landPos;

        // 着弾スカッシュ (多段ヒットと並行して1回)
        StartCoroutine(ImpactSquash(weapon, baseScale));

        // 多段ヒット: 毎 tick OverlapSphere → ダメージ。ApplyDamageToEnemies は1呼び出し内で
        // 重複除去するが、呼び出しを跨げば同じ敵に再ヒット = 多段。全体停止は最後の一撃だけ
        // (毎回だと停止が重なってガタつくため)。
        for (int i = 0; i < giantHitCount; i++) {
            bool lastHit = (i == giantHitCount - 1);
            // 1発目 (初撃) だけ giantFirstHitDamage を使い、大きくできる (0以下なら通常値)。
            int dmg = (i == 0 && giantFirstHitDamage > 0) ? giantFirstHitDamage : giantDamagePerHit;
            Collider[] hits = Physics.OverlapSphere(landPos, giantRadius);
            int hitCount = ApplyDamageToEnemies(hits, landPos, dmg,
                                                allowGlobalHitstop: lastHit, globalHitstop: giantHitstop);

            if (i == 0) {
                if (hitCount == 0) sound?.PlayAttackMiss();
                OnStampLanded?.Invoke(new StampEvent {
                    weapon = weapon, position = landPos, rotation = Quaternion.identity,
                    attackType = AttackType.Release_Charge2, index = 0, totalCount = 0, hitCount = hitCount
                });
            }
            if (!lastHit) yield return new WaitForSeconds(giantHitInterval);
        }

        if (weapon != null) StartCoroutine(DissolveAndDestroy(weapon));
        ReleaseWeaponSlot();
    }
}
