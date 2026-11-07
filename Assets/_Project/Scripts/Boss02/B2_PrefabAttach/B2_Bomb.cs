/*=============================================================================

 File   : [B2_Bomb.cs]
 Desc   : トランプ爆弾の挙動を制御するクラス:
           - 着弾予測とマーカー表示
           - 爆発処理とプレイヤーへのダメージ判定
           - プール管理による効率的なオブジェクト再利用
 ------------------------------------------------------------------------------

 Date   : 2026/05/25
 Author : Yokoyama Haruki
 
===============================================================================*/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Boss02
{
    public class B2_Bomb : MonoBehaviour
    {
        // ----- 設定 -----
        [SerializeField] float radius = 4f;
        [SerializeField] GameObject markerPrefab;
        [SerializeField] LayerMask groundLayer;
        [SerializeField] GameObject explosionEffect;

        // ----- 参照変数 -----
        B2_Boss boss;
        B2_TrumpBomb bombAction => boss.ActionController.weightSelector.trumpBomb;
        Rigidbody rb;

        GameObjectPool markerPool;
        GameObjectPool effectPool;

        // ----- 内部変数 -----
        float elapsedTime;
        bool hasPredicted;
        bool exploded;                 // 爆発済みガード（多重ダメージ・プール二重返却を防ぐ）
        GameObject markerInstance;
        GameObject model;
        GameObject effectInstance;

        Vector3 saveVelocity;
        Vector3 saveAngulerVelocity;
        Coroutine hitStopCoroutine;

        void Awake()
        {
            // マーカープールの初期化
            markerPool = PoolRegistry.Get(markerPrefab, 10);
            effectPool = PoolRegistry.Get(explosionEffect, 10);

            boss = GameObject.Find("Boss_02").GetComponent<B2_Boss>();
            model = transform.Find("Model").gameObject;
            rb = GetComponent<Rigidbody>();
            enabled = false;
        }


        void OnEnable()
        {
            elapsedTime = 0f;
            hasPredicted = false;
            exploded = false;
            CancelInvoke(nameof(ReturnPool));   // 前回の遅延返却が残っていれば破棄
            model.SetActive(true);
            saveVelocity = Vector3.zero;
            saveAngulerVelocity = Vector3.zero;

        }

        void Update()
        {
            // 着弾予測とマーカー表示
            if (!hasPredicted)
            {
                if (rb.linearVelocity.sqrMagnitude > 0.1f)
                {
                    if (PredictLandingPoint())
                    {
                        hasPredicted = true;
                    }
                }
            }

            if (boss.isHitStop)
            {
                hitStopCoroutine ??= StartCoroutine(HitStop());
                return;
            }


            elapsedTime += Time.deltaTime;

            // 長時間爆発しなかった場合戻す
            if (elapsedTime >= 10f)
            {
                ReturnPool();
            }

        }

        void Explosion()
        {
            // 爆発エフェクトの生成やダメージ処理などをここに実装
            effectInstance = effectPool.Rent(transform.position, Quaternion.identity);
            model.SetActive(false);
        }

        /// <summary>
        /// 爆発範囲内にプレイヤーがいるかを判定し、ダメージを与える処理を実装
        /// </summary>
        void CheckHitPlayer()
        {
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
            foreach (var hitCollider in hitColliders)
            {
                if (hitCollider.CompareTag("Player"))
                {
                    // プレイヤーにダメージを与える処理をここに実装
                    PlayerHealth playerHealth = hitCollider.GetComponent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        CombatSystem.Instance.Damage(playerHealth, 1);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// オブジェクトをプールに返却し、状態をリセットする処理
        /// </summary>
        void ReturnPool()
        {
            CancelInvoke(nameof(ReturnPool));   // 遅延返却と早期返却の二重発火を防ぐ
            bombAction.ReturnToPool(gameObject, true);
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            if(effectInstance != null)
            {
                effectPool.Return(effectInstance);
                effectInstance = null;
            }
            enabled = false;
        }

        /// <summary>
        /// 現在の速度と重力を考慮して、将来の位置を予測し、地面に当たるポイントを探す関数
        /// </summary>
        /// <returns></returns>
        bool PredictLandingPoint()
        {
            Vector3 pos = transform.position;
            Vector3 vel = rb.linearVelocity;

            RaycastHit hit;
            float timeStep = 0.1f;

            // 5秒分、0.1秒ごとに予測
            for (float t = 0; t < 10f; t += timeStep)
            {
                // 放物線移動
                vel += Physics.gravity * timeStep;
                pos += vel * timeStep;

                // デバッグ表示
                Debug.DrawRay(pos, Vector3.up * 0.2f, Color.red, 1f);

                // Groundだけ判定
                if (Physics.Raycast(
                    pos,
                    vel.normalized,
                    out hit,
                    0.5f,
                    groundLayer))
                {
                    markerInstance = markerPool.Rent(hit.point + Vector3.up * 0.01f, Quaternion.Euler(90f, 0f, 0f));

                    return true;
                }
            }

            return false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (exploded) return;   // 2回目以降の接触では再爆発・再ダメージ・二重返却をしない

            if (collision.gameObject.CompareTag("Ground"))
            {
                exploded = true;

                if (markerInstance != null)
                {
                    markerPool.Return(markerInstance);
                    markerInstance = null;
                }

                // 爆発処理
                Explosion();
                CheckHitPlayer();
                // 爆発後2秒でオブジェクトをプールに返す
                Invoke(nameof(ReturnPool), 2f); 
            }
        }

        IEnumerator HitStop()
        {

            saveVelocity = rb.linearVelocity;
            saveAngulerVelocity = rb.angularVelocity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;


            yield return new WaitUntil(() => !boss.isHitStop);

            rb.linearVelocity = saveVelocity;
            rb.angularVelocity = saveAngulerVelocity;

            hitStopCoroutine = null;
        }



        private void OnDisable()
        {
            if (markerInstance != null)
            {
                markerPool.Return(markerInstance);
                markerInstance = null;
            }

            if(hitStopCoroutine != null)
            {
                StopCoroutine(hitStopCoroutine);
                hitStopCoroutine = null;
            }
        }

        //class MarkerPool
        //{
        //    GameObject prefab;
        //    Queue<GameObject> pool = new Queue<GameObject>();
        //    public MarkerPool(GameObject prefab, int initialSize = 10)
        //    {
        //        this.prefab = prefab;
        //        for (int i = 0; i < initialSize; i++)
        //        {
        //            GameObject obj = Object.Instantiate(prefab);
        //            obj.SetActive(false);
        //            pool.Enqueue(obj);
        //        }
        //    }
        //    public GameObject Get(Vector3 position, Quaternion rotation)
        //    {
        //        if (pool.Count > 0)
        //        {
        //            GameObject obj = pool.Dequeue();
        //            obj.transform.position = position;
        //            obj.transform.rotation = rotation;
        //            obj.SetActive(true);
        //            return obj;
        //        }
        //        else
        //        {
        //            return Object.Instantiate(prefab, position, rotation);
        //        }
        //    }
        //    public void Return(GameObject obj)
        //    {
        //        obj.SetActive(false);
        //        pool.Enqueue(obj);
        //    }
        //}
    }
}