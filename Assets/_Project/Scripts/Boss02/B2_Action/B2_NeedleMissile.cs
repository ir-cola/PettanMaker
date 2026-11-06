/*=============================================================================

 File   : [B2_NeedleMissle.cs]
 Desc   : 待ち針ミサイルのアクションクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Boss02
{
    public class B2_NeedleMissile : IAction
    {
        class MissileGroup
        {
            public bool IsFired;
            public List<GameObject> Missiles;
            public MissileGroup(bool isFiring, List<GameObject> missiles)
            {
                IsFired = isFiring;
                Missiles = missiles;
            }
        }

        List<GameObject> activeMissiles = new List<GameObject>();

        B2_Settings.NeedleMissileSettings settings;

        // ----- 内部変数 -----
        int firedCount = 0;
        float chargeTimer = 0f;
        bool isFireMotion = false;
        bool isFiered = false;
        bool isCharged = false;     // チャージVFXを一度だけ出すためのフラグ
        List<int> randomIndex = new List<int>();

        public B2_NeedleMissile(B2_Boss boss) : base(boss)
        {
            settings = boss.Settings.MissileSettings;

            for(int i = 0; i < settings.missileList.Count; i++)
            {
                randomIndex.Add(i);
            }

            // 発射グループの親オブジェクトを取得
            //missileGroup01_parent = boss.transform.Find("MissileGroup1");
            //Debug.Assert(missileGroup01_parent != null, "MissileGroup1 not found in boss hierarchy");
            //missileGroup02_parent = boss.transform.Find("MissileGroup2");
            //Debug.Assert(missileGroup02_parent != null, "MissileGroup2 not found in boss hierarchy");

            //// グループ内のオブジェクトを元に待ち針ミサイルを生成してリストに格納
            //for (int i = 0; i < missileGroup01_parent.childCount; i++)
            //{
            //    Transform child = missileGroup01_parent.GetChild(i);
            //    if (child == null)
            //    {
            //        Debug.LogError($"MissileGroup1 child at index {i} is null");
            //        continue;
            //    }

            //    missileGroup01.Missiles.Add(
            //        GameObject.Instantiate(
            //            settings.missilePrefab, child.position, child.rotation, child));
            //}

            //for (int i = 0; i < missileGroup02_parent.childCount; i++)
            //{
            //    Transform child = missileGroup02_parent.GetChild(i);
            //    if (child == null)
            //    {
            //        Debug.LogError($"MissileGroup2 child at index {i} is null");
            //        continue;
            //    }

            //    missileGroup02.Missiles.Add(
            //        GameObject.Instantiate(
            //            settings.missilePrefab, child.position, child.rotation, child));
            //}


        }

        public override void Enter()
        {

            Debug.Log("Entering Needle Missile Action");
            IsFinished = false;

            activeMissiles.Clear();
            firedCount = 0;

            // フィッシャー・イェーツでランダムにミサイルを選択するためのインデックスリストを作成
            for(int i= settings.missileList.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (randomIndex[i], randomIndex[j]) = (randomIndex[j], randomIndex[i]);
            }

            // シャッフルしたインデックスでミサイルを有効化
            // activeMissileCount がミサイル数を超えても範囲外アクセスしないよう上限を取る
            int activateCount = Mathf.Min(settings.activeMissileCount, randomIndex.Count);
            for(int i = 0; i < activateCount; i++)
            {
                GameObject missile = settings.missileList[randomIndex[i]];
                if (missile == null)
                {
                    Debug.LogError($"Missile at index {randomIndex[i]} is null");
                    continue;
                }
                var movement = missile.GetComponent<B2_MovementMissile>();
                if (movement != null) movement.enabled = true;
            }


            //MissileGroup targetGroup = null;

            //// 両方未発射
            //if (!missileGroup01.IsFired && !missileGroup02.IsFired)
            //{
            //    targetGroup =
            //        UnityEngine.Random.value < 0.5f
            //        ? missileGroup01
            //        : missileGroup02;
            //}
            //// 発射していない方を発射
            //else if (!missileGroup01.IsFired)
            //{
            //    targetGroup = missileGroup01;
            //}
            //else if (!missileGroup02.IsFired)
            //{
            //    targetGroup = missileGroup02;
            //}

            //基本両方とも残っている状態になっているはず
            //targetGroup =
            //    UnityEngine.Random.value < 0.5f
            //    ? missileGroup01
            //    : missileGroup02;

            //if (targetGroup != null)
            //{
            //    // targetGroup.IsFired = true;

            //    foreach (var missile in targetGroup.Missiles)
            //    {
            //        // 壊されたミサイルは発射しない
            //        if (!missile.activeSelf) continue;
                        
            //        missile.GetComponent<B2_MovementMissile>().enabled = true;
            //        activeMissiles.Add(missile);
            //        firedCount++;
            //    }
            //}
            //else
            //{
            //    WaitRecoveryAfterFinish(settings.recoveryTime);
            //}

            boss.Animator.SetTrigger(B2_Animator.ToNeedleMissile);
            isFireMotion = false;
            isFiered = false;
            isCharged = false;
            chargeTimer = 0f;

        }

        public override void Update()
        {
            chargeTimer += Time.deltaTime;

            // 引き抜きが終わった瞬間 = チャージ(待ち)開始。発射の予兆をここで見せる
            if(!isCharged && chargeTimer >= settings.pullMoveDuration)
            {
                isCharged = true;
                SpawnChargeVfx();
            }

            if(chargeTimer >= settings.pullMoveDuration + settings.chargeTime)
            {
                if(!isFireMotion)
                {
                    isFireMotion = true;
                    boss.Animator.SetTrigger(B2_Animator.NextAnimation);
                }

                if (B2_MovementMissile.activeCount == 0)
                {
                    WaitRecoveryAfterFinish(settings.recoveryTime);
                    if(!isFiered)
                    {   
                        isFiered = true;
                        boss.Animator.SetTrigger(B2_Animator.NextAnimation);
                    }
                }
            }
        }

        public override void Exit()
        {
            //Debug.Log("Exiting Needle Missile Action");
        }

        // チャージ位置の子として生成する(発生源に追従させるため)。追従先が無いなら出さない。
        // Unity のオブジェクトは ?. だと破棄済みを検知できないため明示的に比較する
        void SpawnChargeVfx()
        {
            if (settings.chargeVfxPrefab == null) return;
            if (settings.chargeVfxPoint == null) return;

            GameObject vfx = GameObject.Instantiate(settings.chargeVfxPrefab, settings.chargeVfxPoint, false);
            if (settings.chargeVfxLifetime > 0f) GameObject.Destroy(vfx, settings.chargeVfxLifetime);
        }

    }
}