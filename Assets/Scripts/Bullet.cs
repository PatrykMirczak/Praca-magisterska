using UnityEngine;

public class Bullet : MonoBehaviour
{
    private IAgent _ownerAgent;        // do ignorowania kolizji ze strzelajacym
    private BotAgent _ownerForCredit;  // tylko AI – liczenie dmg/kills

    private Vector2 _pos;
    private Vector2 _vel;
    private float _life;
    private float _radius;
    private float _damage;
    private LayerMask _wallMask;

    public bool IsActive { get; private set; }


    public void Activate(IAgent ownerAgent, BotAgent ownerForCredit, Vector2 startPos, Vector2 velocity,
                     float lifeTime, float radius, float damage, LayerMask wallMask)
    {
        _ownerAgent = ownerAgent;
        _ownerForCredit = ownerForCredit;

        _pos = startPos;
        _vel = velocity;
        _life = lifeTime;
        _radius = radius;
        _damage = damage;
        _wallMask = wallMask;

        transform.position = startPos;

        IsActive = true;
        gameObject.SetActive(true);
    }

    // wywolywane przy oddawaniu do puli
    public void Deactivate()
    {
        IsActive = false;
        _ownerAgent = null;
        _ownerForCredit = null;
        gameObject.SetActive(false);
    }

    // Zwraca true, gdy pocisk powinien byc "zgaszony" (trafienie / uplynelo zycie)
    public bool Elapse(float dt, ArenaManager arena)
    {
        _life -= dt;
        if (_life <= 0f) return true;

        Vector2 prevPos = _pos;
        _pos += _vel * dt;
        transform.position = _pos;

        // 1) Kolizja ze sciana – prosty CircleCast po segmencie ruchu
        Vector2 dir = (_pos - prevPos);
        float dist = dir.magnitude;
        if (dist > 0f)
        {
            dir /= dist;
            // cast "kkokiem" – minimalizuje przebicia cienkich scian
            var hit = Physics2D.CircleCast(prevPos, _radius, dir, dist, _wallMask);
            if (hit.collider != null)
            {
                return true; // zderzenie ze sciana -> usun pocisk
            }
        }

        // 2) Kolizja z botami (AI + Random)) - bez fizyki
        var agents = arena.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null || a.IsDead()) continue;

            // POMIN strzelajacego (dziala rowniez dla RandomBotow, bo jest IAgent)
            if (_ownerAgent != null && ReferenceEquals(a, _ownerAgent)) continue;

            float hitRange = _radius + a.BodyRadius;
            Vector2 toAgent = (Vector2)a.transform.position - _pos;
            if (toAgent.sqrMagnitude <= hitRange * hitRange)
            {
                // kredyt tylko dla AI (BotAgent) – dla randomow _ownerForCredit = null
                a.ApplyHit(_damage, _ownerForCredit);
                return true;
            }
        }

        return false; // leci dalej
    }

}
