using UnityEngine;

/// <summary>
/// B1_Settings の各レンジを ParticleSystem で可視化する。
/// DebugManager から Setup() → Tick() を呼ぶだけで動作する。
/// </summary>
public class B1_DebugRangeRings : MonoBehaviour
{
    private (ParticleSystem ps, Transform tr)[] rings;

    public void Setup(B1_Settings s)
    {
        rings = new[]
        {
            Spawn(s.Scratch.AttackRange,  new Color(1f,   0.2f, 0.2f, 1f),  "Scratch"),
            Spawn(s.Walk.ActivateRange,   new Color(1f,   0.9f, 0.1f, 1f),  "Walk"),
            Spawn(s.Run.ActivateRange,    new Color(0.2f, 1f,   1f,   1f),  "Run"),
            Spawn(s.RushCharge.MinRange,  new Color(1f,   0.55f,0f,   1f),  "RushMin"),
            Spawn(s.RushCharge.MaxRange,  new Color(1f,   0.55f,0f,   0.5f),"RushMax"),
            Spawn(s.TailAttack.MinRange,  new Color(0.7f, 0.3f, 1f,   1f),  "TailMin"),
            Spawn(s.TailAttack.MaxRange,  new Color(0.7f, 0.3f, 1f,   0.5f),"TailMax"),
        };
    }

    public void Tick(Vector3 bossPos)
    {
        if (rings == null) return;
        var p = new Vector3(bossPos.x, 0.05f, bossPos.z);
        foreach (var (_, tr) in rings)
            tr.position = p;
    }

    private (ParticleSystem, Transform) Spawn(float radius, Color color, string label)
    {
        var go = new GameObject($"DebugRing_{label}");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop            = true;
        main.duration        = 1f;
        main.startLifetime   = 0.2f;
        main.startSpeed      = 0f;
        main.startSize       = 0.08f;
        main.startColor      = color;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;

        var emission = ps.emission;
        emission.rateOverTime = 60f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = radius;
        shape.arc       = 360f;
        shape.rotation  = new Vector3(-90f, 0f, 0f);   // 地面に平行

        ps.Play();
        return (ps, go.transform);
    }
}
