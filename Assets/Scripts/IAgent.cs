using UnityEngine;

public interface IAgent
{
    Transform transform { get; }
    bool IsDead();
    float BodyRadius { get; }

    // attacker to AI-bot (moze byc null), zeby doliczyc dmg/frag w jego statystykach
    void ApplyHit(float damage, BotAgent attacker);
    void Elapse(float deltaTime);
}
