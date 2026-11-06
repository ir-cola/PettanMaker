/**===========================================================================

 File   : プレイヤー中央オーケストレーション [PlayerLifetimeScope.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 Player GameObject のエントリポイント (VContainer の LifetimeScope)。
 - 基本ステータス (移動/ジャンプ/回避) を Inspector に保持 (IPlayerConfig)
 - 各コンポーネント (Mover/Animator/Health/Attack/Input/StateMachine) を生成・公開 (IPlayerContext)
 - DI コンテナを構築し、IPlayerContext / IPlayerConfig を注入可能にする
 コンポーネント生成・更新ループは Awake / Update で手動 (states は IPlayerContext 経由)。
 ===========================================================================**/

using UnityEngine;
using VContainer;
using VContainer.Unity;

[RequireComponent(typeof(Animator), typeof(CharacterController))]
public class PlayerLifetimeScope : LifetimeScope, IPlayerContext {

    [Header("設定アセット")]
    [Tooltip("プレイヤー設定 (ScriptableObject)。移動/回避/ジャンプ等のチューニング値はこのアセットで調整する。未アサイン時はコード既定値で動作")]
    [SerializeField] private PlayerSettings settings;

    // チューニング値は PlayerSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値 (= Cheshire シーンの調整値)。
    private float speed = 9.0f;
    private float gravity = -9.81f;
    private float rotationSpeed = 10.0f;
    private float moveDrag = 7.0f;              // 滑り出しの鋭さ (初期加速 = Speed × この値)
    private float walkFloor = 0.3f;             // 最小入力時の速度倍率 (歩き)
    private float inertiaDecay = 6.0f;          // 入力を離した後の慣性スライド減衰
    private float quickTurnSpeed = 720f;        // 向き直り速度 (度/秒、>90°)
    private float mediumTurnSpeed = 540f;       // 向き直り速度 (度/秒、45〜90°)
    private float rotEaseSpeed = 8f;            // 角度差 45°未満の指数イーズ速度
    private float attackRecoveryTime = 0.40f;   // 攻撃発動後の移動ロック追加時間 (秒)
    private float chargeMoveSpeedMul = 0.40f;   // チャージ中の移動速度倍率
    private float dodgeDistance = 2.5f;         // 回避の移動距離 (m)
    private float dodgeMultiplier = 1.5f;       // 回避の速度倍率 (Speed × この値)
    private float dodgeCooldown = 0.5f;         // 回避のクールタイム (秒)
    private float jumpHeight = 1.8f;
    private float groundCheckDistance = 0.3f;
    private float bufferTime = 0.2f;            // インプットバッファ (秒)

    // 速度倍率（アイテム効果用ランタイム値）
    private float speedMultiplier = 1.0f;
    // 体格スキル(アリス小/巨大)による絶対移動速度の上書き。< 0 = 上書き無し(既定 speed を使う)。
    // 服スキル(PlayerClothSkills)が毎フレーム設定/解除する。アイテムの speedMultiplier とは
    // 直交して乗算合成される (小さくてもアイテムで加速はできる)。
    private float sizeSpeedOverride = -1f;
    // 体格スキル(アリス小/巨大)によるジャンプ力の倍率 (1 = 既定 jumpHeight のまま)。
    // 服スキル(PlayerClothSkills)が毎フレーム設定する。
    private float sizeJumpMul = 1f;

    // ===== IPlayerConfig (設定アクセサ) =====
    public float Speed                 => (sizeSpeedOverride >= 0f ? sizeSpeedOverride : speed) * speedMultiplier;
    public float Gravity               => gravity;
    public float RotationSpeed         => rotationSpeed;
    public float JumpHeight            => jumpHeight * sizeJumpMul;
    public float DodgeDistance         => dodgeDistance;
    public float DodgeMultiplier       => dodgeMultiplier;
    public float DodgeCooldown         => dodgeCooldown;
    public float GroundCheckDistance   => groundCheckDistance;
    public float BufferTime            => bufferTime;
    public float SpeedMultiplier       => speedMultiplier;
    public float MoveDrag              => moveDrag;
    public float WalkFloor             => walkFloor;
    public float InertiaDecay          => inertiaDecay;
    public float QuickTurnSpeed        => quickTurnSpeed;
    public float MediumTurnSpeed       => mediumTurnSpeed;
    public float RotEaseSpeed          => rotEaseSpeed;
    public float AttackRecoveryTime    => attackRecoveryTime;
    public float ChargeMoveSpeedMul    => chargeMoveSpeedMul;

