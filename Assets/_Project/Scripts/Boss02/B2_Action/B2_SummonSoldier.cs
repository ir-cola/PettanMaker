/*=============================================================================

 File   : [B2_SummonSoldier.cs]
 Desc   : トランプ兵召喚のアクションクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_SummonSoldier : IAction
    {
        public B2_SummonSoldier(B2_Boss boss) : base(boss) { }
        
        B2_Settings.SummonSoldierSettings settings => boss.Settings.SoldierSettings;
        enum State
        {
            Delay,
            Summoning,
            Recovery
        }

        List<GameObject> summonedSoldiers = new List<GameObject>();
        List<GameObject> activeSoldiers = new List<GameObject>();
        float delayTimer = 0f;
        float summonTimer = 0f;
        float summoningTimer = 0f;  // Summoning 状態の経過時間(保険のタイムアウト用)
        int summonedCount = 0;

        // 生存中の召喚兵が上限に達しているか。
        // activeSoldiers は「今生きている兵」を跨いで管理する(Enter でクリアしない)ため、
        // ここでは倒された分だけ間引いてから判定する。
        bool IsAtSoldierCapacity()
        {
            activeSoldiers.RemoveAll(soldier => soldier == null);
            return activeSoldiers.Count >= settings.maxSoldierCount;
        }

        // 召喚したトランプ兵の数をカウントするためのメソッド
        // 召喚された側が召喚終了時に呼び出す
        public void AddSummonedCount()
        {
            summonedCount++;
        }

        State state = State.Delay;

        public override void Enter() 
        {
            Debug.Log("Entering Summon Soldier Action");
            IsFinished = false;
            delayTimer = 0f;
            summonTimer = 0f;
            summoningTimer = 0f;
            state = State.Delay;
            boss.Animator.SetTrigger(B2_Animator.ToSummonSoldier);
            summonedSoldiers.Clear();
            summonedCount = 0;

            // 召喚の起点が女王だと分かるよう、杖に詠唱エフェクトを出す
            SpawnCasterVfx();
        }

        // 杖先の子として生成する(杖に追従させるため)。追従先が無いなら出さない。
        // Unity のオブジェクトは ?. だと破棄済みを検知できないため明示的に比較する
        void SpawnCasterVfx()
        {
            if (settings.casterVfxPrefab == null) return;
            if (settings.casterVfxPoint == null) return;

            GameObject vfx = GameObject.Instantiate(settings.casterVfxPrefab, settings.casterVfxPoint, false);
            if (settings.casterVfxLifetime > 0f) GameObject.Destroy(vfx, settings.casterVfxLifetime);
        }

        public override void Update()
        {
            switch(state)
            {
                case State.Delay:
                    delayTimer += Time.deltaTime;
                    if(delayTimer > settings.startDelay)
                    {
                        state = State.Summoning;
                    }
                    break;

                case State.Summoning:
                    SummonSoldier();
                    summoningTimer += Time.deltaTime;

                    // 「出せた分が全員せり上がり終わった」かつ「これ以上出せない」なら完了。
                    // 生存数が maxSoldierCount に張り付くと SummonSoldier() が生成をスキップし続け、
                    // summonedCount が soldierCount に永久に届かないため、
                    // soldierCount との比較だけだと Summoning から抜けられなくなる。
                    bool spawnedEnough = summonedSoldiers.Count >= settings.soldierCount;
                    bool allRisen = summonedCount >= summonedSoldiers.Count;
                    bool cannotSpawnMore = spawnedEnough || IsAtSoldierCapacity();

                    if ((cannotSpawnMore && allRisen) || summoningTimer >= settings.maxSummonTime)
                    {
                        Debug.Log("All soldiers summoned. Transitioning to Recovery state.");
                        state = State.Recovery;
                        boss.Animator.SetTrigger(B2_Animator.NextAnimation); // 次のアニメーションに移行
                    }

                    break;

                case State.Recovery:
                    WaitRecoveryAfterFinish(settings.recoveryTime);
                    break;
            }
        }

        void SummonSoldier()
        {
            summonTimer += Time.deltaTime;
            activeSoldiers.RemoveAll(soldier => soldier == null); 

            if (summonTimer > settings.summonDuration &&
                summonedSoldiers.Count < settings.soldierCount) 
            {
                // 召喚タイマーをリセットして次の召喚に備える
                summonTimer -= settings.summonDuration;

                // 倒された兵士をリストから削除し、アクティブリストを更新
                activeSoldiers.RemoveAll(soldier => soldier == null);
                if (activeSoldiers.Count >= settings.maxSoldierCount)
                {
                    // 最大召喚数に達している場合は召喚をスキップ
                    return;
                }

                // 召喚位置の確定
                float distance = UnityEngine.Random.Range(settings.minSummonDistance, settings.maxSummonDistance);
                float angle = UnityEngine.Random.Range(0f, 360f);
                Vector3 summonPosition = boss.transform.position + new Vector3(
                    distance * Mathf.Cos(angle * Mathf.Deg2Rad),
                    settings.summonHeightOffset,
                    distance * Mathf.Sin(angle * Mathf.Deg2Rad)
                );

                // 召喚兵の生成
                GameObject soldier = GameObject.Instantiate(
                    settings.soldierPrefab, 
                    summonPosition,
                    Quaternion.identity);

                // 召喚兵の初期化
                soldier.GetComponent<B2_SummonedSoldier>()?.Init(boss, this);

                // リストに追加
                summonedSoldiers.Add(soldier);
                activeSoldiers.Add(soldier);

                // エフェクトの生成
                if (settings.SummonEffect != null)
                {
                    GameObject.Instantiate(settings.SummonEffect, summonPosition - new Vector3(0, settings.summonHeightOffset, 0), Quaternion.identity);
                }
            }

        }



        public override void Exit() 
        {
            //Debug.Log("Exiting Summon Soldier Action");
        }
    }
}