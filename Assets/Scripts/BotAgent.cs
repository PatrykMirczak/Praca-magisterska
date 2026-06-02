using System;
using System.Collections.Generic;
using UnityEngine;

//DEBUG
#if UNITY_EDITOR
using UnityEditor;
#endif

public class BotAgent : MonoBehaviour, IAgent
{
    [Header("Fitness - wagi")]
    public float wAlive = 1.0f;
    public float wDist = 0.2f;
    public float wDmg = 2.0f;
    public float wKill = 100.0f;
    public float wAcc = 30.0f;   // nagroda za trafienia (accuracy)
    public float wShots = 0.05f;   // mala kara za kazdy strzal (anty-spam)
    public float wBump = 2.0f;    // kara za pchanie w sciane

    [Header("Anti-orbit (IIR)")]
    [Tooltip("Szybkosc filtra EMA; przy dt=0.02f, 0.02 = pamiec ok. 1 s")]
    public float spinAlpha = 0.02f;
    [Tooltip("Prog wykrycia dlugotrwalego skretu (po IIR)")]
    public float spinThreshold = 0.4f;
    [Tooltip("Waga kary za dlugotrwaly skret bez celu")]
    public float wSpinIIR = 1.0f;
    [Tooltip("Gating: brak celu z przodu, ponizej tego progu (0..1)")]
    public float noTargetGate = 0.2f;
    [Tooltip("Gating: 'czysty przod' (daleko do sciany) powyzej tego progu (0..1)")]
    public float wallClearGate = 0.3f;


    [Header("Bot - ustawienia ruchu")]
    public float moveSpeed = 5f;     // predkosc poruszania
    public float rotateSpeed = 180f; // stopnie/sek

    [Header("Walka")]
    public float maxHealth = 100f;
    public float fireCooldown = 0.25f; // min odstep miedzy strzalami (s)

    [Header("Sensory: sciany (raycast)")]
    [Tooltip("Liczba promieni (np. 8, 12, 16)")]
    public int rayCount = 8;
    [Tooltip("Zasieg promieni w jednostkach")]
    public float rayLength = 10f;
    [Tooltip("Warstwy traktowane jako sciany")]
    public LayerMask wallMask = ~0; // domyslnie wszystko

    [Header("Sensory: wrogowie (sektory)")]
    [Tooltip("Liczba sektorow (np. 8)")]
    public int enemySectors = 8;
    [Tooltip("Zasieg wykrywania wrogow")]
    public float enemyViewRadius = 12f;

    [Header("Kolizje (bez fizyki)")]
    public float bodyRadius = 0.5f;  // przyblizony promien bota (w jednostkach swiata)
    public float skin = 0.02f;        // maly margines, by nie "skleic sie" ze sciana


    //SZCHOWNICA
    [Header("LOS — fazowanie (szachownica)")]
    [Tooltip("Co ile krokow odswiezac LOS dla pojedynczego bota. 1 = co klatke (bez fazowania).")]
    public int losEveryNSteps = 3;
    [Tooltip("Ile sekund dane LOS uznajemy za 'swieze' (strzelanie i decyzje).")]
    public float losFreshTTL = 0.12f;

    // Fazowanie i swiezosc LOS
    private int _losPhase;          // 0..losEveryNSteps-1, nadawany przy Init
    private int _step;              // licznik krokow bota
    private float _timeSinceLos;    // sekundy od ostatniego pelnego przeliczenia LOS

    private bool ShouldUpdateLOS() =>
        (losEveryNSteps <= 1) || (((_step + _losPhase) % losEveryNSteps) == 0);

    private bool HasFreshLOS => _timeSinceLos <= losFreshTTL;





    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;


    //Genom
    [HideInInspector] public Genome genome;


    //stan bota
    private NeuralNetwork brain;
    private Vector2 position;       // pozycja bota
    private float rotationDeg;      // obrot w stopniach (0..360)
    private bool isDead = false;

    // referencja do areny, w ktorej jest bot
    // na arenie znajduja sie referencje do przeciwnikow i strzalow
    private ArenaManager arena;

    // Walka / statystyki do fitnessu
    private float health;
    private float fireTimer;
    private float timeAlive;
    private float distanceTravelled;
    private float damageDealt;
    private int kills;


    // Bufory optymalizacyjne
    private RaycastHit2D[] _rayHits;     // 1-elementowy bufor na wynik raycastu
    private Vector2[] _baseRayDirs;      // bazowe kierunki promieni dla katow 0 stopni, 360/n, ...
    private float[] _enemySectorsBuf;    // tymczasowe wartosci sektorow (0..1)
    private float[] _inputs;             // gotowy bufor wejsc do sieci NN

    // Stale pomocnicze przeliczane raz (dla sektorow)
    private float _sectorAngle;          // szerokosc sektora w stopniach
    private float _invSectorAngle;       // 1 / _sectorAngle
    private float _enemyViewRadiusSqr;   // promien^2 (porownania bez pierwiastka)




