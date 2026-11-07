/**===========================================================================

 File   : 逃走ルーム範囲ギズモ [EscapeRoomZone.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 WhiteBunny/EscapeRoom に付ける。同じ GameObject の BoxCollider(isTrigger) が
 白ウサギ（Enemy02_RouteEscape）の徘徊範囲を定義する。
 このコンポーネントはその範囲を Scene ビューへ常時描画し、
 「逃走ルームの広さ」をゲームを再生せずに見ながら調整できるようにするだけの
 オーサリング補助（実行時の判定サイズは BoxCollider.size がそのまま担う）。

   ・広さを変える  ： BoxCollider の Size / Center を編集
   ・中心を動かす  ： この GameObject の Transform を動かす

===========================================================================**/

using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class EscapeRoomZone : MonoBehaviour {

    [Tooltip("Scene ビューに描く範囲の色")]
    [SerializeField] private Color zoneColor = new Color(0.35f, 0.8f, 1f, 1f);

    // インスペクタから追加した瞬間に、判定を通り抜ける trigger ＆ 既定サイズにする
    void Reset() {
        var bc = GetComponent<BoxCollider>();
        bc.isTrigger = true;
        if (bc.size == Vector3.one) bc.size = new Vector3(24f, 8f, 24f);
        gameObject.layer = 2;   // Ignore Raycast（接地/オクルージョン等のレイに拾われて誤爆しないように）
    }

    void OnDrawGizmos() {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.06f);
        Gizmos.DrawCube(bc.center, bc.size);
        Gizmos.color = zoneColor;
        Gizmos.DrawWireCube(bc.center, bc.size);
    }
}
