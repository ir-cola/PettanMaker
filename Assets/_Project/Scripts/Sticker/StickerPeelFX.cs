/**===========================================================================

 File   : シール剥がし演出ヘルパー [StickerPeelFX.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 World/StickerPeel の板ポリシールに共通の「剥がれた後」演出。
 剥がれ切ったシールが、ペラッの勢いのまま剥がし方向へ飛んでいき、
 飛びながらディゾルブで溶けて消える。

 使用側: StageIntroSequence (イントロの白兎シール) /
         StickerPickup (敵撃破ドロップの服シール回収)

===========================================================================**/

using System.Collections;
using UnityEngine;

/**===========================================================================
  シール剥がし演出ヘルパー
===========================================================================**/
public static class StickerPeelFX {

    /** 上方向への剥がれ感を出すカール半径 (板ローカル。シェーダー既定 0.12 より大きめ) */
    public const float CurlRadius = 0.24f;

    static readonly int PeelId       = Shader.PropertyToID("_Peel");
    static readonly int DissolveId   = Shader.PropertyToID("_Dissolve");
    static readonly int CurlRadiusId = Shader.PropertyToID("_CurlRadius");
    static readonly int DirXId       = Shader.PropertyToID("_DirX");
    static readonly int DirYId       = Shader.PropertyToID("_DirY");

    /** 剥がし開始前のマテリアル準備 (_Peel リセット + カール半径設定) */
    public static void PrepareMaterial(Material mat) {
        if (mat == null) return;
        if (mat.HasProperty(PeelId)) mat.SetFloat(PeelId, 0f);
        if (mat.HasProperty(CurlRadiusId)) mat.SetFloat(CurlRadiusId, CurlRadius);
    }

    /**=======================================================================
      剥がれ切ったシールを、ペラッの勢いのまま剥がし方向へ飛ばしながら
      ディゾルブで溶かして消す (飛び終わってから消えるのではなく、飛行中に消える)。
      abort が true を返したら途中で打ち切る (最終状態=完全消滅にして返る)。
      終了時に Transform は元の位置/回転へ戻す (シール位置を敵召喚などの
      基準に使っている呼び出し元があるため。見た目は消滅済みなので影響なし)。
    =======================================================================**/
    public static IEnumerator FlyOffAndDissolve(Transform tf, Material mat, float duration,
                                                System.Func<bool> abort = null) {
        if (tf == null || mat == null) yield break;
        bool canDissolve = mat.HasProperty(DissolveId);

        Vector3 basePos = tf.position;
        Quaternion baseRot = tf.rotation;

        // 剥がし方向 (板ローカル XY) をワールドへ変換し、めくれた勢いの続きとして飛ばす
        float dx = mat.HasProperty(DirXId) ? mat.GetFloat(DirXId) : 1f;
        float dy = mat.HasProperty(DirYId) ? mat.GetFloat(DirYId) : 1f;
        Vector3 drift = tf.rotation * new Vector3(dx, dy, 0f);
        if (drift.sqrMagnitude < 1e-6f) drift = Vector3.up;
        else drift.Normalize();

        float size    = Mathf.Max(0.3f, tf.localScale.x);
        float riseH   = size * 0.3f;    // 上へ
        float driftH  = size * 0.55f;   // 剥がし方向の奥へ

        float t = 0f;
        while (t < duration) {
            if (abort != null && abort()) break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));

            // 勢いよく飛び出して減速 (easeOut)。バウンスはさせない
            float fly = 1f - (1f - p) * (1f - p);
            tf.position = basePos + (Vector3.up * riseH + drift * driftH) * fly;

            // 一方向にひらっとひるがえる (往復スウェイなし)
            tf.rotation = baseRot * Quaternion.Euler(-28f * fly, 0f, 10f * fly);

            // 飛びながら溶けて消える (飛行の序盤からディゾルブ開始)
            if (canDissolve) {
                float d = Mathf.InverseLerp(0.1f, 0.85f, p);
                mat.SetFloat(DissolveId, Mathf.Lerp(-0.15f, 1.15f, d));
            }
            yield return null;
        }
        if (canDissolve) mat.SetFloat(DissolveId, 1.15f);   // 打ち切り時も完全消滅で終える
        if (tf != null) {
            tf.position = basePos;      // 召喚位置などの基準を持ち上げたままにしない
            tf.rotation = baseRot;
        }
    }
}
