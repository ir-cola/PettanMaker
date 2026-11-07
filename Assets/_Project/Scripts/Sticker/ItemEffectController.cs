/**===========================================================================

 File   : アイテム効果制御クラス [ItemEffectController.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/12
 -----------------------------------------------------------------------------

 アイテムシールがプレイヤーに貼り付けられた瞬間に効果を発動する。
 PlayerPasteManager.onStickerPasted を購読して動作する。
   ・Item_Tramp  : HP上限 +1（永続・重複加算なし）
   ・Item_Hat    : 一定時間 移動速度アップ（時限・再貼付で時間上書き）
   ・Item_Rabbit : 一定時間 コンボのスタンプ数 +2（時限・再貼付で時間上書き）

===========================================================================**/

using System.Collections;
using UnityEngine;

public class ItemEffectController : MonoBehaviour {

    [Header("参照")]
    [SerializeField] private PlayerLifetimeScope player;
    [SerializeField] private PlayerHealth        playerHealth;
    [SerializeField] private PlayerAttack        attackLogic; // field名は prefab YAML key と揃えるためそのまま
    [SerializeField] private PlayerPasteManager  pasteManager;

    [Header("対象シール（Dataフォルダのアセットをアサイン）")]
    [SerializeField] private StickerData trumpSticker;   // HP上限+1
    [SerializeField] private StickerData hatSticker;     // 移動速度アップ
    [SerializeField] private StickerData rabbitSticker;  // コンボスタンプ+2

    [Header("トランプ：HP上限+1")]
    [SerializeField] private int hpUpAmount = 1;

    [Header("ハット：移動速度アップ")]
    [SerializeField] private float speedMultiplier = 1.6f;
    [SerializeField] private float speedDuration   = 8.0f;

    [Header("ラビット：コンボスタンプ +2")]
    [SerializeField] private int   comboStampBonus    = 2;
    [SerializeField] private float comboStampDuration = 10.0f;

    [Header("消費設定")]
    [Tooltip("貼り付け成功時にインベントリから1枚消費するか")]
    [SerializeField] private bool consumeOnPaste = true;

    // 効果中の状態
    private bool      isTrumpApplied = false;       // トランプは1回のみ加算
    private Coroutine speedCoroutine = null;        // ハット効果コルーチン（非null=効果中）
    private float     speedOriginalMultiplier;      // ハット発動前の倍率（復元用）
    private Coroutine comboCoroutine = null;        // ラビット効果コルーチン（非null=効果中）

    /**=======================================================================
      自動参照解決
    =======================================================================**/
    private void Awake() {
        if (player       == null) player       = FindObjectOfType<PlayerLifetimeScope>();
        if (playerHealth == null) playerHealth = FindObjectOfType<PlayerHealth>();
        if (attackLogic  == null && player != null) attackLogic = player.Attack;
        if (pasteManager == null) pasteManager = FindObjectOfType<PlayerPasteManager>();
    }

    /**=======================================================================
      貼付イベントを購読
    =======================================================================**/
    private void OnEnable() {
        if (pasteManager != null) {
            pasteManager.onStickerPasted.AddListener(HandleStickerPasted);
        }
        else {
            Debug.LogWarning("[ItemEffect] PlayerPasteManager が見つかりません。アイテム効果は発動しません。");
        }
    }

    private void OnDisable() {
        if (pasteManager != null) {
            pasteManager.onStickerPasted.RemoveListener(HandleStickerPasted);
        }
        // 無効化中にバフが永続化しないよう、走行中の効果は明示的に巻き戻す
        if (speedCoroutine != null) {
            StopCoroutine(speedCoroutine);
            if (player != null) player.SetSpeedMultiplier(speedOriginalMultiplier);
            speedCoroutine = null;
        }
        if (comboCoroutine != null) {
            StopCoroutine(comboCoroutine);
            if (attackLogic != null) attackLogic.AddBonusAttackCount(-comboStampBonus);
            comboCoroutine = null;
        }
    }