    //DO SYSTEMU KAR I NAGROD
    // Statystyki pomocnicze (do fitnessu)
    private int shotsFired;
    private int shotsHit;
    private float wallBumpTime;

    // Pomoc: do wykrywania "stuck" i "same output"
    private float _maxEnemySignal;     // max wartosc w sektorach (0..1) – "czy widze cel"

    //Liczniki dla IIR
    private float spinEMA;       // wygladzony turnIntent
    private float spinIIRTime;   // akumulacja czasu wykrytego orbitowania

    // Wlasciwosci
    public float BodyRadius => bodyRadius;



    // Liczenie przyjetych obrazen i publiczne gettery ---
    private float damageTaken;   // suma przyjetych obrazen w tej generacji

    public float TimeAlive => timeAlive;
    public int ShotsFired => shotsFired;
    public int ShotsHit => shotsHit;
    public float DamageTaken => damageTaken;
    public float DamageDealt => damageDealt;  // opcjonalnie, moze sie przydac
    public int Kills => kills;        // opcjonalnie


    public void Init(Genome genome, ArenaManager arena)
    {
        this.genome = genome;
        this.brain = genome.network;
        this.arena = arena;

        //WALIDACJA parametrow
        rayCount = Mathf.Max(1, rayCount);
        enemySectors = Mathf.Max(1, enemySectors);
        enemyViewRadius = Mathf.Max(0.0001f, enemyViewRadius);
        rayLength = Mathf.Max(0.0001f, rayLength);
        bodyRadius = Mathf.Max(0f, bodyRadius);
        skin = Mathf.Max(0f, skin);

        // Ustaw pozycje/rotacje startowa na podstawie transformu
        position = transform.position;
        rotationDeg = transform.eulerAngles.z;

        // Reset stanu
        isDead = false;
        health = maxHealth;
        fireTimer = 0f;
        timeAlive = 0f;
        distanceTravelled = 0f;
        damageDealt = 0f;
        kills = 0;

        // Statystyki pomocnicze reset
        shotsFired = 0;
        shotsHit = 0;
        wallBumpTime = 0f;

        _maxEnemySignal = 0f;


        // Raycast: 1 wynik wystarczy - szukanie najblizszego trafienia
        _rayHits = new RaycastHit2D[1];

        // Bazowe kierunki promieni: wyliczamy wektory jednostkowe dla katow 0 stopni, 360/rayCount, ...
        _baseRayDirs = new Vector2[rayCount];
        {
            float step = 360f / rayCount;
            for (int i = 0; i < rayCount; i++)
            {
                float aRad = Mathf.Deg2Rad * (step * i);
                _baseRayDirs[i] = new Vector2(Mathf.Cos(aRad), Mathf.Sin(aRad)); // kierunek dla "lokalnego" 0 stopni
            }
        }

#if UNITY_EDITOR
        AssertFrontRayIsForward();
#endif

        // Bufor sektorow i stale katowe
        _enemySectorsBuf = new float[enemySectors];
        _sectorAngle = 360f / enemySectors;
        _invSectorAngle = 1f / _sectorAngle;
        _enemyViewRadiusSqr = enemyViewRadius * enemyViewRadius;

        // Wejscia do NN: [promienie] + [sektory] + [HP] + [CD]
        int extraInputs = 2; // HP, cooldown
        _inputs = new float[rayCount + enemySectors + extraInputs];

        // Sanity-check: warstwa wejsciowa sieci
        int expectedInputs = _inputs.Length;
        if (brain != null && (brain.layers == null || brain.layers.Length == 0 || brain.layers[0] != expectedInputs))
        {
            Debug.LogWarning(
                $"[BotAgent] Rozmiar wejsc NN ({brain.layers?[0]}) != {expectedInputs}. " +
                $"Dopasuj layerSizes w GA do: {expectedInputs} wejsc.");
        }


        spinEMA = 0f;
        spinIIRTime = 0f;
        damageTaken = 0f;

        //SZACHOWNICA
        // Fazowanie LOS – deterministycznie z master RNG (MatrixRand) lub ustaw rEcznie
        _losPhase = Matrix.MatrixRand.RangeInt(0, Mathf.Max(1, losEveryNSteps));
        _step = 0;
        _timeSinceLos = float.PositiveInfinity; // do pierwszego peLnego update'u LOS nie strzelamy





        // Upewnienie sie, ze bot jest widoczny/aktywny
        gameObject.SetActive(true);
    }

