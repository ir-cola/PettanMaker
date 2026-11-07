/*=============================================================================

 File   : [B2_ChandelierGimmick.cs]
 Desc   : シャンデリアギミック全体の進行管理(繰り返しループ)。
          戦闘開始からクールタイムが経過するとステージ内のスイッチが出現する。
          出現したスイッチを全て押すと、ステージ中央上部のシャンデリアが落下して
          ボスに直撃する。使用後はシャンデリアを天井へ吊り直し、クールタイムを
          再スタートして、また出現 → 使用 …を繰り返す。

          ループ:
            戦闘開始
              → クールタイム(appearCooldown 秒)経過
              → スイッチ(既定3か所)が出現
              → プレイヤーが攻撃で全て作動
              → B2_Chandelier.Drop() → 落下 → 着弾
                 → (シャンデリアの onImpact 配線で) B2_Boss.HitByChandelier()
                    土台が無事なら「土台破壊 + 大幅スタン」
                    破壊済みなら「ダメージ + 大幅スタンの半分」
              → スイッチを片付け、シャンデリアをフェードで天井へ吊り直す
              → クールタイム再スタート(先頭へ戻る)

          スイッチの onActivated とシャンデリアの onImpact はこのクラスが
          実行時に自動購読するので、Inspector で個別に配線する必要はない。
          (シャンデリアの onImpact → B2_Boss.HitByChandelier() だけは
           Inspector で配線する)

          セットアップ(Inspector):
            - Boss          : ボスの B2_Boss (未設定ならシーンから自動取得)
            - Switches      : ステージ内のスイッチ(3か所)
            - Chandelier    : 中央上部のシャンデリア
            - Appear Cooldown : 出現までの待ち時間(秒)

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    /// <summary>クールタイムでスイッチを出現させ、全作動でシャンデリアを落とすループ管理。</summary>
    public class B2_ChandelierGimmick : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("クールタイムの起点(戦闘開始)を見るボス。未設定ならシーンから自動取得")]
        [SerializeField] private B2_Boss boss;
        [Tooltip("ステージ内のスイッチ(3か所)。全て作動でシャンデリアが落ちる")]
        [SerializeField] private List<B2_Switch> switches = new();
        [Tooltip("ステージ中央上部のシャンデリア")]
        [SerializeField] private B2_Chandelier chandelier;

        [Header("クールタイム")]
        [Tooltip("スイッチが出現するまでの待ち時間(秒)。戦闘開始から数え始め、\n" +
                 "1回使い切ってシャンデリアが天井へ戻ったら、またここから数え直す")]
        [SerializeField] private float appearCooldown = 30f;
        [Tooltip("ONなら出現するまでスイッチを非アクティブにして隠す")]
        [SerializeField] private bool hideSwitchesUntilAppear = true;

        [Header("出現演出")]
        [SerializeField] private AudioSource seSource;
        [Tooltip("スイッチ出現時のSE")]
        [SerializeField] private AudioClip appearSe;
        [Tooltip("各スイッチの位置に出す出現エフェクト")]
        [SerializeField] private GameObject appearVfxPrefab;

        [Header("イベント")]
        [Tooltip("スイッチが出現した瞬間。UI通知やカメラ演出などを配線する")]
        public UnityEvent onSwitchesAppeared;
        [Tooltip("スイッチが1つ作動するたびに呼ぶ(作動数, 総数)")]
        public SwitchProgressEvent onSwitchProgress;
        [Tooltip("全スイッチが作動した瞬間。シャンデリア落下と同時に鳴らしたい演出を配線する")]
        public UnityEvent onAllSwitchesActivated;

        // 汎用 UnityEvent<T> は Inspector に出ないため、具象サブクラスにする(CinemaTrack と同じ作法)
        [System.Serializable]
        public class SwitchProgressEvent : UnityEvent<int, int> { }

        private bool appeared;      // スイッチが出ている(＝プレイヤーが押せる)
        private bool resetting;     // 使用後の片付け中。この間はクールタイムを数えない
        private float cooldownTimer;

        /// <summary>作動済みスイッチの数</summary>
        public int ActivatedCount { get; private set; }
        /// <summary>スイッチの総数</summary>
        public int SwitchCount => switches.Count;
        /// <summary>スイッチが出現中か</summary>
        public bool HasAppeared => appeared;
        /// <summary>次の出現までの残り秒数(出現中/片付け中は0)</summary>
        public float CooldownRemaining =>
            appeared || resetting ? 0f : Mathf.Max(0f, appearCooldown - cooldownTimer);

        private void Awake()
        {
            if (boss == null) boss = FindObjectOfType<B2_Boss>();

            foreach (B2_Switch sw in switches)
            {
                if (sw == null) continue;
                // 実行時に購読するので Inspector 配線は不要
                sw.onActivated.AddListener(OnSwitchActivated);
                if (hideSwitchesUntilAppear) sw.gameObject.SetActive(false);
            }

            if (chandelier != null) chandelier.onImpact.AddListener(OnChandelierImpact);
        }

        private void OnDestroy()
        {
            foreach (B2_Switch sw in switches)
            {
                if (sw != null) sw.onActivated.RemoveListener(OnSwitchActivated);
            }
            if (chandelier != null) chandelier.onImpact.RemoveListener(OnChandelierImpact);
        }

        private void Update()
        {
            if (appeared || resetting) return;
            // 出現演出(ムービー)中は数えない。戦闘開始が起点。
            if (boss != null && !boss.BattleStarted) return;

            cooldownTimer += Time.deltaTime;
            if (cooldownTimer < appearCooldown) return;

            AppearSwitches();
        }

        /// <summary>スイッチを出現させる。クールタイムを待たずに出したいときは外から呼んでもよい。</summary>
        [ContextMenu("スイッチを出現させる")]
        public void AppearSwitches()
        {
            if (appeared) return;
            appeared = true;
            ActivatedCount = 0;

            foreach (B2_Switch sw in switches)
            {
                if (sw == null) continue;
                sw.gameObject.SetActive(true);
                sw.ResetSwitch(); // 前回押された状態を引きずらない
                if (appearVfxPrefab != null)
                    Instantiate(appearVfxPrefab, sw.transform.position, sw.transform.rotation);
            }

            if (seSource != null && appearSe != null) seSource.PlayOneShot(appearSe);
            onSwitchesAppeared?.Invoke();
        }

        // スイッチ1つが作動するたびに呼ばれる(Awake で購読)
        private void OnSwitchActivated()
        {
            if (!appeared) return;

            // 押された数はリアルタイムに数え直す(参加スイッチが増減しても破綻しない)
            ActivatedCount = 0;
            foreach (B2_Switch sw in switches)
            {
                if (sw != null && sw.IsActivated) ActivatedCount++;
            }

            onSwitchProgress?.Invoke(ActivatedCount, switches.Count);

            if (ActivatedCount < switches.Count) return;

            onAllSwitchesActivated?.Invoke();
            if (chandelier != null) chandelier.Drop();
        }

        // シャンデリア着弾(Awake で購読)。片付けてクールタイムを再スタートする。
        private void OnChandelierImpact()
        {
            if (resetting) return;
            StartCoroutine(ResetRoutine());
        }

        private IEnumerator ResetRoutine()
        {
            resetting = true;
            appeared = false;

            // 使い終わったスイッチを片付ける
            foreach (B2_Switch sw in switches)
            {
                if (sw == null) continue;
                sw.ResetSwitch();
                if (hideSwitchesUntilAppear) sw.gameObject.SetActive(false);
            }
            ActivatedCount = 0;

            // 落ちた本体をフェードで消して天井へ吊り直す(終わるまで待つ)
            if (chandelier != null) yield return chandelier.ResetToCeilingRoutine();

            // シャンデリアが天井に戻ってからクールタイム再スタート
            cooldownTimer = 0f;
            resetting = false;
        }
    }
}