    /**=======================================================================
      シールが貼り付けられたときの処理（PlayerPasteManager のイベントから呼ばれる）
    =======================================================================**/
    private void HandleStickerPasted(StickerData data) {
        UseSticker(data);
    }

    /**=======================================================================
      シールを使用する（貼付経由・ショートカット経由 共通エントリ）
      Q キーなどのホットバーショートカットからもここを呼ぶ
    =======================================================================**/
    public void UseSticker(StickerData data) {
        if (data == null) return;
        if (data.stickerType != StickerType.Item) return; // アイテム以外は対象外

        if (!ApplyEffect(data)) {
            Debug.Log($"[ItemEffect] {data.stickerName} は効果未定義です。");
            return;
        }

        if (consumeOnPaste) {
            StickerInventory.Instance?.RemoveSticker(data);
        }
    }

    /**=======================================================================
      シール種別に対応する効果を適用する
    =======================================================================**/
    private bool ApplyEffect(StickerData data) {
        if (data == trumpSticker)  { ApplyTrump();  return true; }
        if (data == hatSticker)    { ApplyHat();    return true; }
        if (data == rabbitSticker) { ApplyRabbit(); return true; }
        return false;
    }

    // ---- トランプ：HP上限+1（永続・重複加算なし） ------------------------
    private void ApplyTrump() {
        if (playerHealth == null) {
            Debug.LogWarning("[ItemEffect] PlayerHealth が未設定です。");
            return;
        }
        if (isTrumpApplied) {
            Debug.Log("[ItemEffect] トランプは適用済みのため重複加算しません。");
            return;
        }
        playerHealth.IncreaseMaxHP(hpUpAmount);
        isTrumpApplied = true;
        Debug.Log($"[ItemEffect] トランプ：HP上限 +{hpUpAmount}（現:{playerHealth.MaxHP}）");
    }

    // ---- ハット：移動速度アップ（時限・再貼付で時間上書き） ----------------
    private void ApplyHat() {
        if (player == null) {
            Debug.LogWarning("[ItemEffect] Player が未設定です。");
            return;
        }
        if (speedCoroutine != null) {
            // 効果中：タイマーだけリセット、倍率はそのまま
            StopCoroutine(speedCoroutine);
            Debug.Log($"[ItemEffect] ハット効果時間を上書き（{speedDuration}s）");
        }
        else {
            // 新規発動：元の倍率を保存してからバフを掛ける
            speedOriginalMultiplier = player.SpeedMultiplier;
            player.SetSpeedMultiplier(speedOriginalMultiplier * speedMultiplier);
            Debug.Log($"[ItemEffect] ハット：速度x{speedMultiplier}（{speedDuration}s）");
        }
        speedCoroutine = StartCoroutine(SpeedUpRoutine());
    }

    private IEnumerator SpeedUpRoutine() {
        yield return new WaitForSeconds(speedDuration);
        if (player != null) player.SetSpeedMultiplier(speedOriginalMultiplier);
        speedCoroutine = null;
        Debug.Log("[ItemEffect] ハット効果終了");
    }

    // ---- ラビット：コンボスタンプ+2（時限・再貼付で時間上書き） ------------
    private void ApplyRabbit() {
        if (attackLogic == null) {
            Debug.LogWarning("[ItemEffect] AttackLogic が未設定です。");
            return;
        }
        if (comboCoroutine != null) {
            // 効果中：タイマーだけリセット、ボーナスはそのまま
            StopCoroutine(comboCoroutine);
            Debug.Log($"[ItemEffect] ラビット効果時間を上書き（{comboStampDuration}s）");
        }
        else {
            attackLogic.AddBonusAttackCount(comboStampBonus);
            Debug.Log($"[ItemEffect] ラビット：スタンプ+{comboStampBonus}（{comboStampDuration}s）");
        }
        comboCoroutine = StartCoroutine(ComboStampRoutine());
    }

    private IEnumerator ComboStampRoutine() {
        yield return new WaitForSeconds(comboStampDuration);
        if (attackLogic != null) attackLogic.AddBonusAttackCount(-comboStampBonus);
        comboCoroutine = null;
        Debug.Log("[ItemEffect] ラビット効果終了");
    }
}
