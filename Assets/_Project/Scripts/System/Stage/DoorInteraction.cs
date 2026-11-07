/**===========================================================================

 File   : ドア出入りインタラクト [DoorInteraction.cs]

 -----------------------------------------------------------------------------
 うさぎ小屋などの「ドア」に付ける汎用インタラクト。中ボスの StageClearGate と
 同じ流儀で、近づくと F プロンプトを出し、F (パッドは RT) で暗転 → ワープ → 復帰。
 内外トグル式: 外にいる時は insidePoint へ、中にいる時は outsidePoint へワープする
 ため「入ったら出られない」を構造的に防ぐ。

  doorPoint    … 近接判定の基準 (ドア中心)。未設定なら自分の Transform。
  insidePoint  … 中へ入る時のワープ先。
  outsidePoint … 外へ出る時のワープ先。
  fPrompt      … F 表示のルート (任意)。近接中のみ表示。

 ※ 演出はすべて Time.unscaled 駆動 (ヒットストップ等の timeScale 低下に影響されない)。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DoorInteraction : MonoBehaviour {

    [Header("参照")]
    [Tooltip("プレイヤー。未設定なら Tag=Player を自動取得")]
    [SerializeField] private Transform player;
    [Tooltip("近接判定の基準 (ドア中心)。未設定なら自分の Transform")]
    [SerializeField] private Transform doorPoint;
    [Tooltip("中へ入る時のワープ先")]
    [SerializeField] private Transform insidePoint;
    [Tooltip("外へ出る時のワープ先")]
    [SerializeField] private Transform outsidePoint;
    [Tooltip("F プロンプトのルート (任意)。近接中のみ表示")]
    [SerializeField] private GameObject fPrompt;

    [Header("インタラクト")]
    [Tooltip("この距離以内でドアに近づくと F で出入りできる")]
    [SerializeField] private float interactRange = 4f;
    [Tooltip("開始時にプレイヤーを「中にいる」状態として扱う")]
    [SerializeField] private bool startInside = false;

    [Header("フェード")]
    [SerializeField] private float fadeOutDuration = 0.35f;
    [SerializeField] private float holdBlack = 0.15f;
    [SerializeField] private float fadeInDuration = 0.45f;
    [SerializeField] private Color fadeColor = Color.black;

    // ---- 内部 ----
    private bool _inside;
    private bool _busy;
    private PlayerLifetimeScope _scope;
    private CanvasGroup _fade;

    private void Awake() {
        if (player == null) {
            var p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null) _scope = player.GetComponent<PlayerLifetimeScope>();
        if (doorPoint == null) doorPoint = transform;
        _inside = startInside;
        if (fPrompt != null) fPrompt.SetActive(false);
    }

    private void Update() {
        if (_busy || player == null || doorPoint == null) return;

        bool near = Vector3.Distance(player.position, doorPoint.position) <= interactRange;
        if (fPrompt != null && fPrompt.activeSelf != near) fPrompt.SetActive(near);
        if (!near) return;

        bool press = (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                  || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
        if (press) StartCoroutine(Go());
    }

    private IEnumerator Go() {
        _busy = true;
        if (fPrompt != null) fPrompt.SetActive(false);
        if (_scope != null) _scope.SetUIMode(true);
        EnsureFadeOverlay();

        yield return Fade(0f, 1f, fadeOutDuration);
        Warp(_inside ? outsidePoint : insidePoint);
        _inside = !_inside;
        if (holdBlack > 0f) yield return new WaitForSecondsRealtime(holdBlack);
        yield return Fade(1f, 0f, fadeInDuration);

        if (_scope != null) _scope.SetUIMode(false);
        _busy = false;
    }

    /// <summary>CharacterController を一時無効化してワープ先へ瞬間移動。</summary>
    private void Warp(Transform target) {
        if (target == null || player == null) return;
        var cc = player.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        player.position = target.position;
        player.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        if (cc != null) cc.enabled = was;
    }

    private IEnumerator Fade(float from, float to, float dur) {
        if (_fade == null) yield break;
        _fade.alpha = from;
        if (dur <= 0f) { _fade.alpha = to; yield break; }
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            _fade.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        _fade.alpha = to;
    }

    private void EnsureFadeOverlay() {
        if (_fade != null) return;
        var go = new GameObject("Door_Fade");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
        var imgGo = new GameObject("Black");
        imgGo.transform.SetParent(go.transform, false);
        var img = imgGo.AddComponent<Image>();
        img.color = fadeColor; img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _fade = cg;
    }

    // エディタでワープ点を見やすく
    private void OnDrawGizmosSelected() {
        Gizmos.color = Color.cyan;
        if (doorPoint != null) Gizmos.DrawWireSphere(doorPoint.position, interactRange);
        Gizmos.color = Color.green;
        if (insidePoint != null) Gizmos.DrawSphere(insidePoint.position, 0.3f);
        Gizmos.color = Color.yellow;
        if (outsidePoint != null) Gizmos.DrawSphere(outsidePoint.position, 0.3f);
    }
}
