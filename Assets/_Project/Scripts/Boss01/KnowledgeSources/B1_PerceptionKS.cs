using UnityEngine;

/// <summary>
/// プレイヤーの位置・距離・空中判定をブラックボードに書き込む。
/// </summary>
public class B1_PerceptionKS : IKnowledgeSource
{
    private readonly Transform bossTransform;
    private readonly Transform playerTransform;

    public B1_PerceptionKS(Transform bossTransform, Transform playerTransform)
    {
        this.bossTransform   = bossTransform;
        this.playerTransform = playerTransform;
    }

    public void Evaluate(B1_Blackboard bb)
    {
        Vector3 toPlayer     = playerTransform.position - bossTransform.position;
        float   distance     = toPlayer.magnitude;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float   angle        = Vector3.SignedAngle(bossTransform.forward, toPlayerFlat, Vector3.up);

        // 空中判定は現在未使用。IsPlayerAirborne は常に false
        bb.SetPerception(distance, angle, false);
    }
}
