/**===========================================================================

 File   : 攻撃データ [AttackData.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 攻撃1回分のダメージ値と付随効果(DamageContext)を保持する ScriptableObject。
 ===========================================================================**/
using UnityEngine;

[CreateAssetMenu(fileName = "AttackData", menuName = "Combat/AttackData")]
public class AttackData : ScriptableObject {
    public int damage;
    public DamageContext effects;
}
