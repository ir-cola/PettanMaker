using System.Collections;
using UnityEngine;

/// <summary>
/// 回復シールが空中の生成位置から地面へ「ひらひら」と舞い落ちる演出。
/// 落下中は左右に揺れながら傾き、地面に近づくほど揺れが収まって
/// 最後はぱたんと平らに寝る。着地したら回収コンポーネントを有効化する。
/// B1_PhaseHealStickerSpawner が生成直後にアタッチして駆動する（前提知識不要の自己完結型）。
/// </summary>
public class B1_HealStickerFall : MonoBehaviour
{
    private const float TAU = Mathf.PI * 2f;

    /// <summary>
    /// 落下を開始する。呼び出し時の transform.position が開始点。
    /// </summary>
    /// <param name="endPos">着地点（地面）。</param>
    /// <param name="endRot">着地時の向き（平らな貼り付き姿勢）。</param>
    /// <param name="duration">落下にかける秒数。</param>
    /// <param name="sway">左右の揺れ幅 (m)。</param>
    /// <param name="freq">揺れの速さ。</param>
    /// <param name="angle">ひらひらの傾き角 (度)。</param>
    /// <param name="enableOnLand">着地時に有効化するコンポーネント（StickerPickup 等、無ければ null）。</param>
    public void Begin(Vector3 endPos, Quaternion endRot, float duration,
                      float sway, float freq, float angle, Behaviour enableOnLand)
    {
        StartCoroutine(FallRoutine(transform.position, endPos, endRot,
                                   Mathf.Max(0.01f, duration), sway, Mathf.Max(0f, freq),
                                   angle, enableOnLand));
    }

    private IEnumerator FallRoutine(Vector3 startPos, Vector3 endPos, Quaternion endRot,
                                    float duration, float sway, float freq, float angle,
                                    Behaviour enableOnLand)
    {
        // 位相と揺れの向きをランダム化して毎回違う舞い方にする。
        float phase = Random.value * TAU;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float tc = Mathf.Clamp01(t);

            // 地面に近づくほど揺れ・傾きを収束させる係数。
            float damp = 1f - tc;

            // 縦：ふわっと落ちる（SmoothStep で加速しすぎない）。
            Vector3 basePos = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, tc));

            // 横：直交する 2 軸の正弦で旋回するように揺らす。
            float wx = Mathf.Sin(phase + tc * freq * TAU);
            float wz = Mathf.Cos(phase * 0.7f + tc * freq * TAU * 0.6f);
            Vector3 offset = Vector3.right   * (wx * sway * damp)
                           + Vector3.forward * (wz * sway * 0.6f * damp);
            transform.position = basePos + offset;

            // 回転：揺れに合わせて傾け、着地に向けて endRot（平ら）へ収束させる。
            Quaternion wobble = Quaternion.AngleAxis(wx * angle * damp,       Vector3.right)
                              * Quaternion.AngleAxis(wz * angle * 0.6f * damp, Vector3.forward);
            transform.rotation = wobble * endRot;

            yield return null;
        }

        // 着地：正確な位置・向きに合わせて回収を有効化する。
        transform.position = endPos;
        transform.rotation = endRot;
        if (enableOnLand != null) enableOnLand.enabled = true;

        Destroy(this);
    }
}
