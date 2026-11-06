/*=============================================================================

 File   : [B2_TrumpBomb.cs]
 Desc   : トランプ爆弾（シャワー）のアクションクラス
        
 ------------------------------------------------------------------------------

 Date   : 2026/05/14
 Author : Yokoyama Haruki
 
===============================================================================*/
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_TrumpBomb : IAction
    {

        B2_Settings.TrumpBombsSettings settings => boss.Settings.TrumpSettings;
        GameObjectPool trumpPool;
        GameObjectPool dummyPool;

        static readonly int MaxTrumpCount = 30;
        static readonly int MaxDummyCount = 200;

        float elapsedTime;
        float trumpInterval;
        float dummyInterval;
        float trumpSpawnTimer;
        float dummySpawnTimer;

        enum State
        {   
            Animation,
            Spawning,
            Recovery
        }

        State state = State.Animation;

        public B2_TrumpBomb(B2_Boss boss) : base(boss) 
        {
            trumpPool = PoolRegistry.Get(settings.TrumpPrefab, MaxTrumpCount);
            dummyPool = PoolRegistry.Get(settings.DummyPrefab, MaxDummyCount);
        }

        public override void Enter() 
        {
            IsFinished = false;
            trumpInterval = settings.spawnDuration / settings.bombCount;
            dummyInterval = settings.spawnDuration / settings.dummyCount;
            elapsedTime = 0f;
            trumpSpawnTimer = 0f;
            dummySpawnTimer = 0f;

            state = State.Animation;
            boss.Animator.SetTrigger(B2_Animator.ToTrumpShower);
        }

        public override void Update()
        {

            switch(state)
            {
                case State.Animation:
                    if(boss.Animator.FinishedAnimationWithTag("Attack"))
                    {
                        state = State.Spawning;
                    }
                    break;
                case State.Spawning:
                    Spawning();
                    break;
                case State.Recovery:
                    WaitRecoveryAfterFinish(settings.recoveryTime);
                    break;
            }

        }

        void Spawning()
        {
            elapsedTime += Time.deltaTime;
            trumpSpawnTimer += Time.deltaTime;
            dummySpawnTimer += Time.deltaTime;
            if (trumpSpawnTimer >= trumpInterval)
            {
                GameObject obj = Spawn(trumpPool);
                obj.GetComponent<B2_Bomb>().enabled = true;
                trumpSpawnTimer -= trumpInterval;
            }
            if (dummySpawnTimer >= dummyInterval)
            {
                // deltaTimeの加算がspawnIntervalを超えている場合、複数回分のトランプを生成する可能性があるため、whileループで対応
                while (dummySpawnTimer >= dummyInterval)
                {
                    GameObject obj = Spawn(dummyPool);
                    obj.GetComponent<B2_DummyTrump>().enabled = true;
                    dummySpawnTimer -= dummyInterval;
                }
            }

            if (elapsedTime >= settings.spawnDuration)
            {
                state = State.Recovery;
                boss.Animator.SetTrigger(B2_Animator.NextAnimation);
            }

        }

        public override void Exit()
        {
            //Debug.Log("Exiting Trump Bomb Action");
        }

        GameObject Spawn(GameObjectPool pool)
        {
            Vector3 spawnPos = boss.transform.position + settings.spawnPosition;
            Quaternion spawnRot = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            GameObject obj = pool.Rent(spawnPos, spawnRot);
            Vector3 randomDir = RandomCone(Vector3.up, settings.spawnAngleRange.x, settings.spawnAngleRange.y);
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            rb.AddForce(randomDir * Random.Range(
                settings.spawnVelocityRange.x, 
                settings.spawnVelocityRange.y), ForceMode.Impulse);

            rb.AddTorque(Random.onUnitSphere * Random.Range(
                settings.spawnAngularVelocityRange.x, 
                settings.spawnAngularVelocityRange.y), ForceMode.Impulse);

            return obj;

        }

        Vector3 RandomCone(Vector3 forward, float minAngle, float maxAngle)
        {
            Vector3 axis =
                Vector3.Cross(forward, Random.onUnitSphere).normalized;

            float angle =
                Random.Range(minAngle, maxAngle);

            Quaternion rot =
                Quaternion.AngleAxis(angle, axis);

            return (rot * forward).normalized;
        }
        public void ReturnToPool(GameObject obj, bool isTrump)
        {
            if (isTrump)
            {
                trumpPool.Return(obj);
            }
            else
            {
                dummyPool.Return(obj);
            }
        }

    }
}