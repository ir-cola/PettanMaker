/**===========================================================================

 File   : プレイヤー攻撃 - 長押しチャージ [PlayerAttack.Charge.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 PlayerAttack の partial 分割 (チャージ担当)。長押しで溜めて発動する叩きつけ:
 スタンプを頭上/斜めから予測着地点へ弧軌道で「ズドンッ」と落とす (V字収束)。
 チャージ段階エフェクト・着地予測もここ。共有機構は PlayerAttack.cs 本体。
 ===========================================================================**/

using System.Collections;
using UnityEngine;

public partial class PlayerAttack {

    // チューニング値は PlayerAttackSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値 (= Cheshire シーンの調整値)。

    // 召喚/着地予測オフセット
    private float forwardOffset = 2.6f;         // 前方オフセット
    private float heightOffset = 3.0f;          // 高さオフセット
    private float sideOffset = 1.0f;            // 横オフセット (フォールバック配置)
    private float betweenWeaponsOffset = 0.5f;  // 武器同士の間隔 (フォールバック配置)
    // チャージ
    private float maxChargeTime = 1.6f;
    private float[] releaseThresholds = { 0f, 0.7f, 1.6f };
    private float charge1Cooldown = 0.6f;       // Charge1 連打防止用。Charge2/3 は対象外
    // 叩きつけ
    private float slamSpeed = 15.0f;            // 最大落下速度 (弧不成立時のフォールバック用)
    private float slamGravity = 100f;           // 落下加速度 (同上)
    private float slamWaitTime = 0.15f;         // スポーン演出 + 叩きつけ前の待機
    // 弧軌道スラム (全スタンプが落下予想点1点へ収束)
    private float arcStagger = 0.05f;           // 同時召喚の時差 (秒)。0で完全同時
    private float arcFlightTime = 0.24f;        // 弧の飛行時間 (秒)。短いほど斜めスパッ
    private float convergeSpread = 2.4f;        // V字収束の横広がり (m)
    private float convergeHeight = 3.0f;        // V字収束の開始高さ (m)
    private float convergeJitter = 0.4f;        // 着地点を進入方向側へ寄せる量 (m)。貫通防止
    private float arcRise = 0.25f;              // V字軌道の膨らみ (m)。小さいほど直線的

    [Header("チャージ 発射/着弾位置 (空オブジェクト。未設定は従来の予測着地/V字収束)")]
    [Tooltip("チャージスタンプの発射位置。プレイヤーの子に置くと向き・移動に追従。複数段は左右にばらけて収束")]
    [SerializeField] private Transform chargeSpawnPoint;
    [Tooltip("チャージスタンプの着弾位置。設定すると予測着地の代わりにここへ弧スラムする")]
    [SerializeField] private Transform chargeImpactPoint;

    [Header("チャージエフェクト")]
    [Tooltip("チャージ中に再生する ParticleSystem。未設定なら Awake で簡易デフォルトを生成。")]
    [SerializeField] private ParticleSystem chargeEffect;
    [Tooltip("Charge2 (中段階) 到達時に1回バースト再生する ParticleSystem。未設定なら白色デフォルト。")]
    [SerializeField] private ParticleSystem chargeMidEffect;
    [Tooltip("Charge3 (満タン) 到達時に1回バースト再生する ParticleSystem。未設定なら黄色デフォルト。")]
    [SerializeField] private ParticleSystem chargeCompleteEffect;

    private float lastCharge1Time = -10f;

    // 外部公開 (StateAttack が参照)
    public float MaxChargeTime => maxChargeTime;
    public float[] ReleaseThresholds => releaseThresholds;
    // Charge1 連打防止
    public bool CanAttack => !isUIMode && Time.time - lastCharge1Time >= charge1Cooldown;
    // チャージ発射位置 (PlayerAttackVFX がチャージ演出リグの取り付け先に使う)
    public Transform ChargeSpawnPoint => chargeSpawnPoint;

