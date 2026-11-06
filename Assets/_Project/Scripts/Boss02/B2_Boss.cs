/**===========================================================================

 File   : [B2_Boss.cs]
 Desc   : Boss02のボスのメインクラス

 Author : Yokoyama Haruki
 Date   : 2026/4/23
 -----------------------------------------------------------------------------
 �Q�l:WeaponSummon.cs
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_Boss : MonoBehaviour
    {

        [SerializeField] Camera mainCamera;
        [SerializeField] float shakeFrequency = 50f; // 振動の周波数
        [SerializeField] Animator animator;

        [Header("出現演出")]
        [Tooltip("女王出現時のカメラ演出。完了後に戦闘開始。未設定なら即戦闘開始")]
        [SerializeField] B2_QueenIntroCamera introCamera;
        [Tooltip("ONなら Start で自動的に演出を再生する。\n" +
                 "プレイヤーがフィールドに入った時に始めたい場合は OFF にして、\n" +
                 "B2_BossRoomTrigger から StartIntro() を呼ぶこと")]
        [SerializeField] bool playIntroOnStart = true;
        [Tooltip("introCamera より前に順番に再生するショット。\n" +
                 "ボス自身(プレハブ)の中にあるトラックを並べる。\n" +
                 "シーン側のショットは B2_BossRoomTrigger の Shots に入れること\n" +
                 "(プレハブはシーンのオブジェクトを参照できないため、この2段構えになっている)")]
        [SerializeField] List<CinemaTrack> preIntroShots = new List<CinemaTrack>();

        [Header("土台破壊(ギミック)")]
        [SerializeField] AudioSource seSource;
        [Tooltip("土台が壊れた瞬間のSE (Boss02_Base_Break)")]
        [SerializeField] AudioClip baseBreakSe;
        [Tooltip("シャンデリア直撃のダメージを与える先。未設定なら子から自動取得")]
        [SerializeField] B2_Health health;

        [Header("第二形態(土台なし状態)")]
        [Tooltip("スタンが明けて土台なし状態=第二形態に入る瞬間のSE (Boss02_Transform_Phase2)")]
        [SerializeField] AudioClip phase2TransformSe;

        [Header("撃破")]
        [Tooltip("HPが0になって倒れた瞬間のSE (Boss02_Defeated)")]
        [SerializeField] AudioClip defeatedSe;
        [Tooltip("撃破時に止めるギミック。スイッチの再出現を止める。未設定ならシーンから自動取得")]
        [SerializeField] B2_ChandelierGimmick chandelierGimmick;
        [Tooltip("シャンデリアが当たる中央判定の半径(m)。ボスがステージ中心からこの距離以内に\n" +
                 "いるときだけシャンデリアが当たる(そういうギミック)。離れていれば空振り")]
        [SerializeField] float chandelierHitRadius = 6f;

        [Header("被弾リアクション")]
        [Tooltip("被弾時のヒットストップ時間(秒)。0で無効")]
        [SerializeField] float damageHitStop = 0.06f;
        [Tooltip("被弾時の振動の強さ。0で無効")]
        [SerializeField] float damageShakePower = 0.05f;
        [Tooltip("被弾時の振動の長さ(秒)")]
        [SerializeField] float damageShakeDuration = 0.12f;

        // 出現演出が完了して戦闘(アクション選択)を始めてよいか
        private bool battleStarted = true;
        // 演出を二重に開始しないためのフラグ
        private bool introStarted = false;
        // 第二形態の演出を二度鳴らさないためのフラグ
        private bool phase2Presented = false;

        /// <summary>出現演出が終わって戦闘が始まっているか(ギミックのクールタイム起点)</summary>
        public bool BattleStarted => battleStarted;

        public B2_ActionController ActionController { get; private set; }
        public B2_StateMachine StateMachine { get; private set; }
        public B2_Settings Settings { get; private set; }

        public B2_Animator Animator { get; private set; }

        /// <summary>土台が破壊済みか。HP到達とシャンデリアのどちらが起点でも立つ</summary>
        public bool IsBaseBroken { get; private set; }

        public bool isHitStop { get; private set; } = false;
        private float hitStopDuration = 0f;
        private float hitStopTimer = 0f;

        private bool isShaking = false;
        // 土台破壊のバク宙で座標を動かしている最中。この間は被弾シェイクを止める
        // (シェイクは transform.position を毎フレーム上書きするので食い合う)
        private bool isBackflipping = false;
        private float shakeTimer = 0f;
        private float shakeTimerDuration = 0f;
        private float shakePower = 0f;
        // 揺れ始める前の姿勢。Transform 参照ではなく「値」で持つこと。
        // (参照で持つと transform 自身を指すので、復帰時に自分へ自分を代入して何も戻らない)
        private Vector3 shakeBasePosition;
        private Quaternion shakeBaseRotation;
        private Vector3 shakeBaseScale;

        // debug
        public float actionTime { get; private set; } = 3f;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            Settings = GetComponent<B2_Settings>();
            if (health == null) health = GetComponentInChildren<B2_Health>();
            ActionController = new B2_ActionController(this);
            StateMachine = new B2_StateMachine(this);
            Animator = new B2_Animator(animator);

            isHitStop = false;
            hitStopDuration = 0f;
            hitStopTimer = 0f;

            if (chandelierGimmick == null) chandelierGimmick = FindObjectOfType<B2_ChandelierGimmick>();

            if (health != null)
            {
                health.onDied.AddListener(Die);
                health.onHPChanged.AddListener(OnDamaged);
            }

            // 出現演出があれば、ムービー状態にして戦闘を止め、演出再生。完了後に戦闘開始。
            if (introCamera != null)
            {
                // 演出が終わるまで戦闘は止めておく。
                // playIntroOnStart が OFF の場合は、B2_BossRoomTrigger が
                // StartIntro() を呼ぶまでこの状態で待機する。
                battleStarted = false;
                StateMachine.ChangeState(StateMachine.movieState);

                // 演出中は無敵。落下してくる女王を殴れてしまうと演出が壊れる
                if (health != null) health.Invulnerable = true;

                if (playIntroOnStart) StartIntro();
            }
        }

        // 被弾リアクション(ヒットストップ+振動)。B2_Health.onHPChanged から呼ばれる。
        private void OnDamaged(int current, int max)
        {
            // 演出中・ダウン後は揺らさない。落下出現などで座標を動かしている最中に
            // 振動を混ぜると、演出側の座標と食い合って位置がズレる
            if (!battleStarted) return;
            if (StateMachine.IsInState<B2_DownedState>()) return;
            // バク宙で座標を動かしている最中も同じ理由で揺らさない。
            // 揺らすと shakeBasePosition(飛んでいる途中の座標)へ引き戻され、
            // 着地できずに空中で固まる
            if (isBackflipping) return;

            if (damageHitStop > 0f) HitStop(damageHitStop);
            if (damageShakePower > 0f) ShakeTransform(damageShakePower, damageShakeDuration);
        }

        /// <summary>
        /// HPが0になった時に呼ばれる(B2_Health.onDied)。無限ダウンへ移行する。
        /// とどめは B2_FinishingBlow のムービーが刺すので、ここでは消さない。
        /// </summary>
        private void Die()
        {
            if (StateMachine.IsInState<B2_DownedState>()) return;

            if (seSource != null && defeatedSe != null) seSource.PlayOneShot(defeatedSe);

            // スイッチの再出現を止める。倒れた後にギミックが回り続けると邪魔になる
            if (chandelierGimmick != null) chandelierGimmick.enabled = false;

            StateMachine.ChangeState(StateMachine.downedState);
        }

        /// <summary>
        /// 第二形態(土台なし状態)へ入る瞬間の演出。B2_OffBaseState.Enter から呼ばれる。
        /// 変身SEを鳴らし、BGMを2曲目へ切り替える。
        /// </summary>
        public void PlayPhase2Presentation()
        {
            if (phase2Presented) return;   // 土台なし状態へ入り直しても二度は鳴らさない
            phase2Presented = true;

            if (seSource != null && phase2TransformSe != null) seSource.PlayOneShot(phase2TransformSe);

            // BGM切替。プレハブからシーンのオブジェクトは参照できないため型で探す
            B2_BossBgm bgm = FindObjectOfType<B2_BossBgm>();
            if (bgm != null) bgm.SwitchToPhase2();
        }

        /// <summary>
        /// 登場演出を開始する。プレイヤーがフィールドに入った時に
        /// B2_BossRoomTrigger から呼ぶ。完了すると戦闘が始まる。
        /// </summary>
        public void StartIntro()
        {
            if (introStarted) return;
            introStarted = true;

            PlayPreIntroShot(0);
        }

        // preIntroShots を先頭から1本ずつ再生し、終わったら introCamera へ繋ぐ。
        // CinemaTrack.Play(onComplete) の完了コールバックで数珠つなぎにする。
        private void PlayPreIntroShot(int index)
        {
            while (index < preIntroShots.Count && preIntroShots[index] == null) index++;

            if (index >= preIntroShots.Count)
            {
                PlayIntroCamera();
                return;
            }

            preIntroShots[index].Play(() => PlayPreIntroShot(index + 1));
        }

        private void PlayIntroCamera()
        {
            if (introCamera == null)
            {
                OnIntroComplete(); // 演出が無いなら即戦闘へ
                return;
            }

            // CinemaTrack の Play On Start が ON だと、Start() の実行順によっては
            // 向こうが先に Play()(コールバック無し) してしまい、こちらの
            // Play(OnIntroComplete) が「再生中」として弾かれて完了通知が失われる。
            // そうなると battleStarted が false のままでボスが一切行動しなくなるため、
            // 無言で壊れないよう警告する。
            if (introCamera.IsPlaying)
            {
                Debug.LogWarning("[B2_Boss] introCamera が既に再生中です。" +
                    "B2_QueenIntroCamera の Play On Start を OFF にしてください。" +
                    "ON のままだと演出が終わっても戦闘が始まりません。", this);
            }

            introCamera.Play(OnIntroComplete);
        }

        // 出現演出の完了コールバック。戦闘状態(onBase)へ移行してアクション選択を解禁する。
        // カメラが TPS へ戻るのと同じタイミングで HUD をシール貼り演出で出す。
        private void OnIntroComplete()
        {
            battleStarted = true;
            StateMachine.ChangeState(StateMachine.onBaseState);

            // 演出が終わったので殴られるようになる
            if (health != null) health.Invulnerable = false;

            // B2_BossRoomTrigger が HideUntilRevealed() で隠していた場合のみ効く。
            // 隠していなければ内部で何も起きない(HUD が無いシーンでも安全)。
            HUDIntroReveal.RevealNow();
            // 黒帯は B2_QueenIntroCamera.OnBegin(=名前表示の寸前)で既に閉じ始めている

            // ボスHPバーを出す。HUDのシール貼りが終わる頃に出るよう、
            // 向こう側で待ち時間(delay)を持たせている。
            // (プレハブからシーンのオブジェクトは参照できないため型で探す)
            B2_BossHPBarReveal hpBar = FindObjectOfType<B2_BossHPBarReveal>();
            if (hpBar != null) hpBar.Reveal();
        }

        /// <summary>
        /// 土台を破壊してスタンさせる。入口は2つあり、どちらが先でも同じ流れになる:
        ///   - シャンデリア着弾 (B2_Chandelier.onImpact)
        ///   - HP が baseHealthRatio を下回る (B2_Health.TakeDamage)
        /// スタンが解けると B2_StunState 側で土台なし状態へ移行する。
        /// 一度きり。先に片方で壊れていれば、もう片方が来ても何もしない(スタンの二重掛けを防ぐ)。
        /// </summary>
        public void BreakBaseAndStun()
        {
            if (IsBaseBroken) return;
            IsBaseBroken = true;

            if (seSource != null && baseBreakSe != null) seSource.PlayOneShot(baseBreakSe);

            // 土台あり用のオブジェクトはここで消すが、接地はしない。
            // 地面へはバク宙の着地で降りるので、ここで BreakBase() を呼んで
            // ワープさせてしまうと弧が始点から描けない
            HideOnBaseObjects();

            // 先にスタンへ入れて行動を止める(ActionController はスタン中は回らない)。
            // バク宙の間もスタン扱いにしたいので、状態としての長さは
            // 「バク宙 + 着地後のスタン」にする。こうすると Settings.StunDuration が
            // そのまま『着地してからスタンしている時間』になる
            Stun(Settings.BackflipDuration + Settings.StunDuration);
            StartCoroutine(BackflipToGroundRoutine());
        }

        /// <summary>
        /// 土台が壊れた瞬間、後ろへバク宙して地面へ降りる。
        /// 着地後もスタンは残る (BreakBaseAndStun がその分を上乗せしているため)。
        /// </summary>
        private IEnumerator BackflipToGroundRoutine()
        {
            isBackflipping = true;

            // 被弾シェイクを必ず切る。Update() が毎フレーム
            //   transform.position = shakeBasePosition + …
            // で座標を上書きするため、生かしたままだと動かせない。
            // しかも土台を割った一撃の OnDamaged が shakeBasePosition に
            // 「土台の上の高さ」を記録済みなので、放置すると空中で固まる
            isShaking = false;

            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 back = -transform.forward * Settings.BackflipBackDistance;

            // 着地点 = 後ろへ下がった先の地面。OffBaseTransform (足元) を基準に測り、
            // その差分だけボス本体を動かす
            Vector3 probe = (Settings.OffBaseTransform != null
                                ? Settings.OffBaseTransform.position
                                : startPos) + back;
            Vector3 landPos = startPos + back;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit,
                                100f, LayerMask.GetMask("Ground")))
            {
                landPos = startPos + back + (hit.point - probe);
            }

            float dur = Mathf.Max(0.01f, Settings.BackflipDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);

                // 水平は等速。縦は放物線を足して跳ね上がってから落ちる (p=0.5 が頂点)
                Vector3 pos = Vector3.Lerp(startPos, landPos, p);
                pos.y += Settings.BackflipApexHeight * 4f * p * (1f - p);
                transform.position = pos;

                // 後ろ向きの回転 = ローカル右軸まわりのマイナス回転
                transform.rotation = startRot
                    * Quaternion.AngleAxis(-360f * Settings.BackflipSpins * p, Vector3.right);
                yield return null;
            }

            transform.position = landPos;
            transform.rotation = startRot;
            isBackflipping = false;
        }

        // 土台あり用のオブジェクトを消す。二度呼んでも安全。
        private void HideOnBaseObjects()
        {
            foreach (var obj in Settings.OnBaseObjects)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        /// <summary>
        /// シャンデリア着弾時の処理。B2_Chandelier.onImpact からはこちらを呼ぶ。
        /// 土台の状態で効果が変わる:
        ///   - 土台が無事   : 土台破壊 + 大幅スタン(StunDuration)
        ///   - 土台が破壊済 : 現在HPの ChandelierPostBreakDamageRatio 倍のダメージ
        ///                    + ChandelierPostBreakStun 秒のスタン
        /// </summary>
        public void HitByChandelier()
        {
            if (health == null) return;

            // シャンデリアは中央にいるときだけ当たる(そういうギミック)。
            // ボスがステージ中心(原点)から離れていれば空振りして何もしない。
            Vector2 planar = new Vector2(transform.position.x, transform.position.z);
            if (planar.magnitude > chandelierHitRadius) return;

            if (!IsBaseBroken)
            {
                // 土台が無事なら、その時のHPに応じた所まで一気に削る。
                //   HP 3/4 以上        → 半分(HighHpTarget)まで
                //   HP 3/4 未満        → 1/4(MidHpTarget)まで
                // 削った結果 BaseHealthRatio を下回るので、B2_Health.TakeDamage の中から
                // BreakBaseAndStun() が呼ばれ、バク宙 → StunDuration 秒のスタンになる。
                float ratio = (float)health.CurrentHP / Mathf.Max(1, health.MaxHP);
                float target = ratio >= Settings.ChandelierHighHpThreshold
                    ? Settings.ChandelierHighHpTarget
                    : Settings.ChandelierMidHpTarget;

                int targetHp = Mathf.RoundToInt(health.MaxHP * target);
                int damage = Mathf.Max(0, health.CurrentHP - targetHp);
                if (damage > 0) health.TakeDamage(damage);

                // 削り先が BaseHealthRatio を上回る設定でも、シャンデリアは必ず土台を壊す。
                // (TakeDamage 側で既に壊れていれば、この呼び出しはガードされて何もしない)
                if (!health.IsDead && !IsBaseBroken) BreakBaseAndStun();
                return;
            }

            // 土台破壊後: 『現在HP』の一定割合だけ削って短めのスタン。
            // 端数は切り上げる。切り捨てだと低HP時に 0 ダメージになって永久に減らせなくなる
            int postDamage = Mathf.CeilToInt(health.CurrentHP * Settings.ChandelierPostBreakDamageRatio);
            if (postDamage > 0) health.TakeDamage(postDamage);
            if (health.IsDead) return; // 倒しきったならスタンさせない(無限ダウンへ譲る)

            Stun(Settings.ChandelierPostBreakStun);
        }

        /// <summary>指定時間のスタンに入る。スタン時間はここで必ず渡す。</summary>
        private void Stun(float duration)
        {
            StateMachine.stunState.SetDuration(duration);
            StateMachine.ChangeState(StateMachine.stunState);
        }

        /// <summary>
        /// 土台を消して地面へ降ろす。土台あり用オブジェクトを非アクティブにし、
        /// OffBaseTransform の真下の地面へボスを落とす。
        /// (BreakBaseAndStun と B2_OffBaseState の両方から呼ばれる。二度呼んでも安全)
        /// </summary>
        public void BreakBase()
        {
            HideOnBaseObjects();

            // バク宙経由(BreakBaseAndStun)で既に降りている場合、ここの補正は差分0になる。
            // B2_OffBaseState から念のため呼ばれる保険の経路
            if (isBackflipping) return;
            if (Settings.OffBaseTransform == null) return;

            if (Physics.Raycast(
                Settings.OffBaseTransform.position,
                Vector3.down,
                out RaycastHit hit,
                100f, LayerMask.GetMask("Ground")))
            {
                transform.position += hit.point - Settings.OffBaseTransform.position;
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (!isHitStop)
            {
                // 出現演出中(battleStarted=false)・スタン中・ダウン後はアクションを選択・実行しない
                if (battleStarted
                    && !StateMachine.IsInState<B2_StunState>()
                    && !StateMachine.IsInState<B2_DownedState>())
                {
                    ActionController.Update();
                }
            }
            else
            {
                hitStopTimer += Time.deltaTime;
                if (hitStopTimer >= hitStopDuration)
                {
                    isHitStop = false;
                    hitStopTimer = 0f;
                }
            }
            StateMachine.Update();

            if(isShaking)
            {
                shakeTimer += Time.deltaTime;

                float currentPower = shakePower * (1 - shakeTimer / shakeTimerDuration); // 徐々に減衰する振動の強さ
                Vector3 camRight = mainCamera != null ? mainCamera.transform.right : Vector3.right;
                float sin = Mathf.Sin(Time.time * shakeFrequency) * currentPower; // 振動の周期を調整
                // 基準位置からのオフセットとして毎フレーム置き直す。
                // += で加算していくと、揺れ幅が累積してボスが流されていく
                transform.position = shakeBasePosition + camRight * sin;

                if (shakeTimer >= shakeTimerDuration)
                {
                    isShaking = false;
                    shakeTimer = 0f;
                    transform.position = shakeBasePosition;   // 振動前の姿勢に戻す
                    transform.rotation = shakeBaseRotation;
                    transform.localScale = shakeBaseScale;
                }
            }
        }

        public void HitStop(float duration)
        {
            isHitStop = true;
            hitStopDuration = duration;
            hitStopTimer = 0f;
        }

        public void ShakeTransform(float power, float duration)
        {
            // 揺れ中に再度呼ばれても、基準は「最初に揺れ始めた時の姿勢」を保つ。
            // 揺れた後の位置を基準にすると、連続被弾でボスがどんどんズレていく
            if (!isShaking)
            {
                shakeBasePosition = transform.position;
                shakeBaseRotation = transform.rotation;
                shakeBaseScale = transform.localScale;
            }
            shakePower = power;
            shakeTimerDuration = Mathf.Max(0.01f, duration);
            shakeTimer = 0f;
            isShaking = true;
        }

            void StartAction()
        {
            Debug.Log("Starting Boss Actions");
            ActionController.RequestNextAction();
        }
    }
}
