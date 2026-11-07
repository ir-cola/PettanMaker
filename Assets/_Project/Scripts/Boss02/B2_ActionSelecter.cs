/*=============================================================================

 File   : [B2_ActionSelecter.cs]
 Desc   : 重みづけ抽選によりボスの行動リストの選ぶクラス
          行動のリストと重みはB2_Settingsより設定
 ------------------------------------------------------------------------------

 Date   : 2026/05/13
 Author : Yokoyama Haruki
 
===============================================================================*/

using UnityEngine;
using System.Collections.Generic;
using System;

namespace Boss02
{

    public class B2_ActionSelecter
    {
        // ----- Boss関連 -----
        private B2_Boss boss;
        private B2_Settings settings;

        // ----- アクションキャッシュ -----
        // newによる生成を減らすため各アクションをキャッシュしておく
        public B2_IdleAction idle { get; private set; }
        public B2_NeedleMissile needleMissile { get; private set; }
        public B2_SummonSoldier summonSoldier { get; private set; }
        public B2_SummonHedgehog summonHedgehog { get; private set; }
        public B2_SwordAttack swordAttack { get; private set; }
        public B2_TrumpBomb trumpBomb { get; private set; }

        // リストキャッシュ
        private List<IAction> cachedActionGroup = new List<IAction>();

        // コンストラクタ
        public B2_ActionSelecter(B2_Boss boss)
        {
            this.boss = boss;
            settings = boss.Settings;

            idle = new B2_IdleAction(boss);
            needleMissile = new B2_NeedleMissile(boss);
            summonSoldier = new B2_SummonSoldier(boss);
            summonHedgehog = new B2_SummonHedgehog(boss);
            swordAttack = new B2_SwordAttack(boss);
            trumpBomb = new B2_TrumpBomb(boss);
        }

        /// <summary>
        /// B2_Settingsを参照し重み付き抽選を行って行動リストを返す
        /// </summary>
        /// <returns> Boss02の行動リスト </returns>
        public List<IAction> SelectActionGroup()
        {
            if ( boss.StateMachine.IsInState<B2_OnBaseState>())
            {
                return CreateActionGroup(WeightSelect(settings.OnBaseActionGroupList));
            }
            else
            {
                return CreateActionGroup(WeightSelect(settings.OffBaseActionGroupList));
            }
        }

        // 重みづけ抽選
        private List<B2_Settings.ActionType> WeightSelect(List<B2_Settings.ActionGroup> groupLists)
        {
            // 重み付けに基づいてアクションを選択するロジック
            float totalWeight = 0f;

            foreach (var group in groupLists) 
            {
                totalWeight += group.actionWeight;
            }

            float randomValue = UnityEngine.Random.Range(0f, totalWeight);
            float cumulativeWeight = 0f;
            foreach (var group in groupLists)
            {
                cumulativeWeight += group.actionWeight;

                if(randomValue <  cumulativeWeight)
                {
                    return group.actions;
                }

            }
            return null; // 何も選択されなかった場合
        }

        // アクションタイプを実際の型に変換して返す
        private List<IAction> CreateActionGroup(List<B2_Settings.ActionType> types)
        {
            cachedActionGroup.Clear();

            if(types == null || types.Count == 0)
            {
                Debug.LogWarning("CreateActionGroup: No action types provided.");
                return cachedActionGroup; // 空のリストを返す
            }

            Debug.Log("Creating action group from types: " + string.Join(", ", types));

            foreach (var type in types)
            {
                // ActionTypeに基づいてIActionのインスタンスを追加する
                switch (type)
                {
                case B2_Settings.ActionType.Idle:
                    cachedActionGroup.Add(idle);
                    break;
                case B2_Settings.ActionType.NeedleMissile:
                    cachedActionGroup.Add(needleMissile);
                    break;
                case B2_Settings.ActionType.SummonSoldier:
                    cachedActionGroup.Add(summonSoldier);
                    break;
                case B2_Settings.ActionType.SummonHedgehog:
                    cachedActionGroup.Add(summonHedgehog);
                    break;
                case B2_Settings.ActionType.SwordAttack:
                    cachedActionGroup.Add(swordAttack);
                    break;
                case B2_Settings.ActionType.TrumpBombs:
                    cachedActionGroup.Add(trumpBomb);
                    break;
                default:
                    return cachedActionGroup;
                }
            }

            return cachedActionGroup;
        }
    }
}
