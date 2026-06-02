using UnityEngine;

public class PassiveDummy : MonoBehaviour, IAgent
{
    [SerializeField] private float bodyRadius = 0.5f;
    public float BodyRadius => bodyRadius;
    public bool IsDead() => false;
    public void Elapse(float dt) { /* nic */ }

    public void ApplyHit(float damage, BotAgent attacker)
    {
        /* nic */
    }
}
