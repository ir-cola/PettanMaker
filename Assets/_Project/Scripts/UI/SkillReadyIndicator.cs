/**===========================================================================

 File   : スキル発動可能インジケータ [SkillReadyIndicator.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------
 HUD の Q アイコンに付ける。SkillGauge が満タン&スキル服着用中&未発動のとき、
 グレーアウトのアイコンを明るく点灯させ、後ろの加算ハロー(glowImage)を
 明滅させて「発動できる」ことを示す。溜め途中は進捗で少しずつ明るくする。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.UI;

public class SkillReadyIndicator : MonoBehaviour {

    [Header("参照")]
    [Tooltip("Q 本体の Image。未設定なら同じ GameObject から取得")]
    [SerializeField] private Image iconImage;
    [Tooltip("後ろに置く加算グロー(UI/AdditiveGlow)の Image。任意")]
    [SerializeField] private Image glowImage;
    [Tooltip("スキルの有無/発動状態の取得元。未設定なら自動取得")]
    [SerializeField] private PlayerClothSkills clothSkills;

    [Header("本体の色")]
    [SerializeField] private Color grayColor     = new Color(0.35f, 0.35f, 0.40f, 0.85f);
    [SerializeField] private Color chargingColor = new Color(0.70f, 0.70f, 0.72f, 1f);
    [SerializeField] private Color readyColor    = Color.white;

    [Header("グロー")]
    [SerializeField] private Color glowColor      = new Color(1f, 0.86f, 0.45f, 1f);
    [SerializeField] private float glowMaxAlpha   = 0.9f;
    [SerializeField] private float glowPulseAmount= 0.4f;
    [SerializeField] private float glowScale      = 1.3f;

    [Header("演出")]
    [SerializeField] private float colorLerp  = 10f;
    [SerializeField] private float pulseSpeed  = 3.2f;
    [SerializeField] private float pulseScale  = 0.10f;

    private RectTransform _iconRT;
    private RectTransform _glowRT;
    private Vector3       _iconBaseScale = Vector3.one;
    private float         _glowA = 0f;

    private void Awake() {
        if (iconImage == null) iconImage = GetComponent<Image>();
        if (iconImage != null) {
            _iconRT = iconImage.rectTransform;
            _iconBaseScale = _iconRT.localScale;
        }
        if (glowImage != null) {
            _glowRT = glowImage.rectTransform;
            Color c = glowColor; c.a = 0f; glowImage.color = c;
        }
        if (clothSkills == null) clothSkills = FindObjectOfType<PlayerClothSkills>();
    }

    private void Update() {
        SkillGauge g = SkillGauge.Active;
        bool hasSkill = clothSkills != null && clothSkills.HasActiveSkill;
        bool running  = clothSkills != null && clothSkills.IsActiveRunning;
        float fill    = g != null ? g.Value01 : 0f;
        bool ready    = hasSkill && g != null && g.IsFull && !running;

        float dt = Time.unscaledDeltaTime;
        float k  = 1f - Mathf.Exp(-colorLerp * dt);
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed);

        // ---- 本体の色 ----
        Color targetCol;
        if (!hasSkill)  targetCol = grayColor;
        else if (ready) targetCol = readyColor;
        else            targetCol = Color.Lerp(grayColor, chargingColor, fill);
        if (iconImage != null)
            iconImage.color = Color.Lerp(iconImage.color, targetCol, k);

        // ---- 発動可能時の拡縮パルス ----
        float pulse = ready ? (1f + pulseScale * wave) : 1f;
        if (_iconRT != null)
            _iconRT.localScale = Vector3.Lerp(_iconRT.localScale, _iconBaseScale * pulse, k);

        // ---- グローの明滅 ----
        float targetGlow = ready
            ? glowMaxAlpha * (1f - glowPulseAmount + glowPulseAmount * wave)
            : 0f;
        _glowA = Mathf.Lerp(_glowA, targetGlow, k);
        if (glowImage != null) {
            Color gc = glowColor; gc.a = _glowA; glowImage.color = gc;
            if (_glowRT != null)
                _glowRT.localScale = _iconBaseScale * glowScale * pulse;
        }
    }
}