    /// <summary>チャージエフェクトの初期化 (本体 Awake から呼ぶ)。</summary>
    private void InitChargeEffects() {
        // 納品VFXパッケージ (Resources/VFX/VFX_Charge_00X) があればそちらを使うので、
        // 簡易デフォルトは生成しない (二重再生防止)。Inspector 指定があれば尊重する。
        bool hasRig = attackVfx != null && attackVfx.HasChargeRig;

        if (chargeEffect == null && !hasRig) chargeEffect = CreateDefaultChargeEffect();
        if (chargeEffect != null) chargeEffect.Stop();

        if (chargeMidEffect == null && !hasRig)
            chargeMidEffect = CreateDefaultBurstEffect("ChargeMidEffect_Default", new Color(1f, 1f, 1f, 1f), burstCount: 35);
        if (chargeMidEffect != null) chargeMidEffect.Stop();

        if (chargeCompleteEffect == null && !hasRig)
            chargeCompleteEffect = CreateDefaultBurstEffect("ChargeCompleteEffect_Default", new Color(1f, 0.95f, 0.3f, 1f), burstCount: 50);
        if (chargeCompleteEffect != null) chargeCompleteEffect.Stop();
    }

    /**=======================================================================
      攻撃開始 (StateAttack から呼ばれる)
    =======================================================================**/
    public void AttackBegin(AttackType attackType) {
        if (isUIMode) {
            AttackLandingIndicator.TryHide();   // 発動不可: 予想サークルを片付ける
            return;
        }
        if (weaponPrefab == null) {
            Debug.LogWarning("[PlayerAttack] weaponPrefab が未設定です");
            AttackLandingIndicator.TryHide();
            return;
        }

        // 連打防止のクールダウンは Charge1 だけ。Charge2/3 はホールドが自然な throttle になる
        if (attackType == AttackType.Release_Charge1) lastCharge1Time = Time.time;

        // 発射位置: 空オブジェクト指定があればそれ、無ければ従来の召喚位置
        Vector3 summonPos = chargeSpawnPoint != null
                          ? chargeSpawnPoint.position
                          : transform.position + transform.forward * forwardOffset + Vector3.up * heightOffset;

        // 着弾点: 空オブジェクト指定があればそこへ、無ければ落下予想サークルと同じ予測点
        Vector3 landCenter;
        bool hasGround;
        if (chargeImpactPoint != null) {
            landCenter = chargeImpactPoint.position;
            hasGround  = true;   // 空オブジェクト指定なら常にそこへ弧スラム
        } else {
            hasGround = TryPredictLanding(out landCenter);
        }

        // チャージ演出: 発動 = 収束→解放フレア (StateAttack.Exit の Stop はキャンセル扱いしない)
        attackVfx?.ReleaseChargeFX();
        sound?.PlayChargeRelease();

        // スラムズーム開始: 落下に合わせてカメラが寄り、最終着地後に戻る
        TPSCameraController.Instance?.SetSlamZoom(true);

        StartCoroutine(SummonWeapon(attackType, summonPos, hasGround, landCenter));
    }

    private IEnumerator SummonWeapon(AttackType attackType, Vector3 beginPos, bool hasGround, Vector3 landCenter) {
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        int baseCount = (int)attackType;
        int totalCount = baseCount + bonusAttackCount;

        for (int i = 0; i < totalCount; i++) {
            Vector3 startPos;
            Vector3 landPos = landCenter;

            if (chargeSpawnPoint != null) {
                // 発射位置=空オブジェクト。複数段は左右にばらして着弾点へ収束させる。
                startPos = ChargeSpawnSpread(beginPos, forward, right, i, baseCount);
                if (totalCount > 1) {
                    Vector3 side = startPos - landCenter; side.y = 0f;
                    if (side.sqrMagnitude > 0.001f) landPos = landCenter + side.normalized * convergeJitter;
                }
            } else if (hasGround) {
                startPos = CalcConvergeStart(landCenter, forward, right, i, baseCount);

                // 着地点は各スタンプの「進入方向側」へ convergeJitter だけ寄せる (中心を跨いで貫通するのを防ぐ)。
                if (totalCount > 1) {
                    Vector3 side = startPos - landCenter;
                    side.y = 0f;
                    if (side.sqrMagnitude > 0.001f)
                        landPos = landCenter + side.normalized * convergeJitter;
                }
            } else {
                // 地面が取れない: 旧仕様 (召喚位置から直下落下) へフォールバック
                startPos = CalcSpawnPos(attackType, beginPos, forward, right, i);
            }

            GameObject weapon = Instantiate(weaponPrefab, startPos, Quaternion.identity);
            activeWeaponCount++;

            OnStampSpawned?.Invoke(new StampEvent {
                weapon = weapon,
                position = weapon.transform.position,
                rotation = Quaternion.LookRotation(forward),
                attackType = attackType,
                index = i,
                totalCount = totalCount
            });

            if (chargeSpawnPoint != null || hasGround)
                StartCoroutine(ArcSlamWeapon(weapon, startPos, landPos, arcRise));
            else
                StartCoroutine(SlamWeapon(weapon));

            if (arcStagger > 0f && i < totalCount - 1)
                yield return new WaitForSeconds(arcStagger);
        }
    }