    //Glowna "klatka" bota – wywolywana z GameManagera w stalym kroku czasu (np. 0.02).
    public void Elapse(float deltaTime)
    {
        if (isDead || brain == null) return;

        fireTimer -= deltaTime;
        if (fireTimer < 0f) fireTimer = 0f;

        // [LOS] ile czasu minelo od ostatniego pelnego przeliczenia LOS
        _timeSinceLos += deltaTime;

        // 1) Zbieranie wejsc z sensorow -> wejscia (ustawia tez _maxEnemySignal)
        FillInputs();

        // 2) NN decyduje
        float[] outputs = brain.FeedForward(_inputs);
        // Znaczenie wyjsc:
        // outputs[0] = ruch do przodu (0..1)
        // outputs[1] = obrot w lewo  (0..1)
        // outputs[2] = obrot w prawo (0..1)
        // outputs[3] = strzal       (0..1)
        float forward = outputs.Length > 0 ? outputs[0] : 0f;
        float turnLeft = outputs.Length > 1 ? outputs[1] : 0f;
        float turnRight = outputs.Length > 2 ? outputs[2] : 0f;
        float shoot = outputs.Length > 3 ? outputs[3] : 0f;

        // Promien z przodu: 0 = sciana tuz przed nosem, 1 = pusto
        float frontRay = (rayCount > 0 && _inputs != null && _inputs.Length > 0) ? _inputs[0] : 1f; // 0 blisko sciany, 1 daleko
        // Gating gazu: dlaw tylko gdy nos prawie na scianie
        forward *= Mathf.Clamp01((frontRay - 0.15f) / 0.85f);

        // Dead-zone / tlumik skretu, gdy nie ma celu i maly gaz
        const float noTarget = 0.15f;
        if (_maxEnemySignal < noTarget && forward < 0.2f)
        {
            // male wartosci skretu wycinamy, duze delikatnie tniemy (bez wymuszania jazdy)
            float tL = Mathf.Abs(turnLeft) < 0.2f ? 0f : turnLeft * 0.7f;
            float tR = Mathf.Abs(turnRight) < 0.2f ? 0f : turnRight * 0.7f;
            turnLeft = tL; turnRight = tR;
        }

        //policz turnIntent po ostatecznej korekcie sygnalow
        float turnIntent = Mathf.Abs(turnRight - turnLeft);


        // 3) Ruch: obrot i przesuniecie (bez fizyki)
        rotationDeg += (turnRight - turnLeft) * rotateSpeed * deltaTime;
        // Wyznaczanie kierunku na podstawie obrotu
        if (rotationDeg >= 360f) rotationDeg -= 360f;
        else if (rotationDeg < 0f) rotationDeg += 360f;

        // kierunek ruchu po obrocie
        float rotRad = rotationDeg * Mathf.Deg2Rad;
        Vector2 fwd = new Vector2(Mathf.Cos(rotRad), Mathf.Sin(rotRad));

        // ile chcemy sie przesunac w tej klatce
        float step = Mathf.Clamp01(forward) * moveSpeed * deltaTime;
        Vector2 prevPos = position;
        Vector2 targetPos = prevPos + fwd * step;

        // 4) Prosta kolizja ze sciana: CircleCast od prevPos do targetPos
        Vector2 moveDir = targetPos - prevPos;
        float moveDist = moveDir.magnitude;

        if (moveDist > 0f)
        {
            moveDir /= moveDist; // normalizacja
            // CircleCast: „kolko” o promieniu bodyRadius przesuwane o moveDist
            RaycastHit2D hit = Physics2D.CircleCast(prevPos, bodyRadius, moveDir, moveDist, wallMask);
            if (hit.collider != null)
            {
                // zatrzymaj sie tuz przed sciana (maly epsilon, zeby nie „przyklejac”)
                //float stopDist = Mathf.Max(0f, hit.distance - 0.001f);
                float stopDist = Mathf.Max(0f, hit.distance - skin); //uzycie skin
                position = prevPos + moveDir * stopDist;
            }
            else
            {
                position = targetPos; // brak kolizji
            }
        }

        // Ile faktycznie ruszyl sie w tej klatce (do kar)
        float moved = Vector2.Distance(prevPos, position);

        // statystyka do fitnessu: przebyty dystans
        distanceTravelled += moved;

        // 5) Aktualizacja transformu (tylko wizualizacja)
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);

        // ====== NOWE: 6) Strzal z "bramka" ======
        // policz raz i uzywaj tez w karach ponizej
        //float frontRay = (rayCount > 0 && _inputs != null && _inputs.Length > 0) ? _inputs[0] : 1f;
        float frontEnemy = (enemySectors > 0 && _enemySectorsBuf != null && _enemySectorsBuf.Length > 0) ? _enemySectorsBuf[0] : 0f;

        // bool canShoot =
        //(_maxEnemySignal > 0.2f) &&   // jest ktos w ogole w sektorach
        //(frontEnemy > 0.2f) &&    // ktos jest z PRZODU
        //(frontRay > 0.25f);     // i nie stoi przy samej scianie

        //SZACHOWNICA
        //bool canShoot =
        //       HasFreshLOS &&                // dane LOS swieze
        //       (_maxEnemySignal > 0.2f) &&   // jest ktos w ogole
        //       (frontEnemy > 0.2f) &&        // ktos z przodu
        //       (frontRay > 0.25f);           // nie przyklejony do sciany

