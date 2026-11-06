using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IBlackboard の値と Condition を使って次の ActionGroup を選択する。
/// 選択結果は OnActionGroupSelected で HistoryKS へ通知される。
/// </summary>
public class B1_AIController
{
    private readonly B1_Boss      boss;
    private readonly IBlackboard  bb;
    private readonly ActionGroupEntry[] allGroups;
    private readonly Dictionary<ActionGroupType, float> cooldownEndTimes = new();

    public event Action<ActionGroupType> OnActionGroupSelected;
    public event Action                  OnRoarActionStarted;

    public B1_AIController(B1_Boss boss, IBlackboard bb)
    {
        this.boss      = boss;
        this.bb        = bb;
        this.allGroups = BuildGroups();
    }

    public B1_Action SelectNextAction()
    {
        var candidates = BuildCandidates();

        if (candidates.Count == 0)
            candidates.Add(FallbackEntry());

        var selected = WeightedSelect(candidates);
        OnActionGroupSelected?.Invoke(selected.groupType);

        if (selected.cooldown > 0f)
            cooldownEndTimes[selected.groupType] = Time.time + selected.cooldown;

        if (selected.groupType == ActionGroupType.Roar)
            OnRoarActionStarted?.Invoke();

        return CreateAction(selected.groupType);
    }

    // 攻撃終了後に挟む硬直（その場待機）の秒数。0 なら硬直なしで即次行動。
    // 「避けたら殴れる」反撃猶予を作るため、攻撃系グループにのみ値を返す。
    public float RecoveryFor(ActionGroupType group)
    {
        var s = boss.Settings;
        return group switch
        {
            ActionGroupType.Scratch    => s.Scratch.RecoveryDuration,
            ActionGroupType.RushCharge => s.Rush.RecoveryDuration,
            ActionGroupType.TailAttack => s.TailAttack.RecoveryDuration,
            _                          => 0f,
        };
    }

    // ---- 候補絞り込み ----

    private List<ActionGroupEntry> BuildCandidates()
    {
        var list = new List<ActionGroupEntry>();
        foreach (var g in allGroups)
        {
            if (!AllConditionsPass(g)) continue;
            // 同じグループを3回連続させない
            if (g.groupType == bb.LastActionGroup && bb.SameGroupCount >= 2) continue;
            // クールダウン中は候補から外す
            if (cooldownEndTimes.TryGetValue(g.groupType, out float endTime) && Time.time < endTime) continue;
            list.Add(g);
        }
        return list;
    }

    private bool AllConditionsPass(ActionGroupEntry g)
    {
        if (g.enableConditions == null) return true;
        foreach (var c in g.enableConditions)
            if (!c.Evaluate(bb)) return false;
        return true;
    }

    private ActionGroupEntry WeightedSelect(List<ActionGroupEntry> list)
    {
        float dist  = bb.PlayerDistance;
        float total = 0f;
        foreach (var e in list) total += EffectiveWeight(e, dist);
        float roll  = UnityEngine.Random.Range(0f, total);
        float accum = 0f;
        foreach (var e in list)
        {
            accum += EffectiveWeight(e, dist);
            if (roll <= accum) return e;
        }
        return list[list.Count - 1];
    }

    private float EffectiveWeight(ActionGroupEntry e, float dist)
    {
        float mult = e.distanceCurve != null ? e.distanceCurve.Evaluate(dist) : 1f;
        float w    = e.baseWeight * Mathf.Max(0f, mult);
        if (e.applyRepeatPenalty)
            w *= RepeatPenalty(e.groupType);
        return w;
    }

    // 直近に選ばれた行動ほど重みを下げ、同じ攻撃の連続選択を抑制する。
    // 除外ではなく倍率なので、候補が1つしか無い状況でも 0 にはならず選択可能。
    private float RepeatPenalty(ActionGroupType type)
    {
        switch (bb.StepsSinceLastUse(type))
        {
            case 1:  return 0.20f; // 直前と同じ攻撃は強く抑制
            case 2:  return 0.50f; // 2つ前も控えめに
            case 3:  return 0.80f; // 3つ前は軽く
            default: return 1f;    // 直近履歴に無ければ補正なし
        }
    }

    // ---- Action 生成 ----

    private B1_Action CreateAction(ActionGroupType type)
    {
        return type switch
        {
            ActionGroupType.Idle            => new B1_IdleAction(boss, bb),
            ActionGroupType.Walk            => new B1_WalkAction(boss, bb),
            ActionGroupType.Run             => new B1_RunAction(boss, bb),
            ActionGroupType.Scratch         => WithAimCrouch(new B1_ScratchAction(boss, bb)),
            ActionGroupType.BackStep        => new B1_BackStepAction(boss, bb),
            ActionGroupType.RushCharge      => new B1_ChargeAction(boss, bb),
            ActionGroupType.TailAttack      => WithAimCrouch(new B1_TailChargeAction(boss, bb)),
            ActionGroupType.TailRain        => WithAimCrouch(new B1_TailRainAction(boss, bb)),
            ActionGroupType.IdleApproach    => new B1_WalkAction(boss, bb),  // TODO
            ActionGroupType.Teleport        => new B1_WalkAction(boss, bb),  // TODO
            ActionGroupType.TeleportScratch => new B1_WalkAction(boss, bb),  // TODO
            ActionGroupType.InvisibleCharge => new B1_WalkAction(boss, bb),  // TODO
            ActionGroupType.Evade           => new B1_WalkAction(boss, bb),  // TODO
            ActionGroupType.Roar            => new B1_IdleAction(boss, bb),  // TODO
            _                               => new B1_WalkAction(boss, bb),
        };
    }

