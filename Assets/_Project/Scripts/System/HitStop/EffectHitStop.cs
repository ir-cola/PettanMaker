/**===========================================================================

 File   : エフェクトヒットストップ [EffectHitStop.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 ヒットストップ中に子の ParticleSystem を一時停止し、復帰時に再生し直す。
 ===========================================================================**/
using UnityEngine;

public class EffectHitStop : HitStopBase {
    private ParticleSystem[] particles;
    private bool[] wasPlaying;   // 凍結開始時に再生中だったか（復帰時はこれだけ Play し直す）

    private void Awake() {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        wasPlaying = new bool[particles.Length];
    }

    protected override void OnFreeze() {
        for (int i = 0; i < particles.Length; i++) {
            if (particles[i] == null) continue;
            wasPlaying[i] = particles[i].isPlaying;
            particles[i].Pause();
        }
    }

    protected override void OnRestore() {
        // 凍結中に再生し直すのは「凍結開始時に再生中だった」ものだけ。
        // 全系統を無条件に Play すると、凍結中に寿命が尽きた単発エフェクトが
        // 復帰の瞬間に再噴出してしまう。
        for (int i = 0; i < particles.Length; i++) {
            if (particles[i] != null && wasPlaying[i]) particles[i].Play();
        }
    }
}
