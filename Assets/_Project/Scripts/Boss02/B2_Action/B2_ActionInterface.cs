/*=============================================================================

 File   : [B2_ActionInterface.cs]
 Desc   : ボスのアクションクラスのインターフェースと基本的なIdleActionの実装
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;
using static Boss02.B2_Settings;

namespace Boss02
{
    public abstract class IAction
    {
        protected B2_Boss boss;
        private float recoveryTimer = 0f;

        public IAction(B2_Boss boss)
        {
            this.boss = boss;
        }

        abstract public void Enter();
        abstract public void Update();
        abstract public void Exit();

        protected void WaitRecoveryAfterFinish(float recoveryTime = 1f)
        {
            if (WaitForRecovery(recoveryTime))
            {
                IsFinished = true;
            }
        }

        private bool WaitForRecovery(float recoveryTime = 1f)
        {
            recoveryTimer += Time.deltaTime;
            bool isRecovered = recoveryTimer >= recoveryTime;
            if (isRecovered)
            {
                recoveryTimer = 0f; // タイマーをリセット
                return true;
            }
            return false;

        }

        public bool IsFinished { get; protected set; }
    }

    public class B2_IdleAction : IAction
    {
        float timer = 0f;
        public B2_IdleAction(B2_Boss boss) : base(boss) { }

        public override void Enter() 
        { 
            timer = 0f;
            IsFinished = false;
            Debug.Log("Entering Idle Action"); 
        }

        public override void Update() 
        {
            timer += Time.deltaTime;
            if (timer >= boss.actionTime) // 2秒後にアクション終了
            {
                IsFinished = true;
            }
        }

        public override void Exit() 
        { 
            //Debug.Log("Exiting Idle Action");
        }
    }
}