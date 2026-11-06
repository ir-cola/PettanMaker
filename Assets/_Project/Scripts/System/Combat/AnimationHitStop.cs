using System.Collections;
using UnityEngine;

/// <summary>
/// 被弾時にキャラクター自身の Animator を一瞬だけ止める「ヒットストップ」演出。
/// Time.timeScale ではなく Animator.speed を操作するため、キャラ単位で完結し、
/// 他のオブジェクトやゲーム全体の進行には影響しない。
/// 停止時間は実時間（unscaledDeltaTime）で計測するので、別途スロー演出が
/// 走っていても意図した長さで復帰する。
///
/// IHitStopable を実装しているため、CombatSystem が DamageContext.sourceHitstop /
/// targetHitstop に応じて Freeze(duration) を呼ぶと、攻撃が当たった瞬間に
/// 攻撃側・被弾側の Animator をピタッと止められる。
/// </summary>
public class AnimationHitStop : MonoBehaviour, IHitStopable
{
    [SerializeField] private Animator animator;

    [Tooltip("停止させる時間（秒・実時間）")]
    [SerializeField, Min(0f)] private float duration = 0.08f;

    [Tooltip("停止中の再生速度。0で完全停止。少し残すと重い「タメ」になる")]
    [SerializeField, Range(0f, 1f)] private float frozenSpeed = 0f;

    [Tooltip("復帰後に戻す再生速度（通常は1）")]
    [SerializeField, Min(0f)] private float normalSpeed = 1f;

    private Coroutine routine;
    private float freezeEndTime; // 実行中フリーズの復帰時刻 (unscaled)。短い後発リクエストの上書き防止に使う

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    /// <summary>
    /// 使用する Animator を外部から差し込む（基底クラスやブリッジ経由で共有する用）。
    /// null が渡された場合は Awake で自己解決した参照を維持する。
    /// </summary>
    public void SetAnimator(Animator target)
    {
        if (target != null)
            animator = target;
    }

    /// <summary>停止時間と停止中速度を外部（Settings SO など）から設定する。</summary>
    public void Configure(float newDuration, float newFrozenSpeed)
    {
        duration = Mathf.Max(0f, newDuration);
        frozenSpeed = Mathf.Clamp01(newFrozenSpeed);
    }

    /// <summary>デフォルト時間でヒットストップを発生させる。</summary>
    public void HitStop() => HitStop(duration);

    // ---- IHitStopable ----
    // CombatSystem から DamageContext.sourceHitstop / targetHitstop で呼ばれる。
    /// <summary>指定時間フリーズする（HitStop と同一挙動）。</summary>
    public void Freeze(float freezeDuration) => HitStop(freezeDuration);

    /// <summary>フリーズを即座に解除して再生速度を元に戻す。</summary>
    public void StopFreeze()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        if (animator != null)
            animator.speed = normalSpeed;
    }

    /// <summary>指定時間だけ Animator を停止させる。</summary>
    public void HitStop(float stopDuration)
    {
        if (!isActiveAndEnabled || animator == null || stopDuration <= 0f)
            return;

        // 連続被弾で重なった場合は一度元の速度へ戻してから停止し直す。
        // ただし実行中フリーズの残り時間の方が長い場合は上書きしない
        // （クリティカルの長い停止が、直後に来る通常被弾の短い停止で切られるのを防ぐ）。
        if (routine != null)
        {
            if (Time.unscaledTime + stopDuration <= freezeEndTime)
                return;
            StopCoroutine(routine);
            animator.speed = normalSpeed;
        }

        freezeEndTime = Time.unscaledTime + stopDuration;
        routine = StartCoroutine(HitStopRoutine(stopDuration));
    }

    private IEnumerator HitStopRoutine(float stopDuration)
    {
        animator.speed = frozenSpeed;

        float elapsed = 0f;
        while (elapsed < stopDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        animator.speed = normalSpeed;
        routine = null;
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        // 停止途中で無効化されても再生速度を取り残さない
        if (animator != null)
            animator.speed = normalSpeed;
    }
}
