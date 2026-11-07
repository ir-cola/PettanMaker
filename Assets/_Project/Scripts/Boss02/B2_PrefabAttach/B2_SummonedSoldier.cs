/*=============================================================================

 File   : [B2_SummonedSoldier.cs]
 Desc   : 召喚されたトランプ兵の初期化処理を行うクラス
          せり上がり中は NavMeshAgent と AI(EnemyBase 派生)を止めておき、
          せり上がりきった Free 時に NavMesh 上へ乗せてから起こす。
        
 ------------------------------------------------------------------------------

 Date   : 2026/07/01
 Author : Yokoyama Haruki
 
===============================================================================*/


using System.Collections.Generic;
using Boss02;
using UnityEngine;
using UnityEngine.AI;

namespace Boss02
{
    public class B2_SummonedSoldier : MonoBehaviour
    {
        [Header("せり上がり中に止めるもの")]
        [Tooltip("未設定なら自身から取得。せり上がり中は無効化し、Free で NavMesh 上へ乗せてから有効化する")]
        [SerializeField] NavMeshAgent agent;
        [Tooltip("Free 時に NavMesh 上の最寄り点を探す半径")]
        [SerializeField] float navSampleRadius = 2f;

        B2_Boss boss;
        B2_SummonSoldier bossAction;

        Vector3 originPos;
        float summonTimer = 0f;
        B2_Settings.SummonSoldierSettings settings => boss.Settings.SoldierSettings;

        // EnemyBase 派生の AI。せり上がり中は止めておく
        readonly List<Behaviour> aiBehaviours = new List<Behaviour>();

        enum State
        {
            Delay,
            Summoning,
            Free,
        }

        State state = State.Delay;

        void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            foreach (MonoBehaviour mb in GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb is EnemyBase) aiBehaviours.Add(mb);
            }

            // せり上がりは transform を直接書き換えるので、Agent や AI が動いていると
            // 位置を奪い合う。しかも初期位置は地面下(summonHeightOffset)で NavMesh に
            // 乗れないため、ここで確実に止めておく。
            // (EnemyBase.Start は無効な間は走らないので、wanderOrigin が地面下のまま
            //  焼き付く問題も同時に避けられる)
            SetAI(false);
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            state = State.Delay;
        }

        public void Init(B2_Boss boss, B2_SummonSoldier bossAction)
        {
            this.boss = boss;
            this.bossAction = bossAction;
            originPos = transform.position;
        }

        // Update is called once per frame
        void Update()
        {
            if (boss == null) return; // Init 前は settings を引けない

            summonTimer += Time.deltaTime;
            switch (state)
            {
                case State.Delay:
                    if (summonTimer > settings.summonDelay)
                    {
                        state = State.Summoning;
                        summonTimer = 0f;
                    }
                    break;
                case State.Summoning:

                    if (summonTimer < settings.summonTime)
                    {
                        // せり上がり中の処理
                        Summon();
                    }
                    else
                    {
                        transform.position = originPos + Vector3.up * settings.summondHeight;
                        ActivateOnNavMesh();
                        state = State.Free;
                        bossAction?.AddSummonedCount();
                    }
                    break;
                case State.Free:
                    // Enemy側のAIに処理を委譲
                    break;
            }
        }

        private void OnDestroy()
        {
            if (state != State.Free)
            {
                bossAction?.AddSummonedCount();
            }
        }


        void Summon()
        {
            float t = summonTimer / settings.summonTime;
            transform.position = originPos + Vector3.up * settings.summondHeight * t;
        }

        // NavMesh 上へ着地させてから Agent と AI を起こす。
        // 順番が重要: NavMesh から離れた位置で Agent を有効化すると
        // "not close enough to the NavMesh" になって動けなくなる。
        void ActivateOnNavMesh()
        {
            if (agent != null)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                {
                    transform.position = hit.position;
                }
                else
                {
                    Debug.LogWarning($"[B2_SummonedSoldier] '{name}' の近くに NavMesh が見つかりません。" +
                                     "NavMesh がベイクされているか、召喚位置が NavMesh の範囲内かを確認してください。", this);
                }
            }

            SetAI(true);
        }

        // NavMeshAgent と EnemyBase 派生の AI をまとめて切り替える。
        // (StageIntroSequence.SetEnemyAI と同じ方針。Health/Death 等は触らない)
        void SetAI(bool on)
        {
            if (agent != null) agent.enabled = on;
            foreach (Behaviour b in aiBehaviours)
            {
                if (b != null) b.enabled = on;
            }
        }
    }
}
