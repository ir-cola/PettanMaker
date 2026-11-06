/**===========================================================================

 File   : グローバルヒットストップ [GlobalHitStop.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------
 Time.timeScale を一瞬だけ落として戻す「全体ヒットストップ」。既存の
 IHitStopable と併用でき、命中の瞬間に全画面で「ズドンッ」の重みを出す。
 復帰は Runner の Update (非スケール時間) 駆動でコルーチンを使わない
 (実行時生成GOのコルーチンは環境により停止する事故があるため)。多重呼び出しは
 「延長」扱い、timeScale が既に 0 のときは何もしない (unscaled 駆動の演出は継続)。
 ===========================================================================**/

using UnityEngine;

public static class GlobalHitStop {

    private class Runner : MonoBehaviour {
        public float endTime;         // 非スケール時間での復帰時刻
        public float restoreScale = 1f;
        public float appliedScale = 1f;   // 自分が設定した停止中の timeScale (外部ポーズ検出用)
        public bool frozen;

        private void Update() {
            if (!frozen) return;
            if (Time.unscaledTime >= endTime) {
                // 停止中にポーズ等で timeScale が外部から変えられていたら、こちらの復帰値で
                // 上書きしない (勝手にポーズ解除してしまうバグを防ぐ)。自分の停止値のままの時だけ戻す。
                if (Mathf.Abs(Time.timeScale - appliedScale) < 0.0001f)
                    Time.timeScale = restoreScale;
                frozen = false;
            }
        }
    }

    private static Runner runner;

    /// <summary>duration 秒だけ timeScale を scale まで落とす。</summary>
    public static void Play(float duration = 0.09f, float scale = 0.05f) {
        if (duration <= 0f) return;

        if (runner == null) {
            var go = new GameObject("GlobalHitStop (Auto)") {
                hideFlags = HideFlags.HideInHierarchy
            };
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

        // ポーズ等で完全停止中 (自分の凍結ではない) は触らない
        if (Time.timeScale <= 0.001f && !runner.frozen) return;

        if (!runner.frozen) runner.restoreScale = Time.timeScale;   // 凍結前の値 (通常 1)
        // 「延長」扱い: 凍結中に短い停止が来ても復帰時刻は縮めない
        // (クリティカルの長い全体停止が、同フレームの通常ヒット分で切られるのを防ぐ)
        float newEndTime = Time.unscaledTime + duration;
        runner.endTime = runner.frozen ? Mathf.Max(runner.endTime, newEndTime) : newEndTime;
        runner.frozen = true;
        Time.timeScale = Mathf.Min(Time.timeScale, Mathf.Max(0.01f, scale));
        runner.appliedScale = Time.timeScale;   // 実際に設定した停止値を記録 (復帰時の外部変更判定に使う)
    }
}
