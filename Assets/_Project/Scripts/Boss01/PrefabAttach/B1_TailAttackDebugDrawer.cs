using UnityEngine;

/// <summary>
/// TailAttackの攻撃範囲をGame/Sceneビューへ描画する。
/// B1_DebugManagerが生成・更新を管理する。
/// </summary>
public sealed class B1_TailAttackDebugDrawer
{
    private const float ImpactDuration = 0.3f;

    private B1_TailAttackAction trackedAction;
    private bool wasLanded;
    private float impactTimer;

    public void Update(B1_TailAttackAction action)
    {
        if (action == null)
        {
            trackedAction = null;
            wasLanded = false;
            impactTimer = 0f;
            return;
        }

        if (trackedAction != action)
        {
            trackedAction = action;
            wasLanded = false;
            impactTimer = 0f;
        }

        Vector3 center = action.DamageCenter + Vector3.up * 0.05f;
        if (!action.HasLanded)
        {
            B1_RuntimeGizmos.DrawFilledCircle(
                center, action.DamageRadius, new Color(1f, 0.75f, 0f, 0.8f), 0.45f);
            return;
        }

        if (!wasLanded)
        {
            wasLanded = true;
            impactTimer = ImpactDuration;
        }

        if (impactTimer <= 0f) return;

        float t = 1f - impactTimer / ImpactDuration;
        B1_RuntimeGizmos.DrawFilledCircle(
            center, action.DamageRadius, new Color(1f, 0.1f, 0f, 1f), 1f);
        B1_RuntimeGizmos.DrawAuraRings(
            center, action.DamageRadius, new Color(1f, 0.25f, 0f, 1f), t);
        impactTimer -= Time.deltaTime;
    }
}