        bool steadyAim = (turnIntent < 0.22f) || (frontEnemy > 0.55f); // pozwol strzelic mimo skretu, jesli cel jest bardzo „mocny”
        bool canShoot =
            HasFreshLOS &&             // dane LOS sa swieze
            (_maxEnemySignal > 0.30f) &&  // w ogole "jest ktos" w sektorach
            (frontEnemy > 0.30f) &&       // ktos z przodu (silniejszy prog)
            (frontRay > 0.35f) &&         // nie jestesmy przy scianie
            steadyAim;                    // nie strzelamy w trakcie gwaltownego skretu




        if (shoot > 0.5f && canShoot && fireTimer <= 0f)
        {
            arena.RequestShot(this, position, fwd);
            fireTimer = fireCooldown;
            shotsFired++;
        }

        // 7) KARY behawioralne (zostaje tylko bump + nowy IIR anti-orbit)

        // — parametry pomocnicze dla bump
        const float movedFrac = 0.05f;
        const float forwardIntent = 0.2f;
        const float wallNear = 0.2f;

        float theoreticalStep = Mathf.Clamp01(forward) * moveSpeed * deltaTime;
        float movedThresh = movedFrac * theoreticalStep + 1e-5f;

        // a) "Szorowanie" o sciane: chce jechac, prawie nie jade, a z przodu sciana
        if (forward > forwardIntent && moved < movedThresh && frontRay < wallNear)
            wallBumpTime += deltaTime;

        // b) Anti-orbit IIR — wykrywamy dlugotrwaly, mocny skret bez celu i bez sciany z przodu
        // turnIntent policzony wczesniej: |turnRight - turnLeft| (0..1)
        spinEMA = Mathf.Lerp(spinEMA, turnIntent, spinAlpha);  // EMA: alpha*turnIntent + (1-alpha)*spinEMA

        bool noTargetFront = frontEnemy < noTargetGate; // brak celu "z przodu"
        bool wallIsClear = frontRay > wallClearGate; // nie kleje sie do sciany
        if (spinEMA > spinThreshold && noTargetFront && wallIsClear)
            spinIIRTime += deltaTime;



        // 8) Czas zycia do fitnessu
        timeAlive += deltaTime;



