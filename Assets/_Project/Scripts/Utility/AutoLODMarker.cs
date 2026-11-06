/**===========================================================================

 File   : 自動LODマーカー [AutoLODMarker.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 AutoLODTool (エディタメニュー) が自動付与した LODGroup の目印。
 「Auto LOD を解除」で、このマーカーが付いた LODGroup だけを安全に削除する。
 手動で設定した LODGroup には付かない。

===========================================================================**/

using UnityEngine;

/** 自動LODマーカー (AutoLODTool 専用・ロジックなし) */
[DisallowMultipleComponent]
public class AutoLODMarker : MonoBehaviour { }
