/**===========================================================================

 File   : ヒットストップ集約 [HitStopRoot.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 複数の IHitStopable を束ね、Freeze/StopFreeze をまとめて中継するルート。
 対象は Inspector 登録と実行時 Register/Unregister の両方に対応。
 ===========================================================================**/
using System.Collections.Generic;
using UnityEngine;

public class HitStopRoot : MonoBehaviour, IHitStopable {
    [SerializeField] private List<MonoBehaviour> targets;

    private readonly List<IHitStopable> hitStopables = new();

    private void Awake() {
        foreach (var t in targets) {
            if (t is IHitStopable s)
                hitStopables.Add(s);
            else if (t != null)
                Debug.LogWarning($"[HitStopRoot] {t.GetType().Name} は IHitStopable を実装していません。", t);
        }
    }

    public void Register(IHitStopable stopable)   => hitStopables.Add(stopable);
    public void Unregister(IHitStopable stopable) => hitStopables.Remove(stopable);

    public void Freeze(float duration) {
        foreach (var s in hitStopables) s.Freeze(duration);
    }

    public void StopFreeze() {
        foreach (var s in hitStopables) s.StopFreeze();
    }
}
