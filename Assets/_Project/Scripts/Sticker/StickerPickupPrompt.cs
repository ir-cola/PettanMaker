/**===========================================================================

 File   : シール回収プロンプト [StickerPickupPrompt.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 ドロップした服シールの上に出す「右クリック / X で はがす」のビルボードUI。
 プレイヤーが近づくとぽよんと出現し、離れると引っ込む。
 アイコンは最後に使った入力デバイス (マウス⇔パッド) で自動で切り替わる。

 シールは実行時生成のため、見た目一式もここで動的に組み立てる
 (アセット参照は Resources/PickupPromptAssets)。

===========================================================================**/

using System.Collections;
using UnityEngine;

/**===========================================================================
  シール回収プロンプト
===========================================================================**/
public class StickerPickupPrompt : MonoBehaviour {

    const float PopDuration    = 0.45f;   // 出現ぽよんの時間
    const float ShrinkDuration = 0.15f;   // 消えるときの時間

    private Transform _player;
    private float _showRange = 2.5f;
    private Vector3 _baseScale = new Vector3(1.2f, 1.2f, 1f);
    private GameObject _mouseIcon;
    private GameObject _padIcon;
    private Coroutine _anim;
    private bool _shown;

    /**=======================================================================
      シール位置の上にプロンプトを生成する (アセットが無ければ null)
    =======================================================================**/
    public static StickerPickupPrompt Create(Vector3 stickerPos, float showRange) {
        var assets = Resources.Load<PickupPromptAssets>("PickupPromptAssets");
        if (assets == null) {
            Debug.LogWarning("[StickerPickupPrompt] Resources/PickupPromptAssets が見つかりません");
            return null;
        }
        var go = new GameObject("PickupPrompt");
        go.transform.position = stickerPos + Vector3.up * 1.05f;
        var prompt = go.AddComponent<StickerPickupPrompt>();
        prompt._showRange = showRange;
        prompt.Build(assets);
        go.AddComponent<Billboard>();   // 常にカメラへ正対
        return prompt;
    }

    void Build(PickupPromptAssets a) {
        transform.localScale = Vector3.zero;    // 接近するまで畳んでおく

        // 背景パネル (チュートリアルビルボードと同じ半透明黒)
        var bg = gameObject.AddComponent<SpriteRenderer>();
        bg.sprite = a.backSprite;
        bg.color = new Color(0f, 0f, 0f, 0.5f);

        _mouseIcon = MakeIcon("Icon_Mouse", a.mouseIcon, a.iconMaterial);
        _padIcon   = MakeIcon("Icon_Pad",   a.padIcon,   a.iconMaterial);
        _padIcon.SetActive(false);

        // 「はがす」ラベル (チュートリアルビルボードと同じ体裁)
        var lgo = new GameObject("Label");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        lgo.transform.localScale = Vector3.one * 0.1f;
        var tmp = lgo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "はがす";
        if (a.font != null) tmp.font = a.font;
        tmp.fontSize = 15f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(8f, 1.8f);
        if (a.labelMaterial != null) tmp.fontSharedMaterial = a.labelMaterial;
    }

    GameObject MakeIcon(string name, Sprite sprite, Material mat) {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (mat != null) {
            sr.sharedMaterial = mat;
            go.AddComponent<SpriteGlowOutline>();
        }
        return go;
    }

    void Update() {
        if (_player == null) {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
            if (_player == null) return;
        }

        // 接近判定 (水平距離)
        Vector3 d = _player.position - transform.position;
        d.y = 0f;
        bool near = d.sqrMagnitude <= _showRange * _showRange;
        if (near != _shown) {
            _shown = near;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(near ? PopIn() : ShrinkOut());
        }

        // 入力デバイスでアイコン切替
        bool pad = InputDeviceWatcher.GamepadActive;
        if (_mouseIcon != null && _mouseIcon.activeSelf == pad) _mouseIcon.SetActive(!pad);
        if (_padIcon != null && _padIcon.activeSelf != pad) _padIcon.SetActive(pad);
    }

    // ぽよんと出現 (減衰バネ: TutorialBillboard と同じ手触り)
    IEnumerator PopIn() {
        const float damping = 5f;
        const float omega   = 3f * Mathf.PI;
        float startX = transform.localScale.x / Mathf.Max(0.01f, _baseScale.x);
        float t = PopDuration * Mathf.Clamp01(startX);   // 途中から出直すときは続きから
        while (t < PopDuration) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / PopDuration);
            float ease = 1f - Mathf.Exp(-damping * p) * Mathf.Cos(omega * p);
            transform.localScale = _baseScale * ease;
            yield return null;
        }
        transform.localScale = _baseScale;
    }

    // すっと引っ込む
    IEnumerator ShrinkOut() {
        Vector3 from = transform.localScale;
        float t = 0f;
        while (t < ShrinkDuration) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / ShrinkDuration);
            transform.localScale = Vector3.Lerp(from, Vector3.zero, p * p);
            yield return null;
        }
        transform.localScale = Vector3.zero;
    }
}
