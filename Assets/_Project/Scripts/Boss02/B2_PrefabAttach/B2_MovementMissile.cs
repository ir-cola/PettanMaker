/*=============================================================================

 File   : [B2_MovementMissile.cs]
 Desc   : 針ミサイルの挙動を制御するクラス:
            - 引き抜き動作
            - 待機中の浮遊動作
            - プレイヤーに向かって飛ぶ動作
            - ターゲットサークルの生成

 ------------------------------------------------------------------------------

 Date   : 2026/05/25
 Author : Yokoyama Haruki
 
===============================================================================*/
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ParticleSystem;

namespace Boss02
{
    public class B2_MovementMissile : MonoBehaviour
    {
        // ----- 設定 -----
        [Header("エフェクト設定")]
        [SerializeField] GameObject preShotEffectPrefab;    // 発射前のエフェクトのプレハブ
        [SerializeField] float preShotOffset = -2.3f; // 発射前のエフェクトのオフセット位置
        [SerializeField] GameObject trailEffectPrefab;      // トレイルエフェクトのプレハブ
        [SerializeField] float trailOffset = 2.5f; // トレイルエフェクトのオフセット位置
        [SerializeField] GameObject hitEffectPrefab;        // ヒットエフェクトのプレハブ


        [Header("その他設定")]
        [SerializeField] float destroyDelay = 2f; // 自動で消えるまでの時間
        [SerializeField] GameObject targetCirclePrefab; // ターゲットサークルのプレハブ
        [SerializeField] float targetCircleRadius = 2f; // ターゲットサークルの半径

        
        // ----- 内部デリゲート -----
        delegate void MoveDelegate();
        MoveDelegate moveDelegate;

        // ----- 参照変数 -----
        Transform playerTransform;
        B2_Boss boss;
        B2_Settings.NeedleMissileSettings Settings => boss.Settings.MissileSettings;

        // ----- 内部変数 -----

        float elapsedTime = 0f;
        Vector3 startPos;
        Quaternion startRot;
        Vector3 targetDir;
        GameObject targetCircleInstance;

        // エフェクト関連
        GameObject trailInstance;
        bool playPreShotEffect = false;

        // エフェクト用のプール
        GameObjectPool preShotPool;
        GameObjectPool hitEffectPool;

        // 遅延発射のためのインスタンス識別子とカウンター
        static public int activeCount { get; private set; } = 0;
        int instanceID;

        void Awake()
        {

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) playerTransform = playerGo.transform;
            boss = GetComponentInParent<B2_Boss>();

            // エフェクトのオブジェクトを生成して非アクティブにしておく
            trailInstance = Instantiate(
                trailEffectPrefab,
                transform.position + transform.up * trailOffset,
                Quaternion.identity, transform);
            trailInstance.SetActive(false);

            // プールを取得
            preShotPool = PoolRegistry.Get(preShotEffectPrefab);
            hitEffectPool = PoolRegistry.Get(hitEffectPrefab);

            enabled = false; // 最初は無効にしておく（boss 取得失敗でも必ず無効化する）
            if (boss == null)
            {
                Debug.LogError("B2_Boss component not found in parent hierarchy.");
            }
        }
        void OnEnable()
        {
            elapsedTime = 0f;
            moveDelegate = PullMove;
            startPos = transform.position;
            startRot = transform.rotation;
            instanceID = activeCount++;
            trailInstance.SetActive(true); // エフェクトを有効化
            playPreShotEffect = false;
        }

        void Update()
        {
            if (boss == null)
            {
                enabled = false; // boss 不在なら動かしようがない。NRE を避けて自身を止める
                return;
            }
            if(boss.isHitStop)
            {
                return; // ヒットストップ中は動かない
            }
            elapsedTime += Time.deltaTime;
            moveDelegate?.Invoke();
        }

        // 引き抜き動作
        void PullMove()
        {
            // 経過時間とカーブを使って位置を更新
            float t = elapsedTime / Settings.pullMoveDuration;
            float curveValue = Settings.pullMoveCurve.Evaluate(t);
            // 親オブジェクトの上方向に沿って引き抜く
            Vector3 targetPos = startPos + transform.parent.up * Settings.pullDistance;
            transform.position = Vector3.Lerp(startPos, targetPos, curveValue);

            Vector3 dir = (playerTransform.position - transform.position).normalized;
            Quaternion targetRot =
                Quaternion.LookRotation(dir) *
                Quaternion.Euler(-90f, 0f, 0f);

            transform.rotation = Quaternion.Lerp(startRot, targetRot, curveValue);

            // 引き抜きが完了したら次の挙動に切り替える
            if (elapsedTime >= Settings.pullMoveDuration)
            {
                ChangeBehaviour(WaitMove);
            }
        }

        // 待機中の浮遊動作
        void WaitMove()
        {
            Vector3 waitPos = startPos + transform.parent.up * Settings.pullDistance;
            transform.position = 
                waitPos + Vector3.up * Mathf.Sin(elapsedTime * Settings.floatFrequency) * Settings.floatAmplitude; // 上下に揺れる動き
            LookToDirection((playerTransform.position - transform.position).normalized);

            // 待機時間とインスタンスIDに基づいて発射タイミングを決定
            if (elapsedTime >= Settings.chargeTime + Settings.shotDelay * instanceID)
            {
                Vector3 pos = transform.position;
                Vector3 randomPos = pos + UnityEngine.Random.insideUnitSphere * Settings.nearDistance;
                randomPos.y = pos.y; // 水平面内でランダムな位置を生成
                targetDir = (playerTransform.position - randomPos).normalized;
                LookToDirection(targetDir);
                CreateTargetCircle(targetDir);
                ChangeBehaviour(ShotMove);
            }

            if(elapsedTime >= Settings.chargeTime + 
                Settings.shotDelay * instanceID - 1f && 
                !playPreShotEffect)
            {
                playPreShotEffect = true;
                Vector3 offset = transform.up * preShotOffset;
                // 発射前のエフェクトを再生
                GameObject preShotEffect =
                    preShotPool.Rent(
                        transform.position + offset,
                        Quaternion.identity);
                // エフェクトの再生が終わり次第エフェクトを戻す
                StartCoroutine(EffectReturnWaitEnd(preShotEffect));
            }

        }

