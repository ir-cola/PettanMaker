/**===========================================================================

 File   : オトモ吹き出しドライバ [CompanionSpeaker.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/11
 -----------------------------------------------------------------------------

 既存の吹き出しUI（Canvas/HUD/RigthUp の BookComment_BG＋Text／BookComment_Icon）を
 「乗っ取って」しゃべらせるドライバ。もともと用意されたポップアップをそのまま使い、
 顔スロットへラベル画像(顔なし＋目＋口)を重ねて口パクさせる。

   ・AttachTo(popupRoot, textTMP, faceSlot, faceSet) で既存UIに取り付け
   ・SayRoutine … 1文字ずつ表示しながら口をパクパク（unscaled 時間）
   ・AppearRoutine … ポップアップをポップ表示
   ・時間は全て unscaled（世界停止 timeScale=0 中でも動く）

===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class CompanionFaceSet {
    public Sprite   baseFace;                                   // label_kaonasi
    public Sprite[] eyeFrames;                                  // label_eye_01..04
    public Sprite[] mouthFrames;                                // label_Mouth_01..04 (0=閉じ想定)
    public TMP_FontAsset font;                                  // 日本語対応フォント(YuMincho SDF 等)
    [Tooltip("顔スロットに対する顔の拡大率(1=スロット一杯)")]
    public float faceScale = 1f;
}

public class CompanionSpeaker : MonoBehaviour {

    CompanionFaceSet _set;
    RectTransform _root;          // ポップアップ本体(出現ポップの対象)
    TMP_Text _tmp;
    Image _eyeImg, _mouthImg;
    RectTransform _faceRoot;      // 生成したラベル顔コンテナ
    Coroutine _flap, _blink;

    public RectTransform Root     => _root;
    public RectTransform FaceRoot => _faceRoot;

    /**=======================================================================
      既存ポップアップに取り付ける
        popupRoot : ポップの拡縮対象(Sticker_UI_3 等)
        tmp       : セリフを流すテキスト(BookComment_BG/Text)
        faceSlot  : ラベル顔を入れる枠(BookComment_Icon)
    =======================================================================**/
    public static CompanionSpeaker AttachTo(RectTransform popupRoot, TMP_Text tmp, RectTransform faceSlot, CompanionFaceSet set) {
        var host = popupRoot != null ? popupRoot.gameObject : (tmp != null ? tmp.gameObject : faceSlot.gameObject);
        var cs = host.AddComponent<CompanionSpeaker>();
        cs._set = set;
        cs._root = popupRoot;
        cs._tmp = tmp;
        if (tmp != null) {
            if (set.font != null) tmp.font = set.font;
            tmp.richText = true;
            tmp.text = "";
        }
        cs.BuildFace(faceSlot);
        return cs;
    }

    void BuildFace(RectTransform slot) {
        if (slot == null) return;
        _faceRoot = TutorialUIGfx.Child(slot, "LabelFace");
        Stretch(_faceRoot, 0f);
        _faceRoot.localScale = Vector3.one * Mathf.Max(0.1f, _set.faceScale);
        AddLayer(_faceRoot, _set.baseFace, "Base");
        _eyeImg   = AddLayer(_faceRoot, EyeFrame(0),   "Eye");
        _mouthImg = AddLayer(_faceRoot, MouthFrame(0), "Mouth");
    }

    Image AddLayer(RectTransform parent, Sprite sprite, string name) {
        var rt = TutorialUIGfx.Child(parent, name);
        Stretch(rt, 0f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.preserveAspect = true; img.raycastTarget = false;
        img.enabled = sprite != null;
        return img;
    }
    static void Stretch(RectTransform rt, float inset) {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    Sprite EyeFrame(int i)   => (_set.eyeFrames   != null && _set.eyeFrames.Length   > 0) ? _set.eyeFrames  [Mathf.Clamp(i, 0, _set.eyeFrames.Length   - 1)] : null;
    Sprite MouthFrame(int i) => (_set.mouthFrames != null && _set.mouthFrames.Length > 0) ? _set.mouthFrames[Mathf.Clamp(i, 0, _set.mouthFrames.Length - 1)] : null;

    /**=======================================================================
      出現ポップ
    =======================================================================**/
    public IEnumerator AppearRoutine(float dur = 0.28f) {
        StartIdle();
        if (_root == null) yield break;
        Vector3 baseScale = Vector3.one;
        _root.localScale = baseScale * 0.5f;
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            _root.localScale = baseScale * EaseOutBack(Mathf.Clamp01(t / dur));
            yield return null;
        }
        _root.localScale = baseScale;
    }

    /**=======================================================================
      しゃべる(1文字ずつ + 口パク)
    =======================================================================**/
    public IEnumerator SayRoutine(string message, float charsPerSec = 24f) {
        if (_tmp == null) yield break;
        _tmp.text = message;
        _tmp.ForceMeshUpdate();
        int total = _tmp.textInfo.characterCount;
        _tmp.maxVisibleCharacters = 0;

        if (_flap != null) StopCoroutine(_flap);
        _flap = StartCoroutine(MouthFlap());

        float shown = 0f;
        while (_tmp.maxVisibleCharacters < total) {
            shown += Time.unscaledDeltaTime * charsPerSec;
            _tmp.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        _tmp.maxVisibleCharacters = total;
        if (_flap != null) { StopCoroutine(_flap); _flap = null; }
        SetMouth(0);
    }

    public IEnumerator SayAndHold(string message, float hold = 0.6f, float charsPerSec = 24f) {
        yield return SayRoutine(message, charsPerSec);
        float t = 0f;
        while (t < hold) { t += Time.unscaledDeltaTime; yield return null; }
    }

    void StartIdle() {
        SetMouth(0);
        if (_blink == null && _set.eyeFrames != null && _set.eyeFrames.Length >= 2)
            _blink = StartCoroutine(Blink());
    }

    IEnumerator MouthFlap() {
        int open = _set.mouthFrames != null ? _set.mouthFrames.Length : 0;
        int i = 1;
        while (true) {
            SetMouth(0);
            yield return new WaitForSecondsRealtime(0.05f);
            SetMouth(open > 1 ? 1 + (i % (open - 1)) : 0);
            i++;
            yield return new WaitForSecondsRealtime(0.07f);
        }
    }

    IEnumerator Blink() {
        while (true) {
            yield return new WaitForSecondsRealtime(Random.Range(2.2f, 4.0f));
            if (_eyeImg == null || _set.eyeFrames == null || _set.eyeFrames.Length < 2) continue;
            _eyeImg.sprite = _set.eyeFrames[_set.eyeFrames.Length - 1];
            yield return new WaitForSecondsRealtime(0.09f);
            _eyeImg.sprite = _set.eyeFrames[0];
        }
    }

    void SetMouth(int idx) {
        if (_mouthImg == null || _set.mouthFrames == null || _set.mouthFrames.Length == 0) return;
        idx = Mathf.Clamp(idx, 0, _set.mouthFrames.Length - 1);
        _mouthImg.sprite = _set.mouthFrames[idx];
        _mouthImg.enabled = _mouthImg.sprite != null;
    }

    /// <summary>取り付けたラベル顔を破棄し、コンポーネントも外す(元UIは呼び出し側で戻す)。</summary>
    public void Detach() {
        if (_flap != null)  { StopCoroutine(_flap);  _flap = null; }
        if (_blink != null) { StopCoroutine(_blink); _blink = null; }
        if (_faceRoot != null) Destroy(_faceRoot.gameObject);
        Destroy(this);
    }

    static float EaseOutBack(float x) {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