        //SZACHOWNICA
        _step++;
    }


    // Wypelnia tablice _inputs: najpierw ray’e do scian (znormalizowane 0..1),
    // potem sektory wrogow (0..1 – im blizej i z LOS, tym wieksza wartosc).
    // LOS (line-of-sight) - czy bot jest za sciana, czy nie.
    // Dodaje rowniez nowe wejscia do sieci NN, patrz koncowke metody.
    private void FillInputs()
    {
        // --- 1) Promienie do scian ---
        // Rotacja lokalnych kierunkow bazowych o aktualny kat bota (szybko: 2 mnozenia na promien)
        float rotRad = rotationDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rotRad);
        float sin = Mathf.Sin(rotRad);

        for (int i = 0; i < rayCount; i++)
        {
            // Obroc bazowy kierunek
            Vector2 baseDir = _baseRayDirs[i];
            Vector2 dir;
            dir.x = baseDir.x * cos - baseDir.y * sin;
            dir.y = baseDir.x * sin + baseDir.y * cos;

            // Raycast bezalokacyjny
            int hitCount = Physics2D.RaycastNonAlloc(position, dir, _rayHits, rayLength, wallMask);
            if (hitCount > 0)
            {
                float dist = _rayHits[0].distance;
                // Normalizacja: 0 (tuz przy scianie) -> 1 (nic w zasiegu)
                _inputs[i] = Mathf.Clamp01(dist / rayLength);
            }
            else
            {
                _inputs[i] = 1f; // brak przeszkody w zasiegu
            }
        }

        // --- 2) Wrogowie w sektorach (fazowane) ---
        bool updateLos = ShouldUpdateLOS();
        if (updateLos)
        {
            // Wyzeruj bufory sektorów tylko gdy robimy update
            for (int s = 0; s < enemySectors; s++)
                _enemySectorsBuf[s] = 0f;

            // Pobierz referencje do listy przeciwników z areny
            IReadOnlyList<IAgent> enemies = arena.GetAgents();  //wszyscy trafialni


            // Kat wlasny do kierunku swiata (zeby liczyc azymut w ukladzie bota)
            for (int e = 0; e < enemies.Count; e++)
            {
                var enemy = enemies[e];
                if (enemy == null || enemy.IsDead()) continue;
                if (ReferenceEquals(enemy, this)) continue;   // nie bierz siebie

                Vector2 toEnemy = (Vector2)enemy.transform.position - position;

                // epsilon, by uniknac dzielenia przez zero
                float sqrDist = toEnemy.sqrMagnitude;
                if (sqrDist < 1e-6f || sqrDist > _enemyViewRadiusSqr) continue; // poza zasiegiem

                float dist = Mathf.Sqrt(sqrDist); // tylko jesli w zasiegu
                Vector2 dir = toEnemy / dist; // normalizacja wektora do wroga

                // --- NOWE: Line-Of-Sight do wroga po warstwie scian ---
                // LOS z minimalnym offsetem od zrodla
                Vector2 origin = position + dir * skin;
                int losHits = Physics2D.RaycastNonAlloc(origin, dir, _rayHits, Mathf.Max(0f, dist - skin), wallMask);
                if (losHits > 0)
                {
                    // cos zaslania (sciana miedzy nami a wrogiem) -> ignoruj tego wroga
                    continue;
                }


                // kat do wroga w stopniach
                float angleToEnemy = Mathf.Atan2(toEnemy.y, toEnemy.x) * Mathf.Rad2Deg;

                // przelicz na kat w lokalnym ukladzie bota (0..360)
                float rel = angleToEnemy - rotationDeg;
                if (rel < 0f) rel += 360f; else if (rel >= 360f) rel -= 360f;


                //NOWE: sektor poczatkowy na srodku przodu bota (a nie na prawo jak wczesniej)
                // Przesun o pol sektora, aby sektor 0 byl centrowany na 0° (kierunek do przodu)
                float half = 0.5f * _sectorAngle;
                float relShift = rel + half;
                if (relShift >= 360f) relShift -= 360f;

                int sectorIndex = (int)(relShift * _invSectorAngle);
                if (sectorIndex < 0) sectorIndex = 0;
                if (sectorIndex >= enemySectors) sectorIndex = enemySectors - 1;

                // Im blizej, tym wieksza wartosc 0..1 (tylko gdy LOS jest czysty)
                float value = 1f - (dist / enemyViewRadius);

                //bierzemy maksimum w sektorze (najmocniej "swiecacy" wrog)
                if (value > _enemySectorsBuf[sectorIndex])
                    _enemySectorsBuf[sectorIndex] = value;
            }

            // odswiezenie znacznika swiezosci
            _timeSinceLos = 0f;
        }

        // Przepisz sektory do _inputs
        int offset = rayCount;
        _maxEnemySignal = 0f;
        for (int s = 0; s < enemySectors; s++)
        {
            float v = _enemySectorsBuf[s];
            _inputs[offset + s] = _enemySectorsBuf[s];
            if (v > _maxEnemySignal) _maxEnemySignal = v;
        }

        offset += enemySectors; // przesuwamy sie za sektory

        // --- 3) Dodatkowe wejscia (opcjonalne usprawnienia) ---
        // 1) zdrowie [0..1]
        _inputs[offset++] = Mathf.Clamp01(health / maxHealth);

        // 2) cooldown strzalu [0..1] (0 = gotowy, 1 = pelny CD)
        float shoot_cd_norm = (fireCooldown <= 0f) ? 0f : Mathf.Clamp01(fireTimer / fireCooldown);
        _inputs[offset++] = shoot_cd_norm;
    }



    // ========= WALKA / FITNESS =========

    // Odbierz obrazenia. Jesli atakujacy != null, dopisz mu nagrode za DMG/frag.
    public void ApplyHit(float damage, BotAgent attacker)
    {
        if (isDead) return;


        // ile realnie "weszlo" (nie schodzimy ponizej 0 HP)
        float applied = Mathf.Min(health, Mathf.Max(0f, damage));
        damageTaken += applied;
        health -= damage;

        if (attacker != null)
        {
            attacker.damageDealt += Mathf.Max(0f, damage);
            if (damage > 0f) attacker.shotsHit++; // licznik trafien
        }

        if (health <= 0f)
        {
            if (attacker != null) attacker.kills++;
            Die();
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        gameObject.SetActive(false);

        // (opcjonalnie) wypisz z areny:
        //if (arena != null) arena.UnregisterBot(this); - moze spowodowac bledy w miejscach gdzie
        // iteruje po kolekcji botow a ktorys z nich zostalby usuniety w trakcie iteracji.

        // zglos smierc do PopulationManager (zapisze fitness i policzy, czy koniec generacji)
        if (PopulationManager.Instance != null)
            PopulationManager.Instance.OnAgentDied(this);
    }

    public bool IsDead() => isDead;

    // Mid-reset bota w trakcie generacji (respawn na arenie)
    public void MidReset(Vector3 pos, float rotZ, bool reviveDead)
    {
        // Jezeli bot martwy i nie chcemy wskrzeszac – nic nie rób
        if (isDead && !reviveDead) return;

        // Wskrzes lub po prostu przestaw bota
        isDead = false;
        gameObject.SetActive(true);

        // Pelne HP i gotowosc do strzalu
        health = maxHealth;
        fireTimer = 0f;

        // Przestaw pozycje i rotacje (i kopie stanu "logiczna")
        position = pos;
        rotationDeg = rotZ;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);

        // Reset stanow chwilowych anty-orbit (nie kasuj statystyk fitness)
        spinEMA = 0f;
        spinIIRTime = 0f;


        // Bufor sektorow i sygnal celu zostana przeliczone w nastepnym FillInputs()
        _maxEnemySignal = 0f;
    }




    // Oblicza fitness bota na podstawie:
    // - czasu zycia, przebytych metrlw, zadanych obrazen i fragow (nagrody bazowe),
    // - jakosci strzelania (nagroda za trafienia, lekka kara za kazdy strzal),
    // - kar behawioralnych (szorowanie o sciane, bycie "stuck", powtarzanie tych samych wyjsc bez celu).
    //   oraz dlugie krecenie sie bez celu.
    // Zwraca wartosc >= 0 (zabezpieczenie przed ujemnymi fitnesami).
    public float GetFitness()
    {
        // 1) Nagrody bazowe (zalezne od przebiegu walki) ---
        // timeAlive       – zacheca do przetrwania,
        // distanceTravelled – promuje eksploracje / ruch,
        // damageDealt     – silnie promuje skutecznosc bojowa,
        // kills           – duzy bonus za fragi (dokreca selekcje najlepiej walczacych).
        float baseScore =
            timeAlive * wAlive +
            distanceTravelled * wDist +
            damageDealt * wDmg +
            kills * wKill;

        // 2) Jakosc strzelania ---
        // W praktyce: nagroda "per trafienie" – kara "per strzal".
        // (wAcc * acc * shotsFired == wAcc * shotsHit)
        float shootingScore =
            wAcc * shotsHit   // nagroda rosnie tylko, gdy faktycznie trafiasz
          - wShots * shotsFired; // lekka kara za kazdy oddany strzal (anty-spam)

        // Dodatkowa kara za spam, gdy w sektorach nic nie ma (brak celu)
        if (_maxEnemySignal < 0.1f)
            shootingScore -= 0.5f * wShots * shotsFired;  // 0.5 na start (mozna 1.0)

        // 3) Kary behawioralne (akumulowane w Elapse) ---
        float penalties =
        wBump * wallBumpTime +
        wSpinIIR * spinIIRTime;


        // --- 4) Suma i zabezpieczenia ---
        float score = baseScore + shootingScore - penalties;

        // Asekuracja na NaN/Inf (np. gdyby cos poszlo nie tak z wagami)
        if (float.IsNaN(score) || float.IsInfinity(score))
            score = 0f;

        // Zazwyczaj GA/EA lepiej dzialaja z nieujemnym fitnessem.
        // Jesli dopuszcza ujemne – mozna zwrocic score bez clampowania.
        return Mathf.Max(0f, score);
    }

    // -------------------- Debug wizualny --------------------
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos) return;  // wylacznik debugowania

        Vector3 pos = transform.position;

        // Raycasty do scian (cyan)
        if (_baseRayDirs != null)
        {
            float rotRad = transform.eulerAngles.z * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rotRad);
            float sin = Mathf.Sin(rotRad);

            Gizmos.color = Color.cyan;
            //Gizmos.color = new Color(0f, 1f, 1f, 0.5f); // polprzezroczysty cyjan
            for (int i = 0; i < _baseRayDirs.Length; i++)
            {
                Vector2 baseDir = _baseRayDirs[i];
                Vector2 dir;
                dir.x = baseDir.x * cos - baseDir.y * sin;
                dir.y = baseDir.x * sin + baseDir.y * cos;

                Gizmos.DrawLine(pos, pos + (Vector3)(dir * rayLength));
            }
        }

        // Zasieg wrogow (pomaranczowa obrecz)
        //Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        //Gizmos.DrawWireSphere(transform.position, enemyViewRadius);
        // Zasieg wrogow – gladka obrecz bez wewnetrznych linii