    private B1_Action WithAimCrouch(B1_Action attack)
    {
        var cfg = boss.Settings.AimCrouch;
        if (cfg == null || !cfg.Enabled) return attack;
        return new B1_AimCrouchAction(boss, bb, attack);
    }

    // ---- ActionGroupEntry テーブル ----

    private ActionGroupEntry[] BuildGroups()
    {
        var s = boss.Settings;
        float chargeMid = (s.RushCharge.MinRange + s.RushCharge.MaxRange) * 0.5f;

        return new[]
        {
            // ---- 待機（常時候補） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.Idle,
                baseWeight       = 10f,
                cooldown         = s.Idle.Cooldown,
                enableConditions = null,
            },

            // ---- 近距離：ひっかき（密着ほど高倍率） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.Scratch,
                baseWeight       = 40f,
                cooldown         = s.Scratch.Cooldown,
                applyRepeatPenalty = true,
                enableConditions = new Condition[] { new PlayerNearCondition(s.Scratch.AttackRange) },
                distanceCurve    = new AnimationCurve(
                    new Keyframe(0f,                   2.0f),
                    new Keyframe(s.Scratch.AttackRange, 0.6f)),
            },

            // ---- 近距離：後退ジャンプ（密着されたら距離を取る。密着ほど高倍率） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.BackStep,
                baseWeight       = 12f,
                cooldown         = s.BackStep.Cooldown,
                applyRepeatPenalty = true,
                enableConditions = new Condition[] { new PlayerNearCondition(s.BackStep.TriggerRange) },
                distanceCurve    = new AnimationCurve(
                    new Keyframe(0f,                      1.8f),
                    new Keyframe(s.BackStep.TriggerRange, 0.4f)),
            },

            // ---- 中距離：突進（レンジ中間でピーク） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.RushCharge,
                baseWeight       = 25f,
                cooldown         = s.RushCharge.Cooldown,
                applyRepeatPenalty = true,
                enableConditions = new Condition[]
                {
                    new PlayerFarCondition(s.RushCharge.MinRange),
                    new PlayerNearCondition(s.RushCharge.MaxRange),
                },
                distanceCurve    = new AnimationCurve(
                    new Keyframe(s.RushCharge.MinRange, 0.6f),
                    new Keyframe(chargeMid,              2.0f),
                    new Keyframe(s.RushCharge.MaxRange,  1.0f)),
            },

            // ---- テールアタック：ジャンプ範囲攻撃（必殺技） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.TailAttack,
                baseWeight       = 15f,
                cooldown         = s.TailAttack.Cooldown,
                applyRepeatPenalty = true,
                enableConditions = new Condition[]
                {
                    new PlayerFarCondition(s.TailAttack.MinRange),
                    new PlayerNearCondition(s.TailAttack.MaxRange),
                },
                distanceCurve    = null,
            },

            // ---- テールレイン：見下ろし固定カメラ＋連続叩きつけ（回避演出／Phase2 の見せ場） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.TailRain,
                baseWeight       = 12f,
                cooldown         = s.TailRain.Cooldown,
                applyRepeatPenalty = true,
                enableConditions = BuildTailRainConditions(s.TailRain),
                distanceCurve    = null,
            },

            // ---- 遠距離：走って接近（遠いほど高倍率） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.Run,
                baseWeight       = 20f,
                cooldown         = s.Run.Cooldown,
                enableConditions = new Condition[] { new PlayerFarCondition(s.Run.ActivateRange) },
                distanceCurve    = new AnimationCurve(
                    new Keyframe(s.Run.ActivateRange,       1.0f),
                    new Keyframe(s.Run.ActivateRange + 8f,  2.5f)),
            },

            // ---- 全距離：歩いて接近（中距離まで有効、遠いと Run 優先） ----
            new ActionGroupEntry
            {
                groupType        = ActionGroupType.Walk,
                baseWeight       = 25f,
                cooldown         = s.Walk.Cooldown,
                enableConditions = new Condition[] { new PlayerFarCondition(s.Walk.ActivateRange) },
                distanceCurve    = new AnimationCurve(
                    new Keyframe(s.Walk.ActivateRange,              1.5f),
                    new Keyframe(s.Run.ActivateRange + 3f,          0.5f)),
            },
        };
    }

    // テールレインの出現条件。距離帯に加え、Phase2Only なら Phase2 条件を足す。
    private static Condition[] BuildTailRainConditions(TailRainActionConfig cfg)
    {
        var list = new List<Condition>
        {
            new PlayerFarCondition(cfg.MinRange),
            new PlayerNearCondition(cfg.MaxRange),
        };
        if (cfg.Phase2Only) list.Add(new Phase2Condition());
        return list.ToArray();
    }

    private static ActionGroupEntry FallbackEntry() => new ActionGroupEntry
    {
        groupType        = ActionGroupType.IdleApproach,
        baseWeight       = 1f,
        enableConditions = null
    };
}