    /**=======================================================================
      V字収束の始点を返す。
        Charge1 : 右上から斜めに突入 / Charge2 : 左右から V 字 / Charge3 : 左右+手前上の3方向
        ボーナス分: 収束点まわりの円周へ黄金角で分散配置
    =======================================================================**/
    private Vector3 CalcConvergeStart(Vector3 landCenter, Vector3 forward, Vector3 right, int index, int baseCount) {
        Vector3 up = Vector3.up;

        if (index >= baseCount) {   // ボーナス分は円周から
            int bonusOrder = index - baseCount;
            float ang = (45f + 137.5f * bonusOrder) * Mathf.Deg2Rad;
            Vector3 dir = right * Mathf.Cos(ang) + forward * Mathf.Sin(ang);
            return landCenter + dir * convergeSpread + up * convergeHeight;
        }

        switch (baseCount) {
            case 1:  // Charge1: 右上から斜めに突入 (スパッと)。低い arcRise で直線的な斜め軌道
                return landCenter + right * (convergeSpread * 0.9f) - forward * (convergeSpread * 0.3f)
                     + up * (convergeHeight + 0.8f);
            case 2:  // Charge2: 左右から V 字
                return landCenter + right * (index == 0 ? -convergeSpread : convergeSpread) + up * convergeHeight;
            default: // Charge3: 左・右・手前上の3方向
                if (index == 0) return landCenter - right * convergeSpread + up * convergeHeight;
                if (index == 1) return landCenter + right * convergeSpread + up * convergeHeight;
                return landCenter - forward * (convergeSpread * 0.85f) + up * (convergeHeight + 0.7f);
        }
    }

    /// <summary>発射位置(空オブジェクト)基準の各スタンプ始点。複数段は左右にばらす (着弾点へ収束)。</summary>
    private Vector3 ChargeSpawnSpread(Vector3 spawnBase, Vector3 forward, Vector3 right, int index, int baseCount) {
        if (index >= baseCount) {   // ボーナス分は円周へ
            int o = index - baseCount;
            float ang = (45f + 137.5f * o) * Mathf.Deg2Rad;
            return spawnBase + (right * Mathf.Cos(ang) + forward * Mathf.Sin(ang)) * (convergeSpread * 0.5f);
        }
        switch (baseCount) {
            case 1:  return spawnBase;
            case 2:  return spawnBase + right * (index == 0 ? -convergeSpread * 0.5f : convergeSpread * 0.5f);
            default:
                if (index == 0) return spawnBase - right * (convergeSpread * 0.5f);
                if (index == 1) return spawnBase + right * (convergeSpread * 0.5f);
                return spawnBase;
        }
    }

    private Vector3 CalcSpawnPos(AttackType attackType, Vector3 beginPos, Vector3 forward, Vector3 right, int index) {
        if (index == 0) return beginPos;

        int baseCount = (int)attackType;

        // ボーナス分（baseCount を超えるインデックス）は前方ラインに並べる
        if (index >= baseCount) {
            int bonusOrder = index - baseCount + 1;
            float side = ((bonusOrder % 2) == 0 ? 1 : -1) * sideOffset * ((bonusOrder + 1) / 2);
            return beginPos + forward * betweenWeaponsOffset * 2.0f + right * side;
        }

        switch (attackType) {
            case AttackType.Release_Charge2:
                return beginPos + forward * betweenWeaponsOffset;
            case AttackType.Release_Charge3: {
                    int correctIndex = index * 2 - 3; // -1, 1
                    return beginPos + forward * betweenWeaponsOffset + right * correctIndex * sideOffset;
                }
            default:
                return beginPos;
        }
    }

    /**=======================================================================
      叩きつけ (フォールバック): スポーン演出 → 重力加速で落下 → 着地 → 後始末
    =======================================================================**/
    private IEnumerator SlamWeapon(GameObject weapon) {
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }
        Vector3 baseScale = weapon.transform.localScale;