#if UNITY_EDITOR
        {
            Handles.color = new Color(1f, 0.5f, 0f, 0.9f); // bardziej wyrazny pomarancz
            var c = transform.position;
            c.z -= 0.01f; // minimalny offset, zeby nie zlewac sie z innymi liniami
            Handles.DrawWireDisc(c, Vector3.forward, enemyViewRadius);
        }
#else
// fallback poza edytorem: recznie narysowany okrag z segmentow
DrawCircleGizmo(transform.position, enemyViewRadius, 64, new Color(1f, 0.5f, 0f, 0.9f));
#endif



        // Kierunek "forward" bota (niebieski) - poprawic nie widac tego
        //float deg = Application.isPlaying ? rotationDeg : transform.eulerAngles.z;
        //Vector3 fwd = Quaternion.Euler(0, 0, deg) * Vector3.right;
        //Gizmos.color = Color.blue;
        //Gizmos.DrawLine(pos, pos + fwd * (enemyViewRadius * 0.5f));
        // Wyrazny kierunek "forward" (gruba strzalka)
        float deg = Application.isPlaying ? rotationDeg : transform.eulerAngles.z;
        Vector3 fwd = Quaternion.Euler(0, 0, deg) * Vector3.right;

#if UNITY_EDITOR
        {
            Vector3 tip = pos + fwd * (enemyViewRadius * 0.55f);
            Vector3 tail = pos;

            // gruba linia glowna
            Handles.color = Color.white;                // wyrazny kolor, inny niz cyjan od rayow
            Handles.DrawAAPolyLine(6f, new Vector3[] { tail, tip });

            // grot strzaly (dwie krotkie kreski pod katem)
            float headLen = enemyViewRadius * 0.08f;
            float headAng = 25f;

            Vector3 left = Quaternion.Euler(0, 0, -headAng) * (-fwd);
            Vector3 right = Quaternion.Euler(0, 0, headAng) * (-fwd);

            Handles.DrawAAPolyLine(6f, new Vector3[] { tip, tip + left * headLen });
            Handles.DrawAAPolyLine(6f, new Vector3[] { tip, tip + right * headLen });
        }
