/**===========================================================================

 File   : UIシール貼付の共通ドライバ [UIStickerPaste.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/11
 -----------------------------------------------------------------------------

===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public static class UIStickerPaste {

    /**=======================================================================
      貼付パラメータ
    =======================================================================**/
    public class Options {
        public float   duration     = 0.7f;                  // 貼り付け全体の秒
        public float   holdPeel     = 0.42f;                 // 飛来中に浮かせておく角の量(0=角折りなし)
        public float   overshoot    = 1.12f;                 // 着地に向けて 1.12→1.0 とわずかに詰める
        public float   tiltDeg      = 9f;                    // 進行方向へ傾けて持ってくる角度
        public float   shimmer      = 1.2f;                  // 接着シマー量(生UI貼付時)
        public Color   shimmerColor = Color.white;
        public Material materialTemplate;                    // 指定時は複製して使用。null は共通Material
        public float   exitDist     = 0f;                    // 画面外開始距離(px)。0=自動(画面長辺×1.1)
        public bool    useUnscaledTime = true;               // 世界停止(timeScale=0)中でも動く
        public bool    keepMaterial = false;                 // 完了後もシールマテリアルを残すか
        public System.Action onPrepared;                     // 初期位置/材質の確定後、最初の描画前に呼ぶ
        public bool    settleEffect = false;                 // 貼り終わりに定着バウンスを入れる
        public float   settleDuration = 0.28f;               // 定着バウンスの秒数
        public float   settleScale = 0.09f;                  // 横へ広がる初動の強さ
        public float   settleShake = 7f;                     // 定着時の減衰揺れ(px)
    }

    /**=======================================================================
      心臓部：画面外(start)→定位置(home)へ飛来しつつ角(_Peel)を押さえる 1 本の
      連続タイムライン。位置/スケール/傾きは 88% までに easeOutQuint で「シューッ」と
      着地し、角押さえは 25%→100% で飛来に重ねて連続的に押さえる。
        onPeel        : 進行に応じて _Peel を書く先(角折りしないなら null)
        onShimmerPhase: 進行(0..1)を渡す先(シマー掃引。不要なら null)
    =======================================================================**/
    public static IEnumerator Motion(
        RectTransform r, Vector3 startPos, Vector3 homePos, Vector3 baseScale, Quaternion baseRot,
        float overshoot, float tilt, float duration, float holdPeel, bool unscaled,
        System.Action<float> onPeel, System.Action<float> onShimmerPhase) {

        if (r == null) yield break;
        float t = 0f;
        duration = Mathf.Max(0.001f, duration);
        while (t < duration) {
            t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            float x = Mathf.Clamp01(t / duration);

            float posX = Mathf.Clamp01(x / 0.88f);
            float inv  = 1f - posX;
            float posE = 1f - inv * inv * inv * inv * inv;                 // easeOutQuint(速く入りなめらかに減速)
            r.localPosition   = Vector3.LerpUnclamped(startPos, homePos, posE);
            r.localScale      = baseScale * Mathf.LerpUnclamped(overshoot, 1.0f, posE);
            r.localRotation   = baseRot * Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(tilt, 0f, posE));

            if (onPeel != null) {
                float peelX = Mathf.Clamp01((x - 0.25f) / 0.75f);
                float peelE = peelX * peelX * (3f - 2f * peelX);          // smoothstep
                onPeel(holdPeel * (1f - peelE));
            }
            if (onShimmerPhase != null) onShimmerPhase(x);
            yield return null;
        }
        r.localPosition = homePos;
        r.localScale    = baseScale;
        r.localRotation = baseRot;
        if (onPeel != null) onPeel(0f);
        if (onShimmerPhase != null) onShimmerPhase(1f);
    }

    /**=======================================================================
      キャプチャ済みテクスチャ札(RawImage)を貼る用の薄いラッパ。HUDIntroReveal が
      自前で GO/材質/テクスチャを用意し、モーションだけをここへ委譲する。
    =======================================================================**/
    public static IEnumerator RunPasteMotion(
        RectTransform r, Material mat, Vector3 startPos, Vector3 homePos,
        float tilt, float duration, float holdPeel, bool unscaled) {

        int peelId = StickerFX.PeelId;
        System.Action<float> onPeel = (mat != null) ? (System.Action<float>)(p => mat.SetFloat(peelId, p)) : null;
        yield return Motion(r, startPos, homePos, Vector3.one, Quaternion.identity,
                            1.12f, tilt, duration, holdPeel, unscaled, onPeel, null);
    }

    /**=======================================================================
      生きた UGUI Graphic を「シールとして貼る」。
        peelGraphic : シール見立てにする絵(吹き出しBG・服シール枠など)。null 可(飛来のみ)
        mover       : 実際に飛来させる Transform(peelGraphic の親＝吹き出しルート等)。
                      null なら peelGraphic 自身を飛ばす。
        homeLocal   : 着地時の mover.localPosition
        fromDir     : 画面外から来る向き(外側)。この角が最後に密着する
    =======================================================================**/
    public static IEnumerator PasteLive(Graphic peelGraphic, RectTransform mover,
                                        Vector3 homeLocal, Vector2 fromDir, Options o = null) {
        o = o ?? new Options();
        if (mover == null) mover = peelGraphic != null ? peelGraphic.rectTransform : null;
        if (mover == null) yield break;

        // --- シールマテリアルを一時適用(接着シマー＋任意の角折り) ---
        Material baseMat = null, mat = null;
        int peelId = StickerFX.PeelId, shimPhaseId = StickerFX.ShimmerPhaseId, shimId = StickerFX.ShimmerId;
        if (peelGraphic != null) {
            baseMat = peelGraphic.material;
            mat = o.materialTemplate != null
                ? new Material(o.materialTemplate)
                : StickerFX.NewApplyUIMaterial();
            if (mat != null) {
                Vector4 uvRect = new Vector4(0, 0, 1, 1);
                Image img = peelGraphic as Image;
                if (img != null && img.sprite != null && img.sprite.texture != null) {
                    Rect tr = img.sprite.textureRect; Texture tx = img.sprite.texture;
                    uvRect = new Vector4(tr.x / tx.width, tr.y / tx.height, tr.width / tx.width, tr.height / tx.height);
                }
                mat.SetVector(StickerFX.UVRectId, uvRect);
                mat.SetFloat(StickerFX.DirXId, fromDir.x);
                mat.SetFloat(StickerFX.DirYId, fromDir.y);
                mat.SetColor(StickerFX.ShimmerColorId, o.shimmerColor);
                mat.SetFloat(shimId, o.shimmer);
                mat.SetFloat(peelId, o.holdPeel);
                mat.SetFloat(shimPhaseId, 0f);
                peelGraphic.material = mat;
            }
        }

        // --- 画面外の開始位置・傾き ---
        float exit = o.exitDist > 0f ? o.exitDist : Mathf.Max(Screen.width, Screen.height) * 1.1f;
        Vector2 dir = fromDir.sqrMagnitude > 1e-4f ? fromDir.normalized : Vector2.up;
        Vector3 startPos = homeLocal + (Vector3)(dir * exit);
        float tilt = (dir.x >= 0f ? -1f : 1f) * o.tiltDeg;

        Vector3    baseScale = mover.localScale == Vector3.zero ? Vector3.one : mover.localScale;
        Quaternion baseRot   = mover.localRotation;

        System.Action<float> onPeel = (mat != null && o.holdPeel > 0f)
            ? (System.Action<float>)(p => mat.SetFloat(peelId, p)) : null;
        System.Action<float> onShim = (mat != null)
            ? (System.Action<float>)(ph => mat.SetFloat(shimPhaseId, ph)) : null;

        // コルーチンの最初の描画で home が一瞬見えないよう、Motion と同じ開始姿勢を先に確定する。
        mover.localPosition = startPos;
        mover.localScale = baseScale * o.overshoot;
        mover.localRotation = baseRot * Quaternion.Euler(0f, 0f, tilt);
        o.onPrepared?.Invoke();

        yield return Motion(mover, startPos, homeLocal, baseScale, baseRot,
                            o.overshoot, tilt, o.duration, o.holdPeel, o.useUnscaledTime, onPeel, onShim);

        if (o.settleEffect)
            yield return SettleMotion(mover, homeLocal, baseScale, baseRot, fromDir, o);

        // --- 後始末：シマーを消して元マテリアルへ ---
        if (mat != null) {
            mat.SetFloat(shimId, 0f);
            if (!o.keepMaterial && peelGraphic != null) {
                peelGraphic.material = baseMat;
                Object.Destroy(mat);
            }
        }
    }

    /**=======================================================================
      貼り終わりの定着インパクト。
      最初に横へ潰れて接着した重さを出し、短い反動と減衰揺れで基準姿勢へ戻す。
    =======================================================================**/
    private static IEnumerator SettleMotion(RectTransform mover, Vector3 homeLocal,
                                            Vector3 baseScale, Quaternion baseRot,
                                            Vector2 fromDir, Options o) {
        if (mover == null) yield break;

        float duration = Mathf.Max(0.001f, o.settleDuration);
        float strength = Mathf.Max(0f, o.settleScale);
        Vector2 tangent = fromDir.sqrMagnitude > 1e-4f
            ? new Vector2(-fromDir.y, fromDir.x).normalized
            : Vector2.right;
        float t = 0f;

        while (t < duration) {
            t += o.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float x = Mathf.Clamp01(t / duration);
            float envelope = (1f - x) * (1f - x);
            float wave = Mathf.Cos(x * Mathf.PI * 3f);

            // 接着面へ一度潰れ、反動しながら収束する。UIなのでZは変えない。
            float sx = 1f + strength * envelope * wave;
            float sy = 1f - strength * 0.55f * envelope * wave;
            mover.localScale = Vector3.Scale(baseScale, new Vector3(sx, sy, 1f));

            float shake = Mathf.Sin(x * Mathf.PI * 6f) * o.settleShake * envelope;
            mover.localPosition = homeLocal + (Vector3)(tangent * shake);
            mover.localRotation = baseRot;
            yield return null;
        }

        mover.localPosition = homeLocal;
        mover.localScale = baseScale;
        mover.localRotation = baseRot;
    }
}
