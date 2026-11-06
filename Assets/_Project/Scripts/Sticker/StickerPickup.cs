/**===========================================================================

 File   : シール回収クラス [StickerPickup.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 地面のデカール／板ポリシールにアタッチし、プレイヤーが近づいて
 右クリック(またはX)で剥がして回収する処理を担う。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/**===========================================================================
  シール回収クラス
  地面に貼られたデカールオブジェクトにアタッチする
  プレイヤーが近づいて右クリックすると回収できる
===========================================================================**/
public class StickerPickup : MonoBehaviour {

    [Header("回収設定")]
    [SerializeField] private float pickupRange = 2.0f; // 回収可能距離
    [SerializeField] private float peelDuration = 0.7f; // 剥がし演出の長さ (秒)
    [SerializeField] private float vanishDuration = 0.75f; // 舞い上がり+ディゾルブの長さ (秒)

    [Header("回収時効果")]
    [Tooltip("回収時にプレイヤーのHPを最大まで回復する。消費アイテム用なのでシール帳には追加しない。")]
    [SerializeField] private bool fullHealOnPickup = false;

    // 剥がし演出中の多重回収防止
    private bool isPeeling = false;

    // このデカールが持つシールデータ
    private StickerData stickerData;

    // UI表示用スプライト（EnemyDeath から渡される）
    private Sprite displaySprite;

    // 内部変数
    private Transform playerTransform;
    private StickerPickupPrompt prompt;   // 「はがす」操作プロンプト

    /**=======================================================================
      初期化（EnemyDeath から呼ばれる）
    =======================================================================**/
    public void Initialize(StickerData data, Sprite sprite = null) {
        stickerData = data;
        displaySprite = sprite;
    }

    /**=======================================================================
      開始処理
    =======================================================================**/
    void Start() {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) {
            playerTransform = player.transform;
        }
        // 近づくと「右クリック / X ではがす」のビルボードUIを出す
        prompt = StickerPickupPrompt.Create(transform.position, pickupRange + 0.6f);
    }

    void OnDestroy() {
        if (prompt != null) Destroy(prompt.gameObject);
    }

    /**=======================================================================
      更新処理
    =======================================================================**/
    void Update() {
        if (playerTransform == null) return;

        // 右クリック / X(西) かつ射程内
        bool pressed = (Mouse.current?.rightButton.wasPressedThisFrame ?? false)
                    || (Gamepad.current?.buttonWest.wasPressedThisFrame ?? false);
        if (pressed && IsInRange()) {
            TryPickup();
        }
    }

    /**=======================================================================
      射程内判定
    =======================================================================**/
    private bool IsInRange() {
        float distance = Vector3.Distance(transform.position, playerTransform.position);
        return distance <= pickupRange;
    }

    /**=======================================================================
      回収処理
    =======================================================================**/
    private void TryPickup() {
        if (isPeeling) return;

        if (fullHealOnPickup) {
            PlayerHealth playerHealth = playerTransform.GetComponent<PlayerHealth>()
                ?? playerTransform.GetComponentInParent<PlayerHealth>()
                ?? playerTransform.GetComponentInChildren<PlayerHealth>();

            if (playerHealth != null) {
                playerHealth.Heal(playerHealth.MaxHP - playerHealth.CurrentHP);
                Debug.Log("[StickerPickup] 回復シールを回収：HP全回復");
            } else {
                Debug.LogWarning("[StickerPickup] PlayerHealth が見つからないため回復できませんでした。", this);
            }
        }

        if (!fullHealOnPickup && stickerData != null) {
            // インベントリに追加（表示スプライトも渡す）
            StickerInventory inventory = StickerInventory.Instance;
            if (inventory != null) {
                inventory.AddSticker(stickerData, displaySprite);
                Debug.Log($"[StickerPickup] 回収：{stickerData.stickerName}");
            }
        }
        else if (!fullHealOnPickup) {
            Debug.Log("[StickerPickup] シールなし（剥がして削除のみ）");
        }

        // 剥がし始めたらプロンプトは消す
        if (prompt != null) { Destroy(prompt.gameObject); prompt = null; }

        // World/StickerPeel の板ポリなら 3D カールで剥がしてから消す
        StartCoroutine(PeelAndDestroy());
    }

    /**=======================================================================
      剥がし演出 (_Peel 0→1) をしてから削除する
      デカール等 _Peel を持たないオブジェクトは即削除にフォールバック
    =======================================================================**/
    private IEnumerator PeelAndDestroy() {
        isPeeling = true;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        Material mat = (mr != null) ? mr.material : null;

        if (mat != null && mat.HasProperty("_Peel")) {
            StickerPeelFX.PrepareMaterial(mat);     // 上方向カール半径を設定
            float t = 0f;
            while (t < peelDuration) {
                t += Time.deltaTime;
                mat.SetFloat("_Peel", Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / peelDuration)));
                yield return null;
            }
            // 剥がれ切ったら剥がし方向へ飛びながらディゾルブで消える
            yield return StickerPeelFX.FlyOffAndDissolve(transform, mat, vanishDuration);
        }

        // mr.material で生成したインスタンスは GameObject 破棄では解放されないので明示破棄する
        if (mat != null) Destroy(mat);
        Destroy(gameObject);
    }

    /**=======================================================================
      ギズモ描画（デバッグ用）
    =======================================================================**/
    private void OnDrawGizmosSelected() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}