#else
// fallback poza edytorem (cienka linia jak wczesniej)
Gizmos.color = Color.blue;
Gizmos.DrawLine(pos, pos + fwd * (enemyViewRadius * 0.55f));
#endif





        // Sektory wrogow + slupki wartosci (tylko w Play)
        if (enemySectors > 0)
        {
            float sectorAngle = 360f / enemySectors;
            float rotDeg = Application.isPlaying ? rotationDeg : transform.eulerAngles.z;

            // Granice sektorow (wyrazne pomaranczowe) – granica 0 jest w -half wzgledem przodu,
            // dzieki czemu srodek sektora 0 wypada dokladnie na przodzie.
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
            float half = 0.5f * sectorAngle;
            for (int s = 0; s < enemySectors; s++)
            {
                float aDeg = rotDeg - half + s * sectorAngle;
                float aRad = aDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(aRad), Mathf.Sin(aRad), 0f);
                Gizmos.DrawLine(pos, pos + dir * enemyViewRadius);
            }

            // Wizualizacja sily sektora (tylko w Play)  — kolor od zielonego (0) do czerwonego (1)
            if (Application.isPlaying && _enemySectorsBuf != null && _enemySectorsBuf.Length == enemySectors)
            {
                for (int s = 0; s < enemySectors; s++)
                {
                    //float midDeg = rotDeg + (s + 0.5f) * sectorAngle;
                    float midDeg = rotDeg + s * sectorAngle;
                    float midRad = midDeg * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(midRad), Mathf.Sin(midRad), 0f);

                    float val = Mathf.Clamp01(_enemySectorsBuf[s]); // 0..1
                    float barLen = 0.25f * enemyViewRadius * val;   // dlugosc slupka zalezna od wartosci

                    // kolor gradientowy (zielony -> czerwony)
                    Gizmos.color = Color.Lerp(Color.green, Color.red, val);

                    // rysowanie slupka w zewnetrznej cwiartce okregu
                    Vector3 start = pos + dir * (0.75f * enemyViewRadius);
                    Gizmos.DrawLine(start, start + dir * barLen);

                    // dla lepszej widocznosci – kropka na koncu slupka
                    Gizmos.DrawSphere(start + dir * barLen, 0.05f * enemyViewRadius * 0.1f);
                }
            }

        }


        // Pasek zdrowia nad botem (tylko Play)
        if (Application.isPlaying)
        {
            float hpRatio = Mathf.Clamp01(health / Mathf.Max(0.0001f, maxHealth));
            Vector3 barStart = pos + Vector3.up * 0.8f;
            Vector3 barEnd = barStart + Vector3.right * 1.0f;

            // tlo (czerwone)
            Gizmos.color = Color.red;
            Gizmos.DrawLine(barStart, barEnd);

            // aktualne HP (zielone)
            Gizmos.color = Color.green;
            Gizmos.DrawLine(barStart, Vector3.Lerp(barStart, barEnd, hpRatio));
        }



        // Linie LOS do faktycznie widocznych wrogow (magenta, tylko Play)
        if (Application.isPlaying && arena != null && _rayHits != null)
        {
            var agents = arena.GetAgents();
            for (int i = 0; i < agents.Count; i++)
            {
                var enemy = agents[i];
                if (enemy == null || enemy.IsDead() || ReferenceEquals(enemy, this)) continue;

                Vector3 enemyPos = enemy.transform.position;
                Vector3 toEnemy3 = enemyPos - pos;
                float sqrDist = toEnemy3.sqrMagnitude;
                if (sqrDist > _enemyViewRadiusSqr) continue; // poza zasiegiem

                float dist = Mathf.Sqrt(sqrDist);
                if (dist < 1e-6f) continue;

                Vector2 dir = (Vector2)(toEnemy3 / dist);

                // uzywamy tego samego "skin", zeby nie trafic w siebie/sciane przy starcie
                int losHits = Physics2D.RaycastNonAlloc(pos + (Vector3)(dir * skin), dir, _rayHits, Mathf.Max(0f, dist - skin), wallMask);
                if (losHits == 0)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(pos, enemyPos);
                }
            }
        }
    }
#endif

