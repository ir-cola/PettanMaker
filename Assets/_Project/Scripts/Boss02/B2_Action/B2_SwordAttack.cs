/*=============================================================================

 File   : [B2_SwordAttack.cs]
 Desc   : 剣攻撃/杖攻撃のアクションクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_SwordAttack : IAction
    {
        B2_Settings.SwordAttackSettings settings => boss.Settings.SwordSettings;
        float elapsedTime = 0f;

        enum State
        {
            Charging,
            Attacking,
            Recovery
        }
        State state = State.Charging;

        public B2_SwordAttack(B2_Boss boss) : base(boss) 
        {
        }

        public override void Enter()
        {
            Debug.Log("Entering Sword Attack Action");

            IsFinished = false;

            elapsedTime = 0f;
            state = State.Charging;
            boss.Animator.SetTrigger(B2_Animator.ToSwordAttack);

            // チャージ開始: 杖にエフェクト + SE
            PlaySe(settings.chargeSe);
            SpawnChargeVfx();
        }

        public override void Update()
        {
            elapsedTime += Time.deltaTime;

            switch(state)
            {
                case State.Charging:
                    if(elapsedTime > settings.chargeTime)
                    {
                        state = State.Attacking;
                        boss.Animator.SetTrigger(B2_Animator.NextAnimation);

                        // 振り始め: トレイルを出す + SE
                        PlaySe(settings.swingSe);
                        SetTrail(true);
                    }
                    break;
                case State.Attacking:
                    if(boss.Animator.FinishedAnimation())
                    {
                        state = State.Recovery;
                        SetTrail(false);
                    }
                    break;
                case State.Recovery:
                    WaitRecoveryAfterFinish(settings.recoveryTime);
                    break;
            }
        }
        public override void Exit()
        {
            // スタン等で中断されてもトレイルが出っぱなしにならないよう必ず止める
            SetTrail(false);
        }

        // チャージエフェクトを杖先の子として生成する(杖に追従させるため)
        void SpawnChargeVfx()
        {
            if (settings.chargeVfxPrefab == null) return;

            Transform point = settings.chargeVfxPoint;
            GameObject vfx = point != null
                ? GameObject.Instantiate(settings.chargeVfxPrefab, point, false)
                : GameObject.Instantiate(settings.chargeVfxPrefab, boss.transform.position, Quaternion.identity);

            if (settings.chargeVfxLifetime > 0f) GameObject.Destroy(vfx, settings.chargeVfxLifetime);
        }

        // Unity のオブジェクトは ?. だと破棄済みを検知できないため明示的に比較する
        void SetTrail(bool on)
        {
            if (settings.wandTrail != null) settings.wandTrail.SetTrail(on);
        }

        void PlaySe(AudioClip clip)
        {
            if (settings.seSource != null && clip != null) settings.seSource.PlayOneShot(clip);
        }
    }
}