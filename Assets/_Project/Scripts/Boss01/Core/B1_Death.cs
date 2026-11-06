using System.Collections;
using UnityEngine;

/// <summary>
/// Boss01 死亡コンポーネント。2フェーズで処理する。
/// Phase1: HP0 → DeathMotion 再生（攻撃判定のみ無効、ボディは残す）
/// Phase2: Player のとどめ攻撃 → ScaleAnimation → 破棄
/// </summary>
public class B1_Death : MonoBehaviour
{
    [Header("DeathMotion タイムアウト（秒）")]
    [SerializeField] private float animationTimeout = 3f;

    [Header("つぶれ演出設定")]
    [SerializeField] private float crushDuration  = 0.15f;
    [SerializeField] private float crushScaleY    = 0.05f;
    [SerializeField] private float disappearDelay = 0.2f;

    [Header("死亡時スカイドーム切り替え（任意）")]
    [SerializeField] private SkydomeBlendSystem skydomeBlendSystem;
    [Tooltip("死亡時に切り替える先のドーム。未設定なら System の Target へ Blend()。")]
    [SerializeField] private GameObject deathSkydome;

    private B1_Boss   boss;
    private B1_Health health;
    private bool      dying;
    private bool      awaitingFinishingBlow;

    private void Awake()
    {
        boss   = GetComponent<B1_Boss>();
        health = GetComponent<B1_Health>();
    }

    private void Start()
    {
        if (health != null)
            health.onDied.AddListener(OnDied);
    }

    private void OnDied()
    {
        if (dying) return;
        dying = true;
        boss?.PlayDefeatedSound();

        if (skydomeBlendSystem != null)
        {
            if (deathSkydome != null) skydomeBlendSystem.BlendTo(deathSkydome);
            else                      skydomeBlendSystem.Blend();
        }

        StartCoroutine(DeathMotionSequence());
    }

    private IEnumerator DeathMotionSequence()
    {
        // 攻撃判定を無効化（ボディコリジョンはとどめ受付のために残す）
        if (boss != null)
        {
            boss.EnableHitbox(false);
            boss.EnableTailHitbox(false);
        }

        // PlayDeath() は B1_DeathState.Enter() が呼ぶので呼ばない

        // アニメ完了イベント or タイムアウトを待つ
        bool animDone = false;
        System.Action onDeathFinished = () => animDone = true;
        if (boss?.AnimatorBridge != null)
            boss.AnimatorBridge.OnDeathFinishedEvent += onDeathFinished;

        float elapsed = 0f;
        while (!animDone && elapsed < animationTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 待機を抜けたら購読を解除する（AnimatorBridge への dangling 購読を残さない）
        if (boss?.AnimatorBridge != null)
            boss.AnimatorBridge.OnDeathFinishedEvent -= onDeathFinished;

        // 最終フレームで固定してループを防ぐ
        boss?.AnimatorBridge?.PauseAnimator();

        // とどめ待機フェーズへ移行
        awaitingFinishingBlow = true;
    }

    /// <summary>PlayerAttack からとどめ一撃として呼ばれる</summary>
    public void TryFinishingBlow()
    {
        if (!awaitingFinishingBlow) return;
        awaitingFinishingBlow = false;
        StartCoroutine(FinishingBlowSequence());
    }

    private IEnumerator FinishingBlowSequence()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        yield return StartCoroutine(CrushAnimation());
        Destroy(gameObject);
    }

    private IEnumerator CrushAnimation()
    {
        Vector3 original = transform.localScale;
        Vector3 target   = new Vector3(original.x * 1.3f, original.y * crushScaleY, original.z * 1.3f);

        float elapsed = 0f;
        while (elapsed < crushDuration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(original, target, Mathf.Clamp01(elapsed / crushDuration));
            yield return null;
        }
        transform.localScale = target;
        yield return new WaitForSeconds(disappearDelay);
    }

    private void OnDestroy()
    {
        if (health != null)
            health.onDied.RemoveListener(OnDied);
    }
}