        // プレイヤーに向かって飛ぶ動作
        void ShotMove()
        {
            transform.position += targetDir * Settings.speed * Time.deltaTime;

            if (elapsedTime >= 10f) // 10秒以上飛んでいる場合は安全のため消す
            {
                StartCoroutine(DelayDisable(0f));
            }
        }

        void IdleMove()
        {
            // 何もしない
        }

        void OnDisable()
        {
            activeCount = Mathf.Max(0, activeCount - 1); // インスタンス数を減らす
        }

        /// <summary>
        /// 挙動を切り替える関数
        /// </summary>
        /// <param name="newBehaviour">新しい挙動</param>
        void ChangeBehaviour(Action newBehaviour)
        {
            moveDelegate = () => newBehaviour();
            elapsedTime = 0f;
            elapsedTime += Time.deltaTime;
            moveDelegate?.Invoke();
        }

        /// <summary>
        /// ターゲットサークルを生成する関数
        /// </summary>
        /// <param name="dir">進行方向</param>
        void CreateTargetCircle(Vector3 dir)
        {
            // レイキャストで生成位置を決定
            RaycastHit hit;
            if (Physics.Raycast(transform.position, dir, out hit, 100f, LayerMask.GetMask("Ground")))
            {
                Debug.Log($"Raycast hit at {hit.point} for instance {instanceID}");
                // ターゲットサークルを生成
                Vector3 spawnPos = hit.point + Vector3.up * 0.001f; // 少し浮かせる
                targetCircleInstance = Instantiate(targetCirclePrefab, spawnPos, Quaternion.Euler(90f, 0f, 0f)); // 地面に平行になるように回転
                targetCircleInstance.transform.localScale = Vector3.one * targetCircleRadius * 2f; // 半径に基づいてスケールを設定
            }

        }

        /// <summary>
        /// 指定した方向を向く関数
        /// </summary>
        /// <param name="dir">向かせたい方向</param>
        void LookToDirection(Vector3 dir)
        {
            Quaternion targetRot =
                Quaternion.LookRotation(dir) *
                Quaternion.Euler(-90f, 0f, 0f);
            transform.rotation = targetRot;
        }

        void OnDrawGizmos()
        {
            if (targetDir != Vector3.zero)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, transform.position + targetDir * 100f);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Player"))
            {
                // プレイヤーにダメージを与える処理をここに実装
                PlayerHealth playerHealth = collision.gameObject.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    CombatSystem.Instance.Damage(playerHealth, 1);
                    StartCoroutine(DelayDisable(0f)); // ダメージを与えた後すぐに消す
                    if (targetCircleInstance != null)
                    {
                        Destroy(targetCircleInstance);
                    }
                }
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Ground"))
            {
                // 地面に当たったら無効化
                ChangeBehaviour(IdleMove);
                // ここでエフェクトを再生するなどの処理を行う
                Vector3 p1 = GetComponent<SphereCollider>().ClosestPoint(other.transform.position);
                Vector3 p2 = other.ClosestPoint(transform.position);
                Vector3 hitPoint = (p1 + p2) / 2f; // 衝突点の中間を計算
                GameObject hitEffect = hitEffectPool.Rent(hitPoint, Quaternion.identity);
                StartCoroutine(EffectReturnWaitEnd(hitEffect));
                StartCoroutine(DelayDisable(destroyDelay));
                if (targetCircleInstance != null)
                {
                    Destroy(targetCircleInstance);
                }

            }
        }


        /// <summary>
        /// 指定した遅延時間後にオブジェクトを無効化するコルーチン
        /// </summary>
        /// <param name="delay">遅延時間（秒）</param>
        /// <returns></returns>
        IEnumerator DelayDisable(float delay)
        {
            yield return new WaitForSeconds(delay);
            enabled = false;
            //gameObject.SetActive(false);
            transform.position = startPos; // 元の位置に戻す
            transform.rotation = startRot; // 元の回転に戻す
            trailInstance.SetActive(false); // エフェクトを無効化
        }

        /// <summary>
        /// エフェクトのパーティクルが終了するまで待機し、終了後にプールに返却するコルーチン
        /// </summary>
        /// <param name="effectRoot"></param>
        /// <returns></returns>
        IEnumerator EffectReturnWaitEnd(GameObject effectRoot)
        {
            // エフェクトのパーティクルシステムを取得
            ParticleSystem[] particleSystems = effectRoot.GetComponentsInChildren<ParticleSystem>();

            // パーティクルがすべて終了するまで待機
            yield return new WaitUntil(() =>
            {
                foreach (var ps in particleSystems)
                {
                    if (ps.IsAlive())
                    {
                        return false;
                    }
                }
                return true;
            });

            // エフェクトをプールに返却
            effectRoot.GetComponent<Poolable>().Release();

        }
        private void OnDrawGizmosSelected()
        {
            if (trailEffectPrefab != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position + transform.up * trailOffset, 0.1f);
            }

            if (preShotEffectPrefab != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(transform.position + transform.up * preShotOffset , 0.1f);
            }
        }
    }
}