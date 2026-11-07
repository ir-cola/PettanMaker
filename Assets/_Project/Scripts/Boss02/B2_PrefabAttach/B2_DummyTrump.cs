/*=============================================================================

 File   : [B2_DummyTrump.cs]
 Desc   : ダミートランプの挙動を制御するクラス:
           - 一定時間アクティブ状態を維持
           - プール管理による効率的なオブジェクト再利用
 ------------------------------------------------------------------------------

 Date   : 2026/05/25
 Author : Yokoyama Haruki
 
===============================================================================*/
using UnityEngine;
using System.Collections;

namespace Boss02
{
    public class B2_DummyTrump : MonoBehaviour
    {
        // ----- 設定 -----
        [SerializeField] Vector2 activeDurationRange;

        // ----- 参照変数 -----
        B2_Boss boss;
        B2_TrumpBomb bombAction => boss.ActionController.weightSelector.trumpBomb;
        Rigidbody rb;

        // ----- 内部変数 -----
        float elapsedTime;
        float activeDuration;

        Vector3 saveVelocity;
        Vector3 saveAngulerVelocity;
        Coroutine hitStopCoroutine;

        private void Awake()
        {
            boss = GameObject.Find("Boss_02").GetComponent<B2_Boss>();
            rb = GetComponent<Rigidbody>();
            enabled = false;
        }


        private void OnEnable()
        {
            // アクティブ状態の開始時に時間をリセットし、ランダムな持続時間を設定
            elapsedTime = 0f;
            activeDuration = Random.Range(activeDurationRange.x, activeDurationRange.y);
            saveVelocity = Vector3.zero;
            saveAngulerVelocity = Vector3.zero;
        }

        private void Update()
        {

            if (boss.isHitStop)
            {
                hitStopCoroutine ??= StartCoroutine(HitStop());
                return;
            }

            // 一定時間アクティブ状態を維持し、その後プールに返却
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= activeDuration)
            {
                bombAction.ReturnToPool(gameObject, false);
                rb.angularVelocity = Vector3.zero;
                rb.linearVelocity = Vector3.zero;
                enabled = false;
            }
        }

        void OnDisable()
        {
            if (hitStopCoroutine != null)
            {
                StopCoroutine(hitStopCoroutine);
                hitStopCoroutine = null;
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

    }
}