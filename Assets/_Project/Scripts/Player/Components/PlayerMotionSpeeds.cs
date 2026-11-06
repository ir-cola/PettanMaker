/**===========================================================================

 File   : プレイヤー全モーション再生速度 [PlayerMotionSpeeds.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 プレイヤーの各モーションの再生速度倍率を 1 か所で調整するコンポーネント。
 各 Animator ステートの速度パラメータ (LocoSpeed/JumpSpeed/.../AttackSpeed) を
 毎フレーム流し込む (Inspector 値を Play 中でも反映)。1 = 等倍。
 チャージ下半身は前進/後退の符号が絡むため StateGrounded が chargeMove を
 読んで LowerSpeed へ反映する。
 ===========================================================================**/

using UnityEngine;

public class PlayerMotionSpeeds : MonoBehaviour {

    [Header("各モーションの再生速度倍率 (1 = 等倍)")]
    [Tooltip("Idle / Walk / Run (通常ロコモーション)")]
    [SerializeField] private float locomotion = 1f;
    [SerializeField] private float jump   = 1f;
    [SerializeField] private float fall   = 1f;
    [SerializeField] private float dodge  = 1f;
    [SerializeField] private float charge = 1f;
    [Tooltip("攻撃モーション (旧 PlayerAttack.attackAnimationSpeed を移行。2 = 2倍速)")]
    [SerializeField] private float attack = 2f;

    [Header("コンボ 段ごとの再生速度 (全体倍率 1=等倍)")]
    [Tooltip("1段目 (Chain_Attack_0) の全体再生速度")]
    [SerializeField] private float combo1 = 1f;
    [Tooltip("2段目 (Chain_Attack_1) の全体再生速度")]
    [SerializeField] private float combo2 = 1f;
    [Tooltip("3段目 (Attack_1) の全体再生速度")]
    [SerializeField] private float combo3 = 1f;

    [Header("コンボ 段ごとの速度カーブ (X=モーション進行0→1, Y=速度倍率。全体倍率に乗算)")]
    [Tooltip("1段目: 振り始め遅く/斬る瞬間速く 等を細かく設定 (平らな1で無効)")]
    [SerializeField] private AnimationCurve combo1Curve = AnimationCurve.Constant(0f, 1f, 1f);
    [Tooltip("2段目の速度カーブ")]
    [SerializeField] private AnimationCurve combo2Curve = AnimationCurve.Constant(0f, 1f, 1f);
    [Tooltip("3段目の速度カーブ")]
    [SerializeField] private AnimationCurve combo3Curve = AnimationCurve.Constant(0f, 1f, 1f);

    [Header("チャージ中の下半身 (歩行)")]
    [Tooltip("チャージ移動の脚の再生速度倍率。横移動ほどこの値へ寄る (前進は等倍)。")]
    [SerializeField] private float chargeMove = 1.5f;

    /// <summary>チャージ移動の脚速度倍率 (StateGrounded が符号付きで LowerSpeed へ反映)。</summary>
    public float ChargeMove => chargeMove;

    private Animator anim;

    private void Awake() {
        anim = GetComponent<Animator>();
    }

    private void Update() {
        if (anim == null) return;
        // 各ステートの速度パラメータへ流し込む (Inspector 値を Play 中でも反映)。
        anim.SetFloat(PlayerAnimator.LocoSpeedParam,   locomotion);
        anim.SetFloat(PlayerAnimator.JumpSpeedParam,   jump);
        anim.SetFloat(PlayerAnimator.FallSpeedParam,   fall);
        anim.SetFloat(PlayerAnimator.DodgeSpeedParam,  dodge);
        anim.SetFloat(PlayerAnimator.ChargeSpeedParam, charge);
        anim.SetFloat(PlayerAnimator.AttackSpeedParam, attack);

        // コンボ再生中は、再生中の段の「全体倍率 × 速度カーブ(進行)」を ComboSpeed へ。
        // 段の途中でも速度を細かく変えられる (振り始め遅く→斬る瞬間速く 等)。
        var st = anim.GetCurrentAnimatorStateInfo(0);
        int c = st.IsName("ComboAttack0") ? 0
              : st.IsName("ComboAttack1") ? 1
              : st.IsName("ComboAttack2") ? 2 : -1;
        if (c >= 0)
            anim.SetFloat(PlayerAnimator.ComboSpeedParam, ComboSpeedAt(c, Mathf.Clamp01(st.normalizedTime)));

        // LowerSpeed (チャージ下半身) は前進/後退の符号が絡むため StateGrounded 側で設定。
    }

    /// <summary>コンボ段 i の速度 = 全体倍率 × 速度カーブ(進行 nt)。カーブが空なら倍率のみ。</summary>
    private float ComboSpeedAt(int i, float nt) {
        float scalar        = i == 0 ? combo1 : i == 1 ? combo2 : combo3;
        AnimationCurve curve = i == 0 ? combo1Curve : i == 1 ? combo2Curve : combo3Curve;
        float shape = (curve != null && curve.length > 0) ? curve.Evaluate(nt) : 1f;
        return Mathf.Max(0.01f, scalar * shape);
    }
}
