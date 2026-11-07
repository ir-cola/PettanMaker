/*=============================================================================

 File   : [B2_SummonHedgehog.cs]
 Desc   : ハリネズミ召喚のアクションクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_SummonHedgehog : IAction
    {
        enum State
        {
            MovingToGrabPoint,
            Holding,
            Returning
        }

        private State currentState;

        private B2_Settings.SummonHedgehogSettings settings => boss.Settings.HedgehogSettings;

        public B2_SummonHedgehog(B2_Boss boss) : base(boss) 
        {
        }

        Vector3 startPos;
        Quaternion startRot;
        Vector3 prevPos;
        float elapsedTime = 0f;
        B2_MoveHedgeHog shotHedgehog;
        B2_FlamingoMallet mallet;
        B2_FlamingoHold hold;
        public override void Enter()
        {
            Debug.Log("Entering Summon Hedgehog Action");
            IsFinished = false;
            elapsedTime = 0f;
            startPos = settings.FlamingoRoot.localPosition;
            startRot = settings.FlamingoRoot.localRotation;
            currentState = State.MovingToGrabPoint;
        }

        public override void Update()
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / settings.GrabDuration);

            switch (currentState)
            {
            case State.MovingToGrabPoint:
                if (elapsedTime < settings.GrabDuration)
                {
                    MoveFlamingoRoot(t);
                }
                else
                {
                    currentState = State.Holding;

                    // つかんだ瞬間に手のボーンの子にする。以降は振り下ろしアニメが
                    // 手を動かせばフラミンゴも一緒に動くので、.anim にフラミンゴの
                    // カーブを入れなくても動きが同期する(＝マレットの距離判定が成立する)
                    AttachFlamingoToHand();

                    // 振り始め: フラミンゴのトレイルを出す
                    SetFlamingoTrail(true);

                    boss.Animator.SetTrigger(B2_Animator.ToSummonHedgehog);
                        elapsedTime = 0f; // ホールド開始時に時間をリセット
                    shotHedgehog = GameObject.Instantiate(
                        settings.hedgehogPrefab,
                        settings.hedgehogSpawnPoint.position,
                        Quaternion.identity).GetComponent<B2_MoveHedgeHog>();

                    // マレットに打ち出し対象を登録(接触した瞬間に打ち出される)
                    mallet = settings.FlamingoRoot.GetComponentInChildren<B2_FlamingoMallet>();
                    if (mallet != null) mallet.SetTarget(shotHedgehog);
                }
                break;

            case State.Holding:
                // マレット接触で打ち出し完了 → リターンへ。
                // 接触が取れないシーン構成でも進行するよう maxHoldTime で保険打ち出しする。
                bool malletLaunched = mallet != null && mallet.HasLaunched;
                bool timedOut = elapsedTime >= settings.maxHoldTime;
                if (malletLaunched || timedOut)
                {
                    // マレット未接触のままタイムアウトした場合の保険打ち出し(斜め前)
                    if (!malletLaunched && shotHedgehog != null && shotHedgehog.IsLaunchable)
                    {
                        shotHedgehog.Launch(new Vector3(-1.0f, 0.2f, 1.0f).normalized);
                    }

                    // 振り終わり: 帰投中は軌跡を引かないようトレイルを止める
                    SetFlamingoTrail(false);

                    // 追従を切ってから帰投する(MoveFlamingoRoot が localPosition を駆動するため)
                    ReleaseFlamingo();

                    currentState = State.Returning;
                    elapsedTime = 0f; // リターン開始時に時間をリセット
                }
                break;

            case State.Returning:
                t = 1.0f - t; // リターンは逆方向の時間で評価
                MoveFlamingoRoot(t);

                if (elapsedTime >= settings.GrabDuration) // リターンが完了したら終了
                {
                    WaitRecoveryAfterFinish(settings.recoveryTime);
                }
                break;
            }
        }

        public override void Exit()
        {
            // スタン等で中断されてもトレイルが出っぱなしにならないよう必ず止める
            SetFlamingoTrail(false);

            // スタン等で途中中断されても、手に追従したままにならないよう必ず解除する
            ReleaseFlamingo();
            settings.FlamingoRoot.localPosition = startPos;
            settings.FlamingoRoot.localRotation = startRot;
        }

        // フラミンゴを手に持たせる。階層は変えず、B2_FlamingoHold が LateUpdate で
        // 手のボーンに追従させる(親子付けにすると Animator の Flamingo レイヤーが
        // 使う "Flamingo_Root/flamingo/..." のパスが壊れるため)。
        void AttachFlamingoToHand()
        {
            if (settings.HandBone == null) return;
            if (hold == null) hold = settings.FlamingoRoot.GetComponent<B2_FlamingoHold>();
            if (hold == null)
            {
                Debug.LogWarning("[B2_SummonHedgehog] FlamingoRoot に B2_FlamingoHold が無いため、" +
                                 "フラミンゴが振りに追従しません(マレットの当たり判定も成立しません)。");
                return;
            }
            hold.Grab(settings.HandBone);
        }

        // 振り中のトレイルを出し入れする。
        // Unity のオブジェクトは ?. だと破棄済みを検知できないため明示的に比較する
        void SetFlamingoTrail(bool on)
        {
            if (settings.flamingoTrail != null) settings.flamingoTrail.SetTrail(on);
        }

        // 追従を解除して、以降はコードで帰投させられるようにする
        void ReleaseFlamingo()
        {
            if (hold != null) hold.Release();
        }

        // GrabPoint を FlamingoRoot の親のローカル空間へ変換して返す。
        // GrabPoint(GripPoint_Hand) の親は MainModel、FlamingoRoot の親は HeartQueen_Model と
        // 別階層なので、localPosition 同士を直接補間すると親の位置/回転の差だけズレる。
        Vector3 GrabPointInFlamingoSpace()
        {
            Transform parent = settings.FlamingoRoot.parent;
            return parent != null
                ? parent.InverseTransformPoint(settings.GrabPoint.position)
                : settings.GrabPoint.position;
        }

        void MoveFlamingoRoot(float t)
        {
            float height = settings.GrabHeightCurve.Evaluate(t);
            float xz = settings.GrabXZCurve.Evaluate(t);
            Vector3 grabPos = GrabPointInFlamingoSpace();
            Vector3 newPos = new Vector3(
                Mathf.LerpUnclamped(startPos.x, grabPos.x, xz),
                Mathf.LerpUnclamped(startPos.y, grabPos.y, height),
                Mathf.LerpUnclamped(startPos.z, grabPos.z, xz)
            );

            prevPos = settings.FlamingoRoot.localPosition;
            settings.FlamingoRoot.localPosition = newPos;
            Vector3 delta = (settings.FlamingoRoot.localPosition - prevPos);
            Vector2 delta2D = new Vector2(delta.x, delta.y);
            if (delta2D.sqrMagnitude > settings.AngleThresholdSpeed * settings.AngleThresholdSpeed * Time.deltaTime * Time.deltaTime)
            { 
                Vector2 normDelta = delta2D.normalized;
                float angle = Mathf.Atan2(Mathf.Abs(normDelta.x), Mathf.Abs(normDelta.y)) * Mathf.Rad2Deg;
                settings.FlamingoRoot.localRotation
                    = Quaternion.Slerp(settings.FlamingoRoot.localRotation, Quaternion.Euler(0f, 0f, angle), Time.deltaTime * 5f);
            }
            Debug.DrawLine(prevPos, settings.FlamingoRoot.localPosition, Color.green, settings.GrabDuration, false);
        }
    }
}