    public void SetSpeedMultiplier(float m) {
        speedMultiplier = Mathf.Max(0f, m);
    }

    /// <summary>体格スキルによる絶対移動速度の上書き (アリス小=7 / 巨大=10 等)。負値で解除して既定 speed へ戻す。</summary>
    public void SetSizeSpeedOverride(float absoluteSpeed) {
        sizeSpeedOverride = absoluteSpeed;
    }

    /// <summary>体格スキルによるジャンプ力の倍率 (アリス巨大=1.5 等)。1 で既定 jumpHeight のまま。</summary>
    public void SetSizeJumpMul(float mul) {
        sizeJumpMul = Mathf.Max(0f, mul);
    }

    // ===== IPlayerContext (コンポーネント) =====
    public PlayerInputHandler Input        { get; private set; }
    public PlayerStateMachine StateMachine { get; private set; }
    public PlayerMover Mover               { get; private set; }
    public PlayerAnimator Animator         { get; private set; }
    public PlayerHealth Health             { get; private set; }
    public PlayerAttack Attack             { get; private set; }
    public PlayerMotionSpeeds MotionSpeeds { get; private set; }
    public PlayerClothSkills ClothSkills   { get; private set; }
    public Player_SoundManager Sound       { get; private set; }
    private PlayerFootIK footIK;
    private bool isDead;

    /// <summary>設定アセット (PlayerSettings) の値をシーン上のフィールドへ反映する。</summary>
    private void ApplySettings() {
        speed              = settings.Speed;
        gravity            = settings.Gravity;
        rotationSpeed      = settings.RotationSpeed;
        moveDrag           = settings.MoveDrag;
        walkFloor          = settings.WalkFloor;
        inertiaDecay       = settings.InertiaDecay;
        quickTurnSpeed     = settings.QuickTurnSpeed;
        mediumTurnSpeed    = settings.MediumTurnSpeed;
        rotEaseSpeed       = settings.RotEaseSpeed;
        attackRecoveryTime = settings.AttackRecoveryTime;
        chargeMoveSpeedMul = settings.ChargeMoveSpeedMul;
        dodgeDistance      = settings.DodgeDistance;
        dodgeMultiplier    = settings.DodgeMultiplier;
        dodgeCooldown      = settings.DodgeCooldown;
        jumpHeight         = settings.JumpHeight;
        groundCheckDistance = settings.GroundCheckDistance;
        bufferTime         = settings.BufferTime;
    }

    protected override void Awake() {
        // 設定アセットの値を反映する (未アサイン時はコード既定値のまま動作)。
        if (settings != null) ApplySettings();
        else Debug.LogWarning("[PlayerLifetimeScope] PlayerSettings が未アサインです。コード既定値で動作します。", this);

        // コンポーネント生成 (DI コンテナ構築より前に行い、Configure で登録する)。
        // ※ Input は IPlayerMover を要するため Mover を先に生成する。
        Mover        = gameObject.AddComponent<PlayerMover>();
        Animator     = new PlayerAnimator(GetComponent<Animator>());
        Input        = new PlayerInputHandler(this, Mover);
        Health       = GetComponent<PlayerHealth>();
        Attack       = GetComponent<PlayerAttack>();
        MotionSpeeds = GetComponent<PlayerMotionSpeeds>();
        ClothSkills  = GetComponent<PlayerClothSkills>();   // 無いシーンもある (null 許容)
        Sound        = GetComponent<Player_SoundManager>();
        StateMachine = new PlayerStateMachine();
        if (Health != null)
            Health.onDied.AddListener(HandleDeath);
        if (Sound != null)
            Sound.Initialize(this);
        else
            Debug.LogWarning("[PlayerLifetimeScope] Player_SoundManagerが未設定です。PlayerのSEは再生されません。", this);
        // FootIK: シーンに常設されていればそれを使い、無ければ実行時に追加する (Mover と同パターン)。
        footIK = GetComponent<PlayerFootIK>();
        if (footIK == null) footIK = gameObject.AddComponent<PlayerFootIK>();
        footIK.Initialize(this);
        // ※ PlayerChargeTwist は Inspector 調整できるようシーンに常設 (実行時 Add しない)。
        //   Configure の RegisterComponentInHierarchy で検出・注入する。

        // VContainer: コンテナ構築 (Configure) + エントリポイント開始。
        // ※ MonoBehaviour から LifetimeScope へ基底を変えたため、シーンに保存済みの
        //   コンポーネントでは autoRun が既定 false でデシリアライズされ得る。未構築なら明示 Build。
        base.Awake();
        if (Container == null) Build();

        StateMachine.ChangeLocomotionState(new StateGrounded(this));
    }

