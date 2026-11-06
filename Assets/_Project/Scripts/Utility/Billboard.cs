/**===========================================================================

 File   : ビルボード [Billboard.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/3
 -----------------------------------------------------------------------------

 スプライト等のオブジェクトを常にカメラへ正対させる。
 CameraFacing : カメラ平面と平行（スクリーン整列。常に真正面で読める）
 YAxisOnly    : Y軸のみ回転（縦は保ったままカメラ方向を向く）

===========================================================================**/

using UnityEngine;

/**===========================================================================
  ビルボード
===========================================================================**/
[DisallowMultipleComponent]
public class Billboard : MonoBehaviour {

    public enum BillboardMode {
        CameraFacing,   // カメラ平面に平行（スクリーン整列）
        YAxisOnly       // Y軸回転のみ
    }

    [SerializeField] private BillboardMode mode = BillboardMode.CameraFacing;
    [Tooltip("未指定なら Camera.main を使用")]
    [SerializeField] private Camera targetCamera;

    private Transform camTf;

    private void OnEnable() {
        ResolveCamera();
    }

    private void LateUpdate() {
        if (camTf == null) {
            ResolveCamera();
            if (camTf == null) return;
        }

        if (mode == BillboardMode.CameraFacing) {
            // カメラと同じ向き＝スプライトを常に正面向きにする（文字が反転しない）
            transform.rotation = camTf.rotation;
        } else {
            Vector3 dir = transform.position - camTf.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void ResolveCamera() {
        Camera c = targetCamera != null ? targetCamera : Camera.main;
        camTf = c != null ? c.transform : null;
    }

    /**=======================================================================
      参照カメラを外部から差し替える
    =======================================================================**/
    public void SetCamera(Camera cam) {
        targetCamera = cam;
        ResolveCamera();
    }
}
