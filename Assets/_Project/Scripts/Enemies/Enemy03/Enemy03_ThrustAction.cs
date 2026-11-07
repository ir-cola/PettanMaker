using UnityEngine;

public class Enemy03_ThrustAction : EnemyAction
{
    private readonly Enemy03 enemy;
    private readonly EnemyAttackHitbox hitbox;
    private readonly float thrustDelay;
    private readonly float hitboxEnableDelay;
    private readonly float hitboxActiveDuration;
    private readonly bool endHitboxOnImpactEvent;
    private readonly string triggerPrep;
    private readonly string triggerThrust;

    private bool thrustFired;
    private bool hitboxEnabled;
    private bool hitboxEnded;
    private float elapsed;
    private float hitboxEndTime;   // 実際に有効化した時点から数えた当たり判定の終了 elapsed 時刻

    public Enemy03_ThrustAction(
        Enemy03 enemy, EnemyAttackHitbox hitbox,
        float thrustDelay, float hitboxEnableDelay, float hitboxActiveDuration,
        bool endHitboxOnImpactEvent,
        string triggerPrep, string triggerThrust)
        : base(enemy)
    {
        this.enemy                = enemy;
        this.hitbox               = hitbox;
        this.thrustDelay          = Mathf.Max(0f, thrustDelay);
        this.hitboxEnableDelay    = Mathf.Max(0f, hitboxEnableDelay);
        this.hitboxActiveDuration = Mathf.Max(0f, hitboxActiveDuration);
        this.endHitboxOnImpactEvent = endHitboxOnImpactEvent;
        this.triggerPrep          = triggerPrep;
        this.triggerThrust        = triggerThrust;
    }

    public override void Enter()
    {
        thrustFired = false;
        hitboxEnabled = false;
        hitboxEnded = false;
        elapsed = 0f;
        SubscribeAttackImpact(NotifyImpact);
        owner.FacePlayer();
        owner.AnimatorBridge?.SetTrigger(triggerPrep);
    }

    public override void Update()
    {
        owner.FacePlayer();
        elapsed += Time.deltaTime;

        if (!thrustFired && elapsed >= thrustDelay)
        {
            thrustFired = true;
            owner.AnimatorBridge?.SetTrigger(triggerThrust);
            enemy.PlayAttackSE();
        }

        if (!hitboxEnabled && !hitboxEnded && elapsed >= hitboxEnableDelay)
        {
            BeginHitbox();
        }

        if (hitboxEnabled && !hitboxEnded && elapsed >= hitboxEndTime)
        {
            EndHitbox();
        }

        if (thrustFired && hitboxEnded)
        {
            IsFinished = true;
        }
    }

    public override void Exit()
    {
        base.Exit();
        hitbox?.EndAttack();
        if (IsFinished)
            enemy.OnThrustResolved(hitbox != null && hitbox.HitThisAttack);
    }

    /// <summary>Enemy03.OnAttackImpact() アニメイベントから呼ばれる（早期終了用）</summary>
    public void NotifyImpact()
    {
        if (!endHitboxOnImpactEvent) return;
        if (hitboxEnded) return;

        if (hitboxEnabled)
        {
            // 当たり判定が有効な間のインパクト = 早期終了（従来どおり）
            EndHitbox();
        }
        else
        {
            // 有効化前にインパクトが来た = これが突きの当たりの瞬間。
            // ここで判定を開始し、アクティブ時間ぶん有効にする（即終了すると当たり判定が
            // 物理ステップを跨がず一度も当たらずダメージ0になる不具合を防ぐ）。
            BeginHitbox();
        }
    }

    private void BeginHitbox()
    {
        hitboxEnabled = true;
        hitbox?.BeginAttack(owner.AttackDamage, owner.AttackDamageType);
        hitboxEndTime = elapsed + hitboxActiveDuration;
    }

    private void EndHitbox()
    {
        hitboxEnded = true;
        hitbox?.EndAttack();
    }
}
