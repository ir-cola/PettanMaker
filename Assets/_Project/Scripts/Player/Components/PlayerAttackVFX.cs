/**===========================================================================

 File   : プレイヤー攻撃VFX配線 [PlayerAttackVFX.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 納品VFXパッケージ (Resources/VFX/*) をプレイヤーの攻撃演出へ配線する:
   ・チャージ    : VFX_Charge_00X (ChargeMagicController) を Begin/Release/Cancel
   ・スタンプ出現: WarpHole_Portal を飛翔方向へ向けて再生 (readme 推奨 ~1.2s 破棄)
   ・スタンプ着弾: 命中 = VFX_Stamp_00X (服で切替) / 空振り = VFX_StampImpact_Miss
   ・命中ヒット  : VFX_Hit_0001 (ヒットストップ中もエフェクトは等速 = 非スケール時間)
 プレハブは Resources/VFX 直下 (ビルド削除対策)。見つからない演出は静かに
 スキップし、チャージは PlayerAttack.Charge 側の簡易デフォルトへフォールバック。
 PlayerAttack.Awake から Ensure() でランタイム装着される (シーン配置不要)。
 服 → VFX 番号の対応は StampImpacts_Readme 準拠:
   001=Default / 002=チェシャ / 003=アリス / 004=フィーバー
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackVFX : MonoBehaviour {

    // Resources/VFX 直下のプレハブ名
    private const string PortalPrefab = "VFX/WarpHole_Portal";
    private const string MissPrefab   = "VFX/VFX_StampImpact_Miss";
    private const string HitPrefab    = "VFX/VFX_Hit_0001";
    private const string CompletePrefab = "VFX/VFX_Hit_0001";   // チャージ完了の一発ポップ (流用)

    // 破棄タイミング (納品readme推奨: Portal 0.8〜1.2s / Miss 1.0〜1.5s)
    private const float PortalLifetime = 1.2f;
    private const float MissLifetime   = 1.5f;
    private const float ImpactLifetime = 2.0f;
    private const float HitLifetime    = 1.5f;
    private const float CompleteLifetime = 1.2f;

    // 床埋まり対策の浮かせ量 (readme: 0.03〜0.08)
    private static readonly Vector3 GroundLift = Vector3.up * 0.05f;

    [Header("VFX スケール調整 (でかすぎ対策)")]
    [Tooltip("スタンプ召喚ポータルの倍率 (小さいほど控えめ)")]
    [SerializeField, Min(0.05f)] private float portalScale = 0.1f;
    [Tooltip("命中ヒット火花の倍率 (プレハブは3倍と大きいので下げる。画面を覆いすぎない値に)")]
    [SerializeField, Min(0.05f)] private float hitScale = 0.4f;
    [Tooltip("スタンプ着弾(命中)エフェクトの倍率")]
    [SerializeField, Min(0.05f)] private float impactScale = 0.8f;
    [Tooltip("空振り着地エフェクトの倍率")]
    [SerializeField, Min(0.05f)] private float missScale = 0.8f;
    [Tooltip("チャージ完了ポップの倍率")]
    [SerializeField, Min(0.05f)] private float completeScale = 0.55f;
    [Tooltip("チャージ完了ポップの高さ (プレイヤー足元からの m)")]
    [SerializeField] private float completeHeight = 1.3f;

    private PlayerAttack attack;
    private PlayerModelSwitcher modelSwitcher;

    // チャージ演出リグ (VFX_Charge_00X のインスタンス)
    private ChargeMagicController chargeRig;
    private string chargeRigCloth;          // どの服用に組んだか (着替えたら組み直す)
    private float releasedAt = -10f;        // ReleaseCharge 時刻 (直後の Stop を Cancel 扱いしない)

    // Resources ロードのキャッシュ (null もキャッシュして毎回 Load しない。警告は1回だけ)
    private static readonly Dictionary<string, GameObject> s_prefabCache = new Dictionary<string, GameObject>();

    /// <summary>プレイヤーに装着済みの本コンポーネントを返す。無ければ追加する。</summary>
    public static PlayerAttackVFX Ensure(GameObject playerGO) {
        var v = playerGO.GetComponent<PlayerAttackVFX>();
        if (v == null) v = playerGO.AddComponent<PlayerAttackVFX>();
        return v;
    }

    private void Awake() {
        attack = GetComponent<PlayerAttack>();
        modelSwitcher = GetComponent<PlayerModelSwitcher>();
        PlayerRunDust.Ensure(gameObject);   // 走り土煙もここで一括ブートストラップ
    }

    private void OnEnable() {
        PlayerAttack.OnStampSpawned += HandleStampSpawned;
        PlayerAttack.OnStampLanded  += HandleStampLanded;
        PlayerAttack.OnEnemyHit     += HandleEnemyHit;
    }

    private void OnDisable() {
        PlayerAttack.OnStampSpawned -= HandleStampSpawned;
        PlayerAttack.OnStampLanded  -= HandleStampLanded;
        PlayerAttack.OnEnemyHit     -= HandleEnemyHit;
    }

    /**=======================================================================
      スタンプイベント → ワンショットVFX
    =======================================================================**/
    private void HandleStampSpawned(PlayerAttack.StampEvent ev) {
        // 出現ポータル: スタンプの飛翔方向へ回転させる (readme 指定)。でかすぎるので portalScale で縮小。
        SpawnOneShot(PortalPrefab, ev.position + GroundLift, ev.rotation, PortalLifetime,
                     unscaledTime: false, scale: portalScale);
    }

    private void HandleStampLanded(PlayerAttack.StampEvent ev) {
        // 着弾エフェクトはスケール時間で再生する。命中時はヒットストップが同時に発生するので、
        // ParticleSystem と VFXGraph(VisualEffect) を一緒に凍らせて「着弾がヒットストップで止まる」
        // 一貫した見た目にする (片方だけ等速で走ってズレるのを避ける)。等速の手応えは
        // HandleEnemyHit のヒット火花 (純ParticleSystem) が担当する。
        if (ev.hitCount > 0)
            SpawnOneShot(ImpactPrefabForCloth(), ev.position + GroundLift, Quaternion.identity,
                         ImpactLifetime, unscaledTime: false, scale: impactScale);
        else
            SpawnOneShot(MissPrefab, ev.position + GroundLift, Quaternion.identity,
                         MissLifetime, unscaledTime: false, scale: missScale);
    }

    private void HandleEnemyHit(PlayerAttack.HitEvent ev) {
        // Toylogic のヒット演出: ヒットストップで画面を止めてもエフェクトは等倍速で流す。
        // VFX_Hit_0001 は純ParticleSystem なので useUnscaledTime だけで完全に等速化できる。
        // プレハブが3倍と大きく画面を覆うので hitScale で縮小する。
        SpawnOneShot(HitPrefab, ev.position, Quaternion.identity, HitLifetime,
                     unscaledTime: true, scale: hitScale);
    }

    /// <summary>チャージ完了 (満タン到達) の一発ポップ。PlayerAttack.Charge から呼ばれる。</summary>
    public void PlayChargeCompleteFX() {
        Vector3 pos = transform.position + Vector3.up * completeHeight;
        SpawnOneShot(CompletePrefab, pos, Quaternion.identity, CompleteLifetime,
                     unscaledTime: true, scale: completeScale);
    }

    private void SpawnOneShot(string path, Vector3 pos, Quaternion rot, float lifetime,
                              bool unscaledTime, float scale) {
        GameObject prefab = LoadPrefab(path);
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, pos, rot);
        if (!Mathf.Approximately(scale, 1f)) {
            fx.transform.localScale *= scale;
            // 納品プレハブには Scaling Mode = Local の ParticleSystem が混ざっている
            // (WarpHole_Portal のトレイル等)。Local は親スケールを無視するので root を
            // 縮めても縮まず、「コアだけ小さくトレイルは原寸」のちぐはぐな絵になる。
            // スケール指定を必ず効かせるため Hierarchy へ揃える。
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) {
                var m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }
        if (unscaledTime) {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) {
                var main = ps.main;
                main.useUnscaledTime = true;
            }
        }
        Destroy(fx, lifetime);
    }

    /**=======================================================================
      チャージ演出リグ (PlayerAttack.Charge から委譲される)
        BeginChargeFX : 長押し確定 (リグが無ければ false → 従来演出へ)
        ReleaseChargeFX : 発動 (収束 → 解放フレア)
        EndChargeFX   : StateAttack.Exit の後始末。発動直後なら収束演出を続けさせ、
                        溜め不足キャンセルなら即リセットする。
    =======================================================================**/
    public bool HasChargeRig => LoadPrefab(ChargePrefabForCloth()) != null || LoadPrefab("VFX/VFX_Charge_001") != null;
    public bool UsingChargeRig => chargeRig != null;

    public bool BeginChargeFX() {
        ChargeMagicController rig = EnsureChargeRig();
        if (rig == null) return false;
        // 前回発動の収束演出が残っていると BeginCharge が無視される (Controller 側ガード)。
        // 連続チャージでは前回分を打ち切って溜め直す。
        if (rig.CurrentState == ChargeMagicController.ChargeState.Converging) rig.CancelCharge();
        rig.BeginCharge();
        return true;
    }

    public bool ReleaseChargeFX() {
        if (chargeRig == null) return false;
        releasedAt = Time.unscaledTime;
        chargeRig.ReleaseCharge();
        return true;
    }

    public bool EndChargeFX() {
        if (chargeRig == null) return false;
        if (Time.unscaledTime - releasedAt > 0.2f) chargeRig.CancelCharge();
        return true;
    }

    private ChargeMagicController EnsureChargeRig() {
        string cloth = ClothVfxSuffix();
        if (chargeRig != null && chargeRigCloth == cloth) return chargeRig;
        if (chargeRig != null) Destroy(chargeRig.gameObject);   // 着替えたので服対応のリグへ組み直す
        chargeRig = null;

        GameObject prefab = LoadPrefab("VFX/VFX_Charge_" + cloth);
        if (prefab == null) prefab = LoadPrefab("VFX/VFX_Charge_001");
        if (prefab == null) return null;

        // 発射位置 (チャージの溜め位置) に追従させる。未設定ならプレイヤーの胸元。
        Transform anchor = attack != null && attack.ChargeSpawnPoint != null
                         ? attack.ChargeSpawnPoint : transform;
        GameObject go = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
        if (anchor == transform) go.transform.localPosition = Vector3.up * 1.2f;

        var ctrl = go.GetComponentInChildren<ChargeMagicController>();
        if (ctrl == null) { Destroy(go); return null; }

        // プレハブ同梱のプレビュー用キーボードトリガ (Space) はゲーム操作と衝突するので切る。
        // 制御は本コンポーネントが Begin/Release/Cancel を直接呼ぶ。
        ctrl.SetTriggerSource(null);
        foreach (var trig in go.GetComponentsInChildren<ChargeTriggerBase>(true)) trig.enabled = false;

        chargeRig = ctrl;
        chargeRigCloth = cloth;
        return chargeRig;
    }

    /**=======================================================================
      服 → VFX 番号 (StampImpacts_Readme: 001=Default/002=チェシャ/003=アリス/004=フィーバー)
    =======================================================================**/
    private string ImpactPrefabForCloth() => "VFX/VFX_Stamp_" + ClothVfxSuffix();
    private string ChargePrefabForCloth() => "VFX/VFX_Charge_" + ClothVfxSuffix();

    private string ClothVfxSuffix() {
        string n = (modelSwitcher != null && modelSwitcher.CurrentClothPrefab != null)
                 ? modelSwitcher.CurrentClothPrefab.name.ToLowerInvariant() : "";
        if (n.Contains("alice"))    return "003";
        if (n.Contains("fever"))    return "004";
        if (n.Contains("cheshire")) return "002";
        return "001";
    }

    private static GameObject LoadPrefab(string path) {
        if (s_prefabCache.TryGetValue(path, out GameObject cached)) return cached;
        GameObject p = Resources.Load<GameObject>(path);
        if (p == null) Debug.LogWarning("[PlayerAttackVFX] Resources/" + path + " が見つかりません (演出スキップ)");
        s_prefabCache[path] = p;   // null もキャッシュ (警告を毎回出さない)
        return p;
    }

    private void OnDestroy() {
        if (chargeRig != null) Destroy(chargeRig.gameObject);
    }
}
