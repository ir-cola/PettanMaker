using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss01 attack hitbox. Damage is applied once to each PlayerHealth per hitbox activation.
/// </summary>
public class B1_AttackHitbox : MonoBehaviour
{
    private readonly HashSet<IHealth> damagedTargets = new();
    private B1_Boss       boss;
    private Collider      col;
    private int           currentDamage   = 1;
    private DamageContext currentContext;

    public Collider HitboxCollider => col != null ? col : col = GetComponent<Collider>();

    private void Awake()
    {
        col = GetComponent<Collider>();
    }

    public void Initialize(B1_Boss owner)
    {
        boss = owner;
    }

    public void BeginAttack(int damage = 1, DamageContext ctx = default)
    {
        damagedTargets.Clear();
        currentDamage  = damage;
        currentContext = ctx;
    }

    private void OnTriggerEnter(Collider other) => TryDamage(other);
    private void OnTriggerStay(Collider other)  => TryDamage(other);

    private void TryDamage(Collider other)
    {
        if (boss == null)
            boss = GetComponentInParent<B1_Boss>();

        if (boss == null || !other.CompareTag("Player"))
            return;

        IHealth health = other.GetComponent<IHealth>() ?? other.GetComponentInParent<IHealth>();
        if (health == null || health.IsDead || damagedTargets.Contains(health))
            return;

        damagedTargets.Add(health);
        CombatSystem.Instance.Damage(health, currentDamage, currentContext, this);
        boss.PlayAttackHitSound();
    }
}
