/**===========================================================================

 File   : シーン内 Directional Light のグローバル提供 [PostProcessDirectionalLightProvider.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 シリアライズした directionalLight を静的プロパティ Current に登録し、
 他スクリプト（ポストエフェクト等）から参照可能にする。
 併せて RenderSettings.sun も設定し、スカイドームシェーダーの
 GetMainLight() が正しい太陽方向を取得できるようにする。
 ===========================================================================**/
using UnityEngine;

public class PostProcessDirectionalLightProvider : MonoBehaviour
{
    // 他スクリプトから参照できるシーン共有の Directional Light
    public static Light Current { get; private set; }

    [SerializeField] private Light directionalLight;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        // Inspectorでの値変更を即座に反映する
        Apply();
    }

    private void OnDisable()
    {
        // このコンポーネントが無効になった際、自身が登録した Light だけをクリアする
        // 別コンポーネントが上書きしていた場合は触らない
        if (Current == directionalLight)
        {
            Current = null;
        }
    }

    // directionalLight が Directional タイプであることを確認してから登録する
    // Point / Spot Light を誤って指定した場合は無視する
    private void Apply()
    {
        if (directionalLight != null && directionalLight.type == LightType.Directional)
        {
            Current = directionalLight;
            RenderSettings.sun = directionalLight; // URP の GetMainLight() が参照する太陽光を設定
        }
    }
}