        yield return SpawnAppearAnimation(weapon, slamWaitTime);
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }

        bool hasHit = false;
        int raycastMask = ~(1 << weapon.layer);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) raycastMask &= ~(1 << playerLayer);

        float velocity = 0f;
        const float groundSkin = 0.3f; // 着地とみなす地面までの余白

        while (weapon != null && !hasHit) {
            velocity = Mathf.Min(velocity + slamGravity * Time.deltaTime, slamSpeed);
            float step = velocity * Time.deltaTime;

            // 落下区間 (+着地余白) を掃引して地面を探す (dt が跳ねても床を貫通しない)
            RaycastHit hit;
            if (Physics.Raycast(weapon.transform.position, Vector3.down, out hit, step + groundSkin, raycastMask)) {
                weapon.transform.position = hit.point; // 地面ちょうどへスナップ
                hasHit = true;
            } else {
                weapon.transform.position += Vector3.down * step;
                if (weapon.transform.position.y < -10f) hasHit = true;
            }
            yield return null;
        }

        FinishSlam(weapon, baseScale);
    }

    /**=======================================================================
      弧軌道スラム: 始点 → 収束点へ 2次ベジェで一気に落とす (t^2 で終端加速『ズドンッ』)。
      rise: 軌道の膨らみ (m)。0 で直線。
    =======================================================================**/
    private IEnumerator ArcSlamWeapon(GameObject weapon, Vector3 startPos, Vector3 landPos, float rise) {
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }
        Vector3 baseScale = weapon.transform.localScale;

        yield return SpawnAppearAnimation(weapon, slamWaitTime);
        if (weapon == null) { ReleaseWeaponSlot(); yield break; }

        Vector3 ctrl = (startPos + landPos) * 0.5f + Vector3.up * rise;

        float t = 0f;
        while (weapon != null && t < 1f) {
            t += Time.deltaTime / Mathf.Max(0.05f, arcFlightTime);
            float e = Mathf.Clamp01(t);
            e = e * e;   // easeInQuad: 出だしタメ → 終端加速 (ズドン)
            float u = 1f - e;
            weapon.transform.position = u * u * startPos + 2f * u * e * ctrl + e * e * landPos;
            yield return null;
        }

        if (weapon != null) weapon.transform.position = landPos;   // 地面ちょうどへスナップ
        FinishSlam(weapon, baseScale);
    }

    /**=======================================================================
      着地の後始末 (叩きつけ用): ダメージ → スカッシュ → ディゾルブ破棄 → スロット解放。
      例外が抜けるとコルーチンが死んで activeWeaponCount が減らず移動ロックになるため try/catch。
    =======================================================================**/
    private void FinishSlam(GameObject weapon, Vector3 baseScale) {
        if (weapon != null) {
            try { OnWeaponLanded(weapon, weapon.transform.position); }
            catch (System.Exception e) { Debug.LogException(e, this); }
            StartCoroutine(ImpactSquash(weapon, baseScale));
            StartCoroutine(DissolveAndDestroy(weapon));
        }
        ReleaseWeaponSlot();
    }

    /**=======================================================================
      叩きつけ着地: 周囲の Enemy にダメージ (OverlapSphere) + 命中演出
    =======================================================================**/
    private void OnWeaponLanded(GameObject weapon, Vector3 landPos) {
        // ダメージを先に確定させ、着弾イベントへ命中数を渡す (0 = 空振り → ミス着地VFX)
        Collider[] hitColliders = Physics.OverlapSphere(landPos, attackRadius);
        int hitCount = ApplyDamageToEnemies(hitColliders, landPos, attackDamage);
        if (hitCount == 0) sound?.PlayAttackMiss();
        OnStampLanded?.Invoke(new StampEvent {
            weapon = weapon, position = landPos, rotation = Quaternion.identity,
            attackType = AttackType.Charge, index = 0, totalCount = 0, hitCount = hitCount
        });
    }

    /**=======================================================================
      着地予測: 召喚位置の真下の地面点を返す (StateAttack の落下予想サークル用)。
    =======================================================================**/
    public bool TryPredictLanding(out Vector3 point) {
        Vector3 origin = transform.position + transform.forward * forwardOffset + Vector3.up * heightOffset;
        return RaycastGround(origin, out point);
    }

    // origin 真下の地面を探す (プレイヤー / 武器レイヤーは誤検知するので除外)
    private bool RaycastGround(Vector3 origin, out Vector3 point) {
        int mask = ~0;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (weaponLayer >= 0) mask &= ~(1 << weaponLayer);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore)) {
            point = hit.point;
            return true;
        }
        point = origin;
        return false;
    }

    /**=======================================================================
      スポーン演出: スケール 0 から EaseOutBack で弾けるように出現 (叩きつけ用)
    =======================================================================**/
    private IEnumerator SpawnAppearAnimation(GameObject weapon, float duration) {
        if (weapon == null || duration <= 0f) yield break;
        Transform tf = weapon.transform;

        OnStampAppear?.Invoke(new StampEvent {
            weapon = weapon, position = tf.position, rotation = tf.rotation,
            attackType = AttackType.Charge, index = 0, totalCount = 0
        });

        Vector3 baseScale = tf.localScale;
        tf.localScale = Vector3.zero;

        float t = 0f;
        while (t < duration && weapon != null) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            const float s = 1.70158f;
            float ease = 1f + (s + 1f) * Mathf.Pow(p - 1f, 3f) + s * Mathf.Pow(p - 1f, 2f);
            tf.localScale = baseScale * ease;
            yield return null;
        }
        if (weapon != null) tf.localScale = baseScale;
    }

    /**=======================================================================
      チャージエフェクト制御
      納品VFXパッケージのリグ (PlayerAttackVFX) があればそちらへ委譲し、
      無ければ従来の ParticleSystem (Inspector 指定 or 簡易デフォルト) を使う。
    =======================================================================**/
    public void StartChargeEffect() {
        sound?.PlayChargeStart();
        if (attackVfx != null && attackVfx.BeginChargeFX()) return;
        if (chargeEffect != null && !chargeEffect.isPlaying) chargeEffect.Play();
    }
    public void StopChargeEffect() {
        sound?.StopCharge();
        if (attackVfx != null && attackVfx.EndChargeFX()) return;
        if (chargeEffect != null && chargeEffect.isPlaying)
            chargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
    public void PlayChargeMidEffect() {
        sound?.PlayChargeLevel1();
        if (attackVfx != null && attackVfx.UsingChargeRig) return;   // リグが段階演出を内蔵
        if (chargeMidEffect == null) return;
        chargeMidEffect.Stop(); chargeMidEffect.Clear(); chargeMidEffect.Play();
    }
    public void PlayChargeCompleteEffect() {
        sound?.PlayChargeLevel2();
        // チャージ完了の一発ポップは、リグ使用時でも必ず出す (ユーザー要望: 完了の合図)。
        attackVfx?.PlayChargeCompleteFX();
        if (attackVfx != null && attackVfx.UsingChargeRig) return;   // リグが溜め中の段階演出を内蔵
        if (chargeCompleteEffect == null) return;
        chargeCompleteEffect.Stop(); chargeCompleteEffect.Clear(); chargeCompleteEffect.Play();
    }

    /// <summary>簡易デフォルトチャージエフェクト (周囲リングから中心へ収束する青パーティクル)。</summary>
    private ParticleSystem CreateDefaultChargeEffect() {
        var go = new GameObject("ChargeEffect_Default");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.up * 1.0f;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 5f; main.loop = true; main.startLifetime = 0.45f; main.startSpeed = 0f;
        main.startSize = 0.1f; main.startColor = new Color(0.6f, 0.85f, 1f, 0.9f);
        main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission; emission.rateOverTime = 40f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.5f;

        var velOverLifetime = ps.velocityOverLifetime;
        velOverLifetime.enabled = true;
        velOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velOverLifetime.radial = new ParticleSystem.MinMaxCurve(-4f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null) renderer.material = new Material(shader) { color = new Color(0.6f, 0.85f, 1f, 0.9f) };
        return ps;
    }

    /// <summary>簡易デフォルトバーストエフェクト (指定色を1回バースト。Charge2=白 / Charge3=黄)。</summary>
    private ParticleSystem CreateDefaultBurstEffect(string name, Color color, int burstCount) {
        var go = new GameObject(name);
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.up * 1.0f;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 0.5f; main.loop = false; main.startLifetime = 0.4f; main.startSpeed = 4f;
        main.startSize = 0.18f; main.startColor = color;
        main.maxParticles = 100; main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null) renderer.material = new Material(shader) { color = color };
        return ps;
    }
}
