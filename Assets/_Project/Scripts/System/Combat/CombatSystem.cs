/**===========================================================================

 File   : 戦闘システム [CombatSystem.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 ダメージ適用の集約シングルトン。TakeDamage・被弾フラッシュ・ヒットストップ・
 カメラシェイクをまとめて処理し、onDamageDealt を発火する。
 ===========================================================================**/
using UnityEngine;
using UnityEngine.Events;

public class CombatSystem : MonoBehaviour {
    private static CombatSystem instance;

    // CombatSystem がシーンに置かれていないステージ (Stage_1 等) でも
    // Damage 呼び出しが NRE にならないよう、初アクセス時に自動生成する。
    public static CombatSystem Instance {
        get {
            if (instance == null) {
                instance = FindObjectOfType<CombatSystem>();
                if (instance == null)
                    instance = new GameObject("CombatSystem (Auto)").AddComponent<CombatSystem>();
            }
            return instance;
        }
    }

    // 自動生成しない参照取得。シーン破棄中のイベント解除 (OnDisable) 用
    public static CombatSystem TryInstance => instance;

    public UnityEvent<GameObject, IHealth, int> onDamageDealt = new();
    public UnityEvent<GameObject, IHealth, int, DamageContext> onDamageDealtDetailed = new();

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    public void Damage(IHealth target, AttackData data, Component source = null)
        => Damage(target, data.damage, data.effects, source);

    public void Damage(IHealth target, int amount, DamageContext ctx = default, Component source = null) {
        if (target == null || target.IsDead) return;

        // プレイヤーの被弾無敵中はフラッシュ/ヒットストップ/シェイク含め被弾処理を丸ごと行わない。
        // デバッグ無敵は HP 減少のみ無効 (PlayerHealth.TakeDamage 側で吸収) で、被弾演出はそのまま通す。
        if (target is PlayerHealth playerHealth && playerHealth.IsHitInvincible) return;

        target.TakeDamage(amount);
        TriggerDamageFlash(target);
        GameObject sourceObject = source != null ? source.gameObject : null;
        onDamageDealt?.Invoke(sourceObject, target, amount);
        onDamageDealtDetailed?.Invoke(sourceObject, target, amount, ctx);

        if (target is MonoBehaviour mb && ctx.targetHitstop > 0)
            ResolveHitStopable(mb)?.Freeze(ctx.targetHitstop);

        if (source != null && ctx.sourceHitstop > 0)
            ResolveHitStopable(source)?.Freeze(ctx.sourceHitstop);

        if (ctx.globalHitstop > 0)
            GlobalHitStop.Play(ctx.globalHitstop, ctx.globalHitstopScale > 0f ? ctx.globalHitstopScale : 0.05f);

        if (ctx.shakeDuration > 0 && ctx.shakeIntensity > 0)
            CameraShake.Play(ctx.shakeDuration, ctx.shakeIntensity);
    }

    // ヒットボックスが本体の子にある構成 (Boss01 等) でも、本体側の
    // AnimationHitStop へ届くよう自身→親の順で IHitStopable を解決する。
    private static IHitStopable ResolveHitStopable(Component c)
        => c.GetComponent<IHitStopable>() ?? c.GetComponentInParent<IHitStopable>();

    private static void TriggerDamageFlash(IHealth target) {
        if (!(target is MonoBehaviour mb)) return;

        var flash = mb.GetComponent<ToonDamageFlash>();
        if (flash == null)
            flash = mb.GetComponentInParent<ToonDamageFlash>();
        if (flash == null)
            flash = mb.GetComponentInChildren<ToonDamageFlash>(true);

        flash?.Flash();
    }
}
