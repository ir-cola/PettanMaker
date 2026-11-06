using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHitboxReceiver : MonoBehaviour
{
    private EnemyAttackHitbox hitbox;

    public void Init(EnemyAttackHitbox hitbox) => this.hitbox = hitbox;

    private void OnTriggerEnter(Collider other) => hitbox?.OnTrigger(other);
    private void OnTriggerStay(Collider other)  => hitbox?.OnTrigger(other);
}
