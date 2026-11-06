/*
 File   : スキルゲージ設定アセット [SkillGaugeSettings.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 スキルゲージ (SkillGauge) のチューニング値を ScriptableObject として資産化したもの。
 SkillGauge はランタイム装着され得るため、Resources/SkillGaugeSettings.asset を
 Awake で自動ロードする。アセットが無い場合はコード既定値で動作する。
*/
using UnityEngine;

[CreateAssetMenu(fileName = "SkillGaugeSettings", menuName = "Player/Skill Gauge Settings")]
public class SkillGaugeSettings : ScriptableObject {

    [Header("充填")]
    [Tooltip("時間経過だけで空→満タンになるまでの秒数")]
    [Min(0.1f)] public float FillSeconds = 30f;
}
