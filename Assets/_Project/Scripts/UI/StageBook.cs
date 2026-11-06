/**===========================================================================

 File   : ステージ選択の本 [StageBook.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30
 -----------------------------------------------------------------------------

   本棚の1冊。マウスを重ねると棚から少し抜き出る(上がる+手前へ+傾く)。
   クリックで対応ステージへ遷移、未実装ステージは Coming Soon を表示。

 ===========================================================================**/
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class StageBook : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {

    [Header("遷移先")]
    [Tooltip("読み込むステージシーン名(空 or comingSoon で未実装扱い)")]
    [SerializeField] string _sceneName = "Stage01";
    [SerializeField] bool _comingSoon = false;
    [SerializeField] GameObject _comingSoonPopup;
    [Tooltip("シール剥がし遷移を使う")]
    [SerializeField] bool _useStickerTransition = true;

    [Header("入場確認(任意)")]
    [Tooltip("設定すると、本をクリックした際に確認ダイアログを表示してから遷移する")]
    [SerializeField] EnterConfirmDialog _confirmDialog;
    [SerializeField] string _worldTitle = "";

    [Header("ホバー演出")]
    [Tooltip("棚から上に抜き出る量(px)")]
    [SerializeField] float _hoverRise = 70f;
    [Tooltip("手前(左)へ寄せる量(px)")]
    [SerializeField] float _hoverShiftX = -30f;
    [SerializeField] float _hoverScale = 1.08f;
    [Tooltip("傾き(度)")]
    [SerializeField] float _hoverTilt = -4f;
    [Tooltip("追従の速さ")]
    [SerializeField] float _speed = 12f;

    RectTransform _rect;
    Vector2 _homePos;
    Vector3 _homeScale;
    Quaternion _homeRot;
    int _homeSibling;
    bool _hover;

    void Awake() {
        _rect = (RectTransform)transform;
    }

    void OnEnable() {
        _rect = (RectTransform)transform;
        _homePos     = _rect.anchoredPosition;
        _homeScale   = _rect.localScale;
        _homeRot     = _rect.localRotation;
        _homeSibling = _rect.GetSiblingIndex();
    }

    public void OnPointerEnter(PointerEventData e) {
        _hover = true;
        _rect.SetAsLastSibling(); // 抜き出た本を手前に
    }

    public void OnPointerExit(PointerEventData e) {
        _hover = false;
    }

    public void OnPointerClick(PointerEventData e) {
        Select();
    }

    /** ゲームパッド選択用: ホバー状態を外部から切り替える */
    public void SetHovered(bool on) {
        if (on) OnPointerEnter(null);
        else OnPointerExit(null);
    }

    /** ゲームパッド決定用: クリックと同じ動作 */
    public void Confirm() {
        Select();
    }

    void Update() {
        // ロック中(未開放)の本は棚から抜き出ない。ホバー時にわずかに反応するのみ。
        bool locked = _comingSoon;
        bool pull = _hover && !locked;
        Vector2 targetPos      = pull ? _homePos + new Vector2(_hoverShiftX, _hoverRise) : _homePos;
        Vector3 targetScale    = _hover ? _homeScale * (locked ? 1.02f : _hoverScale) : _homeScale;
        Quaternion targetRot   = pull ? _homeRot * Quaternion.Euler(0f, 0f, _hoverTilt) : _homeRot;

        float t = 1f - Mathf.Exp(-_speed * Time.unscaledDeltaTime);
        _rect.anchoredPosition = Vector2.Lerp(_rect.anchoredPosition, targetPos, t);
        _rect.localScale       = Vector3.Lerp(_rect.localScale, targetScale, t);
        _rect.localRotation    = Quaternion.Slerp(_rect.localRotation, targetRot, t);

        // 棚に戻りきったら元の重なり順へ
        if (!_hover && (_rect.anchoredPosition - _homePos).sqrMagnitude < 0.5f) {
            if (_rect.GetSiblingIndex() != _homeSibling) _rect.SetSiblingIndex(_homeSibling);
        }
    }

    void Select() {
        if (_comingSoon || string.IsNullOrEmpty(_sceneName)) {
            if (_comingSoonPopup != null) _comingSoonPopup.SetActive(true);
            return;
        }
        // 確認ダイアログがあれば、遷移前に「この世界に入りますか？」を表示
        if (_confirmDialog != null) {
            _confirmDialog.Show(_sceneName, _worldTitle);
            return;
        }
        if (_useStickerTransition) SceneLoader.Load(_sceneName); // ロード画面付き遷移
        else SceneManager.LoadScene(_sceneName);
    }
}
