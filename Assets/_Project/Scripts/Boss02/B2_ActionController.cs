/*=============================================================================

 File   : [B2_ActionController.cs]
 Desc   : ボスのアクションを管理するクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/13
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_ActionController
    {
        private IAction currentAction;
        public B2_ActionSelecter weightSelector { get; private set; }

        private Queue<IAction> actionQueue = new Queue<IAction>();
        private Queue<IAction> forceQueue = new Queue<IAction>();

        public B2_ActionController(B2_Boss boss)
        {
            weightSelector = new B2_ActionSelecter(boss);
            currentAction = new B2_IdleAction(boss);
        }

        public void Update()
        {
            // 更新処理
            if (currentAction != null)
            {
                currentAction.Update();

                if (currentAction.IsFinished)
                {
                    EndCurrentAction();
                }
            }

            // 次のアクション選択
            if (currentAction == null)
            {
                SelectNextAction();
            }
        }


        public void PushForceAction(IAction action)
        {
            forceQueue.Enqueue(action);
        }
        /// <summary>
        /// 現在のアクションを強制終了して次のアクションを選択
        ///  例:ボスのやられモーション中(Idle)が終わったあと、アニメーションイベントからの呼び出し等
        /// </summary>
        public void RequestNextAction()
        {
            EndCurrentAction();
        }

        public void ClearActionQueue()
        {
            actionQueue.Clear();
            forceQueue.Clear();
        }

        private void EndCurrentAction()
        {
            currentAction?.Exit();
            currentAction = null;
        }

        private void SelectNextAction()
        {
            // 現在のキューの中身を表示
            Debug.Log("ActionQueue is " + (string.Join(", ", actionQueue)));
            if (forceQueue.Count > 0)
            {
                ChangeAction(forceQueue.Dequeue());
                return;
            }
            else if (actionQueue.Count > 0)
            {
                ChangeAction(actionQueue.Dequeue());
                return;
            }

            Debug.Log("No queued actions, selecting new actions based on weights...");
            GenerateActionQueue(weightSelector.SelectActionGroup());
        }

        private void ChangeAction(IAction newAction)
        {
            Debug.Log("Changing action to: " + newAction.GetType().Name);
            currentAction = newAction;

            if(currentAction == null)
            {
                Debug.LogError("Attempted to change to a null action!");
                return;
            }

            currentAction.Enter();
        }

        private void GenerateActionQueue(List<IAction> actions)
        {
            foreach (var action in actions)
            {
                actionQueue.Enqueue(action);
            }

        }
    }
}
