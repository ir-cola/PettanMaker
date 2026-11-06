/**===========================================================================

 File   : プレイヤーHP表示クラス [HPDisplay.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 2026/7/6 手動配置ハートを廃止し、スクリプトによる自動配置に変更
 2026/7/6 反り上がり配置を廃止し、重なりを保ったまま等間隔の横一列に整列

 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/**===========================================================================
  プレイヤーHP表示クラス
===========================================================================**/
public class HPDisplay : MonoBehaviour {
    [Header("ハート設定")]
    [Tooltip("ハートのスプライト")]
    [SerializeField] private Sprite heartSprite;
    [Tooltip("ハート1個のサイズ")]
    [SerializeField] private Vector2 heartSize = new Vector2(93f, 76f);
    [Tooltip("ハートの傾き（Z回転・度）")]
    [SerializeField] private float heartAngle = -15f;

    [Header("自動配置設定（このオブジェクトの左下基準）")]
    [Tooltip("1個目のハートの中心位置")]
    [SerializeField] private Vector2 startOffset = new Vector2(19.9f, 24.8f);
    [Tooltip("隣のハートとの中心間隔（ハート幅より小さいぶんだけ重なる）")]
    [SerializeField] private float spacing = 65.3f;
    [Tooltip("1個ごとの上下オフセット（プラスで右肩上がり）")]
    [SerializeField] private float stepY = 5f;

    [Header("HP設定")]
    public int maxHP = 5;

    private readonly List<Image> hearts = new List<Image>();
    private PlayerHealth playerHealth;

    void Start() {
        playerHealth = FindObjectOfType<PlayerHealth>();
        if (playerHealth != null) {
            // PlayerHealth が持つ実際の最大値で初期化（Inspector の maxHP は無視される）
            maxHP = playerHealth.MaxHP;
            CreateHearts();
            playerHealth.onHPChanged.AddListener(UpdateHP);
            UpdateHP(playerHealth.CurrentHP);
        }
        else {
            Debug.LogWarning("PlayerHealth が見つかりません");
            CreateHearts();
            UpdateHP(maxHP);
        }
    }

    void OnDestroy() {
        // PlayerHealth はプレイヤーごと持続し HUD より長生きし得るので購読を解除する
        // (解除しないと破棄済みの本表示へ UpdateHP が飛んで MissingReferenceException)
        if (playerHealth != null) playerHealth.onHPChanged.RemoveListener(UpdateHP);
    }

    // maxHP 個のハートを自動配置する（既存分は作り直し）
    void CreateHearts() {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        hearts.Clear();

        for (int i = 0; i < maxHP; i++) {
            GameObject obj = new GameObject($"Heart_{i + 1}");
            obj.transform.SetParent(transform, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.sizeDelta = heartSize;
            rect.localEulerAngles = new Vector3(0f, 0f, heartAngle);
            rect.anchoredPosition = HeartPosition(i);

            Image image = obj.AddComponent<Image>();
            image.sprite = heartSprite;
            image.raycastTarget = false;

            hearts.Add(image);
        }
    }

    // i 番目のハートの中心位置（重なりを保ったまま等間隔で右肩上がりに並べる）
    Vector2 HeartPosition(int i) {
        return startOffset + new Vector2(spacing * i, stepY * i);
    }

    public void UpdateHP(int currentHP) {
        // 最大HPが増えていたらアイコンを作り直す（トランプシール対応）
        if (playerHealth != null && playerHealth.MaxHP != maxHP) {
            maxHP = playerHealth.MaxHP;
            CreateHearts();
        }

        for (int i = 0; i < hearts.Count; i++) {
            hearts[i].enabled = (i < currentHP);
        }
    }
}