#if UNITY_EDITOR
    private void OnValidate()
    {
        // ostrzez jezeli biezacy obiekt (lub jego dzieci) jest na warstwie z wallMask
        int myLayer = gameObject.layer;
        if ((wallMask.value & (1 << myLayer)) != 0)
            Debug.LogError($"[BotAgent:{name}] Warstwa obiektu ({LayerMask.LayerToName(myLayer)}) jest w wallMask! " +
                           "Bot nie moze byc na warstwie przeszkod.");

        // sprawdz dzieci (np. collider na dziecku)
        var cols = GetComponentsInChildren<Collider2D>(true);
        foreach (var c in cols)
        {
            int l = c.gameObject.layer;
            if ((wallMask.value & (1 << l)) != 0)
                Debug.LogError($"[BotAgent:{name}] Dziecko '{c.name}' jest na warstwie w wallMask ({LayerMask.LayerToName(l)}).");
        }
    }
#endif

#if UNITY_EDITOR
    private void AssertFrontRayIsForward()
    {
        // policz kierunek ray0 tak jak w FillInputs
        float rotRad = rotationDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rotRad), sin = Mathf.Sin(rotRad);

        Vector2 base0 = _baseRayDirs != null && _baseRayDirs.Length > 0 ? _baseRayDirs[0] : Vector2.right;
        Vector2 ray0;
        ray0.x = base0.x * cos - base0.y * sin;
        ray0.y = base0.x * sin + base0.y * cos;

        Vector2 fwd = new Vector2(Mathf.Cos(rotRad), Mathf.Sin(rotRad));
        if (Vector2.Dot(ray0.normalized, fwd) < 0.999f)
            Debug.LogError($"[BotAgent:{name}] Ray0 NIE jest do przodu! Sprawdz _baseRayDirs kolejnosc.");
    }
#endif


    //#if UNITY_EDITOR
    //    [ContextMenu("Diagnostics/Dump sensors (once)")]
    //    private void DebugDumpSensorsOnce()
    //    {
    //        // 1) policz sensory (to ustawi tez _maxEnemySignal, _enemySectorsBuf, _inputs)
    //        FillInputs();

    //        // 2) Ray’e (0..1): 0 = bardzo blisko sciany, 1 = daleko/brak przeszkody
    //        System.Text.StringBuilder sb = new System.Text.StringBuilder();
    //        sb.AppendLine($"[BotAgent:{name}] DUMP SENSORS @pos={transform.position} rot={rotationDeg:F1}deg");
    //        sb.Append("RAYS: ");
    //        for (int i = 0; i < rayCount; i++)
    //        {
    //            float v = (i < _inputs.Length) ? _inputs[i] : -1f;
    //            sb.Append(v.ToString("0.00")).Append(i == rayCount - 1 ? "" : ", ");
    //        }
    //        sb.AppendLine();

    //        // 3) SEKTORY wrogów (0..1) + max sygnal
    //        sb.Append("ENEMY SECTORS: ");
    //        for (int s = 0; s < enemySectors; s++)
    //        {
    //            float v = (s < _enemySectorsBuf.Length) ? _enemySectorsBuf[s] : -1f;
    //            sb.Append(v.ToString("0.00")).Append(s == enemySectors - 1 ? "" : ", ");
    //        }
    //        sb.AppendLine();
    //        sb.AppendLine($"maxEnemySignal = {_maxEnemySignal:0.00}");

    //        // 4) HP i cooldown (ostatnie dwa wejscia)
    //        int hpIdx = rayCount + enemySectors;
    //        int cdIdx = hpIdx + 1;
    //        float hpIn = (hpIdx < _inputs.Length) ? _inputs[hpIdx] : -1f;
    //        float cdIn = (cdIdx < _inputs.Length) ? _inputs[cdIdx] : -1f;
    //        sb.AppendLine($"HP_in={hpIn:0.00}, ShootCD_in={cdIn:0.00}");

    //        Debug.Log(sb.ToString());
    //    }
    //#endif


    //DEBUG
    //#if UNITY_EDITOR
    //    [ContextMenu("Debug/Init dummy genome")]
    //    private void DebugInitDummyGenome()
    //    {
    //        // policz liczbe wejsc z aktualnych ustawien sensorow
    //        int inputCount = Mathf.Max(1, rayCount) + Mathf.Max(1, enemySectors) + 2; // +2 = HP + cooldown

    //        // prosty uklad warstw: [wejscia, ukryta, wyjscia]
    //        // jesli chcesz, podmien 22 na inna liczbe – to tylko debug.
    //        int[] layers = { inputCount, 22, 4 };

    //        // utworz genom i siec
    //        genome = new Genome(layers);
    //        brain = genome.network;

    //        // podlacz arene (szuka w rodzicu)
    //        arena = GetComponentInParent<ArenaManager>();

    //        // zresetuj stan jak przy normalnym starcie
    //        Init(genome, arena);

    //        // dodatkowo ustaw od razu rotacje testowa
    //        //rotationDeg = -180f; // lub 90, 180, 270
    //        //transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);

    //        Debug.Log($"{name}: dummy genome zainicjalizowany (inputs={inputCount}).");
    //    }
    //#endif


    // Fallback: rysuje okrag pojedyncza obrecza za pomoca Gizmos (poza edytorem)
    private void DrawCircleGizmo(Vector3 center, float radius, int segments, Color color)
    {
        if (segments < 3) segments = 3;
        Gizmos.color = color;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

}
