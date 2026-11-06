/*=============================================================================

 File   : [B2_BossRoomTrigger.cs]
 Desc   : 女王の登場カットシーンの開始役。チェシャ猫(Boss01)の
          B1_BossRoomTrigger と同じ立ち位置で、Boss02 用。

          開始のきっかけは2通り(Start Mode):
            - PlayerLands  : プレイヤーが着地した瞬間(既定)。
                             ステージ開幕に上空から落ちてくる想定。
            - EnterTrigger : Collider(Is Trigger)へプレイヤーが侵入した瞬間。
                             使う場合はこの GameObject に Collider が必要。
          どちらも1回だけ発火する。

          流れ:
            きっかけ → プレイヤー凍結 + HUD非表示
                     → 暗転
                     → Shots を順に再生(暗転中にカメラが開始位置へ飛ぶので
                       ワープが見えない)
                     → 明転
                     → B2_Boss.StartIntro() でボス側のショットへ
                     → 完了で戦闘開始 + TPSカメラへ復帰(操作も戻る)

          ■ 複数ショット(切り替え)について
            CinemaTrack の Camera Cuts は「そのトラック全体」の設定なので、
            1本の中で「滑らかな移動」と「カット切り替え」を混ぜられない。
            ショットを切り替えたい場合は、ショットごとに CinemaTrack を作って
            Shots に順番に並べる。ここが1ショット=1カットになる。
            ※ ボス(プレハブ)の中にあるショットは、シーンから参照できないため
              B2_Boss の Pre Intro Shots 側に並べること。

          セットアップ(Inspector):
            - Boss        : B2_Boss (未設定なら親→シーンの順に自動取得)
            - Shots       : シーン側のカット(任意)。上から順に再生
            - Screen Fade : B2_ScreenFade (未設定なら暗転せず即開始)
            ※ B2_Boss 側の Play Intro On Start は OFF にすること

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    /// <summary>プレイヤーの着地(または侵入)で女王の登場カットシーンを開始する。</summary>
    public class B2_BossRoomTrigger : MonoBehaviour
    {
        public enum StartMode
        {
            /// <summary>プレイヤーが着地した瞬間(上空から落ちてくる開幕用)</summary>
            PlayerLands,
            /// <summary>Collider(Is Trigger)へ侵入した瞬間</summary>
            EnterTrigger,
        }

        [Header("開始のきっかけ")]
        [SerializeField] private StartMode startMode = StartMode.PlayerLands;
        [Tooltip("PlayerLands のとき、開幕の一瞬の接地で誤爆しないよう、\n" +
                 "この時間だけ空中に居てから着地した場合のみ発火する")]
        [SerializeField] private float minAirTimeBeforeLand = 0.2f;
        [Tooltip("ONなら開幕の落下中もプレイヤーを操作不可にする。\n" +
                 "入力(移動/攻撃)だけを殺し、重力は生かす。\n" +
                 "※ PlayerLifetimeScope.SetUIMode は Mover ごと止めてしまい、\n" +
                 "   ApplyGravity が return して落ちてこなくなるのでここでは使えない")]
        [SerializeField] private bool freezeInputDuringFall = true;

        [Header("開幕の明転")]
        [Tooltip("ONなら開始時は真っ暗にしておき、落下の開始と同時に明転する。\n" +
                 "※ Start Mode が PlayerLands のときだけ効く。EnterTrigger では\n" +
                 "   落下フェーズが無く明転する所が無いので、暗転したままになってしまう")]
        [SerializeField] private bool startFromBlack = true;
        [Tooltip("真っ暗のまま待つ時間(秒)")]
        [SerializeField, Min(0f)] private float startHoldBlack = 0.1f;
        [Tooltip("明るくなるまでの時間(秒)。\n" +
                 "待ち＋明転が落下時間を食い潰すと、明るくなった時にはもう着地している。\n" +
                 "落下時間は Player の開始 Y で決まる(重力 -9.81・上限なしの自由落下):\n" +
                 "  Y=22 → 約2.1秒 / Y=14.5 → 約1.7秒\n" +
                 "既定は 0.1+1.0=1.1秒で明転し切り、残り約1.0秒の落下を見せる配分")]
        [SerializeField, Min(0f)] private float startFadeInDuration = 1.0f;

        [Header("参照")]
        [SerializeField] private B2_Boss boss;
        [Tooltip("落下中プレイヤーを追うカメラ。未設定なら落下は演出せず素の画で落ちる")]
        [SerializeField] private B2_FallCamera fallCamera;
        [Tooltip("暗転に使う。未設定なら暗転せずそのまま始まる")]
        [SerializeField] private B2_ScreenFade screenFade;

        [Header("暗転")]
        [Tooltip("着地してから暗転を始めるまでの待ち(秒)。着地の余韻を見せる")]
        [SerializeField] private float delayBeforeFade = 0.5f;
        [Tooltip("暗くなるまでの時間(秒)")]
        [SerializeField] private float fadeOutDuration = 0.4f;
        [Tooltip("真っ暗のまま待つ時間(秒)。この間にカメラが演出開始位置へ飛ぶ")]
        [SerializeField] private float holdBlack = 0.15f;
        [Tooltip("明るくなるまでの時間(秒)")]
        [SerializeField] private float fadeInDuration = 0.5f;

        [Header("ショット")]
        [Tooltip("女王の登場演出より前に流すシーン側のカット。上から順に再生する。\n" +
                 "ショット間がカット切り替えになる(1ショット=1 CinemaTrack)")]
        [SerializeField] private List<CinemaTrack> shots = new();

        [Header("その他")]
        [Tooltip("カットシーン中プレイヤーの操作を凍結する。\n" +
                 "解除は演出完了時に B2_QueenIntroCamera(freezePlayer)が行う")]
        [SerializeField] private bool freezePlayer = true;
        [Tooltip("ONならカットシーン中は HUD を隠し、演出完了時にシール貼り演出で出す")]
        [SerializeField] private bool hideHudDuringCutscene = true;

        private bool triggered;
        private PlayerLifetimeScope player;
        private float airTime;
        private bool fallPhaseStarted;

        private void Awake()
        {
            if (boss == null) boss = GetComponentInParent<B2_Boss>();
            if (boss == null) boss = FindObjectOfType<B2_Boss>();
            if (screenFade == null) screenFade = FindBlackFade();
            if (fallCamera == null) fallCamera = FindObjectOfType<B2_FallCamera>();

            // 開幕は真っ暗から始め、落下の開始(Update の初回)で明転する。
            // 暗転板は遅延生成なので、この SetInstant がその生成も兼ねる。
            if (startFromBlack && startMode == StartMode.PlayerLands && screenFade != null)
                screenFade.SetInstant(1f);
        }

        // 暗転板を自動取得する。シーンには白転用(とどめ演出が使う)の B2_ScreenFade も
        // 居るため、素の FindObjectOfType では白を掴んで開幕が白画面になりうる。
        // 見分けが付くよう B2_ScreenFade が色を公開しているので、黒い方を選ぶ。
        private static B2_ScreenFade FindBlackFade()
        {
            foreach (var f in FindObjectsOfType<B2_ScreenFade>())
            {
                Color c = f.FadeColor;
                if (c.r < 0.5f && c.g < 0.5f && c.b < 0.5f) return f;
            }
            return FindObjectOfType<B2_ScreenFade>();
        }

        private void Update()
        {
            if (triggered || startMode != StartMode.PlayerLands) return;

            if (player == null)
            {
                player = FindObjectOfType<PlayerLifetimeScope>();
                if (player == null) return;
            }
            if (player.Mover == null) return;

            // 開幕の落下中は操作させない(重力は生かしたいので入力だけ殺す)。
            // PlayerLifetimeScope が Input を作り終えるのを待つため Update で行う。
            // 落下フェーズの初回セットアップ(プレイヤーを見つけた最初のフレーム)
            if (!fallPhaseStarted)
            {
                fallPhaseStarted = true;
                if (freezeInputDuringFall) SetInputFrozen(true);
                // 落下追従カメラを開始(後ろ上から見下ろす)
                if (fallCamera != null) fallCamera.Begin();
                // 開幕の明転。カメラが画角に付いてから明るくする(先に明転すると
                // TPSカメラの初期位置が一瞬映ってしまう)
                if (startFromBlack && screenFade != null) StartCoroutine(StartFadeInRoutine());
            }

            // 空中に居た時間を数え、着地の瞬間に発火する。
            // 開幕から接地しっぱなしの構成で即発火しないよう、
            // 一定時間の滞空を条件にしている。
            if (!player.Mover.IsGrounded)
            {
                airTime += Time.deltaTime;
                return;
            }

            if (airTime >= minAirTimeBeforeLand) Begin();
            airTime = 0f;
        }

        // 開幕の明転。落下が始まってから明るくするので、暗いうちに
        // プレイヤーが落ち切ってしまわないよう待ち＋明転は短めにする。
        private IEnumerator StartFadeInRoutine()
        {
            if (startHoldBlack > 0f) yield return new WaitForSecondsRealtime(startHoldBlack);
            yield return screenFade.FadeFromBlack(startFadeInDuration);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (startMode != StartMode.EnterTrigger) return;
            if (!other.CompareTag("Player")) return;
            Begin();
        }

        // 入力(移動/攻撃)だけを止める。Mover は触らない。
        // PlayerLifetimeScope.SetUIMode は Mover.SetUIMode も呼び、あちらは
        // ApplyGravity を丸ごと return させるため、落下中に使うと空中で止まってしまう。
        private void SetInputFrozen(bool frozen)
        {
            if (player == null) return;
            if (player.Input != null) player.Input.SetUIMode(frozen);
            if (player.Attack != null) player.Attack.SetUIMode(frozen);
        }

        private void Begin()
        {
            if (triggered) return;
            if (boss == null)
            {
                Debug.LogWarning("[B2_BossRoomTrigger] Boss が未設定のため、カットシーンを開始できません。", this);
                return;
            }
            triggered = true;
            StartCoroutine(BeginRoutine());
        }

        private IEnumerator BeginRoutine()
        {
            // 前置きショット中に動かれないよう、ここで先に凍結しておく。
            // 解除は B2_QueenIntroCamera(freezePlayer=ON)の OnEnd が行うので、
            // あちらを OFF にする場合は解除役がいなくなる点に注意。
            if (freezePlayer)
            {
                if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();
                if (player != null) player.SetUIMode(true);
            }

            // HUD を隠す。復帰は演出完了時に B2_Boss.OnIntroComplete が RevealNow() で行う。
            // HideForIntro ではなく HideUntilRevealed を使うこと(前者はバナー監視で即復帰してしまう)
            if (hideHudDuringCutscene) HUDIntroReveal.HideUntilRevealed();

            // 着地の余韻: すぐ暗転せず少し待つ。この間も落下カメラは追従したまま
            if (delayBeforeFade > 0f) yield return new WaitForSeconds(delayBeforeFade);

            // 落下追従を止める。カメラはこのまま次のショットが引き継ぐ
            // (暗転中に旋回開始位置へ飛ぶので繋ぎ目は見えない)
            if (fallCamera != null) fallCamera.End();

            if (screenFade != null)
            {
                yield return screenFade.FadeToBlack(fadeOutDuration);
                if (holdBlack > 0f) yield return new WaitForSecondsRealtime(holdBlack);
            }

            // 暗転中に再生を始める。カメラが演出開始位置へ飛ぶのが見えない。
            PlayShot(0);

            if (screenFade != null)
            {
                yield return null;   // 1フレーム待ってカメラが開始位置に着いてから明転する
                yield return screenFade.FadeFromBlack(fadeInDuration);
            }
        }

        // shots を先頭から1本ずつ再生し、終わったらボス側のショットへ繋ぐ。
        // CinemaTrack.Play(onComplete) の完了コールバックで数珠つなぎにする。
        private void PlayShot(int index)
        {
            // 空の枠は飛ばす(Inspector で消し忘れても止まらないように)
            while (index < shots.Count && shots[index] == null) index++;

            if (index >= shots.Count)
            {
                boss.StartIntro();
                return;
            }

            shots[index].Play(() => PlayShot(index + 1));
        }
    }
}
