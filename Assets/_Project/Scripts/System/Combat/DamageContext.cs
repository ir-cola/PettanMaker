/**===========================================================================

 File   : ダメージコンテキスト [DamageContext.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 攻撃ヒット時の演出パラメータ(ヒットストップ時間・画面シェイクの長さと強さ)を
 まとめて受け渡すためのデータ構造体。
 ===========================================================================**/
[System.Serializable]
public struct DamageContext {
    public float targetHitstop;
    public float sourceHitstop;
    public float shakeDuration;
    public float shakeIntensity;

    [UnityEngine.Tooltip("全画面ヒットストップ(Time.timeScale停止)の長さ(秒)。0で無効。強攻撃の「ズドン」に使う。")]
    public float globalHitstop;

    [UnityEngine.Tooltip("全画面ヒットストップ中の timeScale。0で完全停止。少し残すと重い「タメ」になる。")]
    public float globalHitstopScale;

    [System.NonSerialized] private int playerDamageType;
    [System.NonSerialized] private bool hasPlayerDamageType;

    /// <summary>Player の被弾リアクション種別。0=ノックバックなし(動ける) / 1=ノックバック /
    /// 2=ノックバック+地面倒れ込み。未指定の攻撃は既定で 1 (ノックバック)。</summary>
    public int PlayerDamageType => hasPlayerDamageType ? playerDamageType : 1;

    public DamageContext WithPlayerDamageType(int damageType) {
        playerDamageType = UnityEngine.Mathf.Max(0, damageType);
        hasPlayerDamageType = true;
        return this;
    }
}
