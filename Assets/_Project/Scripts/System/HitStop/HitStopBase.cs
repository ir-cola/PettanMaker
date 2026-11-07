/**===========================================================================

 File   : ヒットストップ基底クラス [HitStopBase.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 IHitStopable の共通実装。指定時間だけ非スケール時間で凍結し、
 凍結中は最長 duration を保持する。開始/復帰は派生クラスに委譲する。
 ===========================================================================**/
using System.Collections;
using UnityEngine;

public abstract class HitStopBase : MonoBehaviour, IHitStopable {
    public bool IsFrozen { get; private set; }

    private Coroutine routine;
    private float remainingTime;

    public void Freeze(float duration) {
        if (duration <= 0f) return;
        if (duration > remainingTime) remainingTime = duration;
        if (routine == null) routine = StartCoroutine(FreezeRoutine());
    }

    public void StopFreeze() {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        remainingTime = 0f;
        bool wasFrozen = IsFrozen;
        IsFrozen = false;
        // 凍結していた時だけ復帰処理を呼ぶ。未凍結のまま OnDisable 等で呼ばれても
        // OnRestore を走らせない（派生の EffectHitStop が無関係にパーティクルを再生する事故を防ぐ）。
        if (wasFrozen) OnRestore();
    }

    private IEnumerator FreezeRoutine() {
        IsFrozen = true;
        OnFreeze();
        while (remainingTime > 0f) {
            remainingTime -= Time.unscaledDeltaTime;
            yield return null;
        }
        IsFrozen = false;
        OnRestore();
        routine = null;
    }

    private void OnDisable() => StopFreeze();

    protected abstract void OnFreeze();
    protected abstract void OnRestore();
}
