/**===========================================================================

 File   : スカイドーム追従 [SkydomeFollow.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 スカイドームの位置をカメラに追従させ、常に視点を中心に据える。
 ===========================================================================**/
using UnityEngine;

public class SkydomeFollow : MonoBehaviour
{
    public Transform targetCamera;

    void LateUpdate()
    {
        if (targetCamera == null) return;
        transform.position = targetCamera.position;
    }
}