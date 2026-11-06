using System.Collections.Generic;
using UnityEngine;

public class EnemyAttackHitbox : MonoBehaviour
{
    [SerializeField] private Transform  hitboxTransform;
    [SerializeField] private Collider   triggerCollider;
    [SerializeField, Min(0f)] private float targetHitCooldown = 0.5f;

    private readonly HashSet<IHealth> damagedTargets = new();
    private readonly Dictionary<IHealth, float> lastHitTimes = new();
    private int currentDamage;
    private int currentDamageType = 1;
    private bool isAttackActive;

    public Collider   HitboxCollider  => triggerCollider;
    public Transform  HitboxTransform => hitboxTransform;
    public bool HitThisAttack => damagedTargets.Count > 0;

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
            // Collider が別 GO にある場合、Trigger イベントをここへ転送するレシーバーを置く
            if (triggerCollider.gameObject != gameObject)
            {
                var recv = triggerCollider.gameObject.GetComponent<EnemyHitboxReceiver>()
                        ?? triggerCollider.gameObject.AddComponent<EnemyHitboxReceiver>();
                recv.Init(this);
            }
        }
    }

    public void BeginAttack(int damage, int damageType = 1)
    {
        damagedTargets.Clear();
        currentDamage = damage;
        currentDamageType = Mathf.Max(0, damageType);
        isAttackActive = true;
        if (triggerCollider != null) triggerCollider.enabled = true;
    }

    public void EndAttack()
    {
        isAttackActive = false;
        if (triggerCollider != null) triggerCollider.enabled = false;
    }

    internal void OnTrigger(Collider other) => TryDamage(other);

    // Collider が同 GO にある場合は直接受け取る
    private void OnTriggerEnter(Collider other) => TryDamage(other);
    private void OnTriggerStay(Collider other)  => TryDamage(other);

    private void TryDamage(Collider other)
    {
        if (!isAttackActive) return;
        if (!other.CompareTag("Player")) return;
        IHealth health = other.GetComponent<IHealth>() ?? other.GetComponentInParent<IHealth>();
        if (health == null || health.IsDead || damagedTargets.Contains(health)) return;

        if (lastHitTimes.TryGetValue(health, out float lastHitTime)
            && Time.time - lastHitTime < targetHitCooldown)
            return;

        damagedTargets.Add(health);
        lastHitTimes[health] = Time.time;
        CombatSystem.Instance.Damage(
            health,
            currentDamage,
            default(DamageContext).WithPlayerDamageType(currentDamageType),
            this);
    }
}
