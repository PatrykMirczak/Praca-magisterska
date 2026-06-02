using UnityEngine;

public class RandomBot : MonoBehaviour, IAgent
{
    [Header("Ruch / Walka")]
    public float moveSpeed = 4f;
    public float rotateSpeed = 150f;
    public float maxHealth = 100f;
    public float fireCooldown = 0.5f;

    [Header("Sensory/sciany")]
    public float rayLength = 1f;
    public LayerMask wallMask = ~0;
    public float bodyRadius = 0.5f;
    public float skin = 0.02f;

    [Header("Sterowanie losowe")]
    public Vector2 forwardFramesRange = new Vector2(10, 30);
    public Vector2 turnFramesRange = new Vector2(5, 15);
    [Range(0f, 1f)] public float pPickForward = 0.25f;
    [Range(0f, 1f)] public float pPickLeft = 0.12f;
    [Range(0f, 1f)] public float pPickRight = 0.12f;
    [Range(0f, 1f)] public float pPickShoot = 0.10f;

    private ArenaManager arena;
    private float health;
    private float fireTimer;
    private float rotationDeg;
    private Vector2 position;
    private bool isDead;

    // licznik „ile klatek jeszcze robie X”
    private int framesForward;
    private int framesLeft;
    private int framesRight;
    private int framesShoot;

    // Wlasciwosci
    public float BodyRadius => bodyRadius;

    public void Init(ArenaManager arena, Vector3 startPos, float startRotZ)
    {
        this.arena = arena;
        position = startPos;
        rotationDeg = startRotZ;
        transform.position = startPos;
        transform.rotation = Quaternion.Euler(0, 0, startRotZ);

        health = maxHealth;
        fireTimer = 0f;
        isDead = false;

        // wyczysc makro-rozkazy
        framesForward = framesLeft = framesRight = framesShoot = 0;
        gameObject.SetActive(true);
    }

    public void Elapse(float deltaTime)
    {
        if (isDead) return;

        // 1) losowe wybory co jakis czas
        MaybePickNewIntents();

        // 2) cooldown
        fireTimer -= deltaTime; if (fireTimer < 0) fireTimer = 0;

        // 3) sterowanie z licznikow
        float forward = framesForward > 0 ? 1f : 0f;
        float turnL = framesLeft > 0 ? 1f : 0f;
        float turnR = framesRight > 0 ? 1f : 0f;
        bool wantShoot = framesShoot > 0;

        rotationDeg += (turnR - turnL) * rotateSpeed * deltaTime;
        if (rotationDeg >= 360f) rotationDeg -= 360f;
        else if (rotationDeg < 0f) rotationDeg += 360f;

        float rotRad = rotationDeg * Mathf.Deg2Rad;
        Vector2 fwd = new Vector2(Mathf.Cos(rotRad), Mathf.Sin(rotRad));

        // 4) proste odbijanie od scian (maly ray do przodu)
        bool wallAhead = Physics2D.Raycast(position + fwd * skin, fwd, rayLength, wallMask);
        if (wallAhead)
        {
            // odwroc kierunek na chwile
            framesForward = Matrix.MatrixRand.RangeInt((int)forwardFramesRange.x, (int)forwardFramesRange.y + 1);
            framesLeft = framesRight = 0;
            rotationDeg += 180f; // gwaltowny obrot
            if (rotationDeg >= 360f) rotationDeg -= 360f;
            fwd = -fwd;
        }

        // 5) ruch z prosta kolizja
        float step = Mathf.Clamp01(forward) * moveSpeed * deltaTime;
        Vector2 prev = position;
        Vector2 target = prev + fwd * step;

        Vector2 dir = target - prev;
        float dist = dir.magnitude;
        if (dist > 0f)
        {
            dir /= dist;
            var hit = Physics2D.CircleCast(prev, bodyRadius, dir, dist, wallMask);
            if (hit.collider != null)
            {
                float stop = Mathf.Max(0f, hit.distance - skin);
                position = prev + dir * stop;
            }
            else position = target;
        }

        transform.position = position;
        transform.rotation = Quaternion.Euler(0, 0, rotationDeg);

        // 6) strzal – prosta bramka anty-sciana (nie strzelaj, gdy tuz przed nosem jest sciana)
        if (wantShoot && !wallAhead && fireTimer <= 0f)
        {
            arena.RequestShot(this, position, fwd);
            fireTimer = fireCooldown;
        }


        // 7) zmniejsz liczniki
        if (framesForward > 0) framesForward--;
        if (framesLeft > 0) framesLeft--;
        if (framesRight > 0) framesRight--;
        if (framesShoot > 0) framesShoot--;
    }

    private void MaybePickNewIntents()
    {
        // "dobieramy" brakujace akcje z pewnym prawdopodobienstwem
        if (framesForward <= 0 && Matrix.MatrixRand.Chance(pPickForward))
            framesForward = Matrix.MatrixRand.RangeInt((int)forwardFramesRange.x, (int)forwardFramesRange.y + 1);

        if (framesLeft <= 0 && Matrix.MatrixRand.Chance(pPickLeft))
        {
            framesLeft = Matrix.MatrixRand.RangeInt((int)turnFramesRange.x, (int)turnFramesRange.y + 1);
            framesRight = 0;
        }
        if (framesRight <= 0 && Matrix.MatrixRand.Chance(pPickRight))
        {
            framesRight = Matrix.MatrixRand.RangeInt((int)turnFramesRange.x, (int)turnFramesRange.y + 1);
            framesLeft = 0;
        }

        if (framesShoot <= 0 && Matrix.MatrixRand.Chance(pPickShoot))
            framesShoot = Matrix.MatrixRand.RangeInt(5, 20); // krotka seria
    }

    // ============ IAgent ============
    public bool IsDead() => isDead;

    public void ApplyHit(float damage, BotAgent attacker)
    {
        if (isDead) return;
        health -= damage;

        // Jesli zginal – natychmiastowy respawn
        if (health <= 0f)
        {
            isDead = true;
            gameObject.SetActive(false);

            // instant respawn w losowym spawnie
            if (arena != null) arena.RespawnRandomImmediately(this);
        }
    }

    // Uzywane przez arene przy respawnie
    public void RespawnAt(Vector3 pos, float rotZ)
    {
        isDead = false;
        gameObject.SetActive(true);

        health = maxHealth;
        fireTimer = 0f;

        position = pos;
        rotationDeg = rotZ;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0, 0, rotationDeg);

        framesForward = framesLeft = framesRight = framesShoot = 0;
    }


}