    private void Update() {
        if (isDead) return;

        Mover.CheckGround();
        Input.Update();
        StateMachine.Update();
    }

    private void HandleDeath() {
        if (isDead) return;
        isDead = true;

        // 現在の攻撃・回避ステートを抜けて後処理を走らせたうえで、入力と慣性を止める。
        StateMachine.ChangeActionState(new StatePlayerNull());
        Input.SetUIMode(true);
        Mover.SetUIMode(true);
        Attack?.SetUIMode(true);
        Sound?.StopCharge();

        // 他の AnyState トリガーが死亡遷移と競合しないよう整理してから Death を再生する。
        Animator.ResetTrigger(PlayerAnimator.AttackParam);
        Animator.ResetTrigger(PlayerAnimator.ChargeParam);
        Animator.ResetTrigger(PlayerAnimator.ComboParam);
        Animator.ResetTrigger(PlayerAnimator.ToMoveParam);
        Animator.ResetTrigger(PlayerAnimator.KnockBackStartParam);
        Animator.SetBool(PlayerAnimator.IsRollParam, false);
        Animator.SetBool(PlayerAnimator.IsKnockBackParam, false);
        Animator.SetBool(PlayerAnimator.IsKnockBackForWallParam, false);
        Animator.SetFloat(PlayerAnimator.MoveParam, 0f);
        Animator.SetFloat(PlayerAnimator.VerticalSpeedParam, 0f);
        Animator.SetTrigger(PlayerAnimator.DeathParam);
    }

    protected override void Configure(IContainerBuilder builder) {
        // 自身 (LifetimeScope) を IPlayerContext / IPlayerConfig として公開する。
        // ※ VContainer は LifetimeScope を具象 PlayerLifetimeScope として自動登録するため、
        //   RegisterInstance(this) で再登録すると実装型が衝突して Build が落ちる。
        //   ファクトリ登録 (_ => this) なら実装型は各 IF となり衝突しない。
        builder.Register<IPlayerContext>(_ => this, Lifetime.Singleton);
        builder.Register<IPlayerConfig>(_ => this, Lifetime.Singleton);
        // PlayerMover へ IPlayerConfig をメソッド注入 (Construct)。Awake で生成済みのインスタンスを登録。
        builder.RegisterComponent(Mover);
        // PlayerChargeTwist へ IPlayerContext を注入 (階層から検出)。
        builder.RegisterComponentInHierarchy<PlayerChargeTwist>();
    }

    /// <summary>ScaryWalk 中に下半身レイヤーだけ Idle に上書きするフラグ。StateGrounded が毎フレーム参照する。</summary>
    public bool ScaryWalkLowerIdle { get; private set; }

    /// <summary>ScaryWalk の下半身 Idle 上書きの ON/OFF。B1_CameraDirector のマップ演出 (EndPoint 到着時) が制御する。</summary>
    public void SetScaryWalkLowerIdle(bool on) {
        ScaryWalkLowerIdle = on;
    }

    /// <summary>固定カメラ移動モードの ON/OFF。ボスの見下ろし固定カメラ演出 (TailRain 等) が制御する。</summary>
    public void SetFixedCameraMove(bool on) {
        Mover.SetFixedCameraMove(on);
    }

    public void SetUIMode(bool uiMode) {
        bool locked = uiMode || isDead;
        Input.SetUIMode(locked);
        Mover.SetUIMode(locked);
        if (Attack != null) Attack.SetUIMode(locked);
    }
}
