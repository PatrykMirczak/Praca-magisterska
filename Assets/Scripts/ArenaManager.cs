using UnityEngine;
using System.Collections.Generic;

public class ArenaManager : MonoBehaviour
{
    [Header("Prefab / Rooty z prefabu areny")]
    [SerializeField] private GameObject botPrefab;          //AI-bot z NN
    [SerializeField] private GameObject randomBotPrefab;    //AI-bot bez NN (RandomBot)
    [SerializeField] private GameObject bulletPrefab;

    [SerializeField] private Transform spawnPointsRoot; // child: SpawnPoints
    [SerializeField] private Transform botsRoot;        // child: BotsRoot
    [SerializeField] private Transform bulletsRoot;     // child: BulletsRoot
    [SerializeField] private Transform obstaclesRoot;   // child: ObstaclesRoot (opcjonalnie)

    [Header("Warstwy i fizyka srodowiska")]
    [SerializeField] private LayerMask wallMask = ~0;   // do raycastow w BotAgent


    [Header("Pooling pocisków")]
    [SerializeField] private int prewarmBullets = 160; // dopasowac w Inspectorze w razie czego

    [Header("Parametry pociskow")]
    [SerializeField] private float bulletSpeed = 12f;
    [SerializeField] private float bulletLife = 3f;     // sekundy
    [SerializeField] private float bulletRadius = 0.15f; // do prostych kolizji
    [SerializeField] private float bulletDamage = 25f;     // obrazenia jednego trafienia

    // --- Stan areny ---
    private readonly List<BotAgent> _bots = new List<BotAgent>(64);
    private readonly List<IAgent> _agents = new List<IAgent>(128);   // wszyscy trafialni (AI + Random)
    private List<Bullet> _bullets;   // aktywne pociski
    private Queue<Bullet> _bulletPool; // wolne pociski (pool)

    // cache spawnpointow
    private Transform[] _spawnPoints;

    public IReadOnlyList<BotAgent> Bots => _bots; // tylko AI-boty z NN
    public IReadOnlyList<IAgent> Agents => _agents; // wszyscy trafialni (AI + Random)

    private void Awake()
    {
        // Autodetekcja dzieci po nazwach (mozesz tez przypisac w Inspectorze)
        if (spawnPointsRoot == null) spawnPointsRoot = transform.Find("SpawnPoints");
        if (botsRoot == null) botsRoot = transform.Find("BotsRoot");
        if (bulletsRoot == null) bulletsRoot = transform.Find("BulletsRoot");
        if (obstaclesRoot == null) obstaclesRoot = transform.Find("ObstaclesRoot");

        // Zbierz spawnpointy
        if (spawnPointsRoot != null)
        {
            int n = spawnPointsRoot.childCount;
            _spawnPoints = new Transform[n];
            for (int i = 0; i < n; i++)
                _spawnPoints[i] = spawnPointsRoot.GetChild(i);
        }
        else
        {
            _spawnPoints = new Transform[0];
            Debug.LogWarning("[ArenaManager] Brak SpawnPointsRoot – nie bedzie gdzie stawiac botow.");
        }

        // sensowny prewarm jesli nie podasz:
        if (prewarmBullets <= 0)
        {
            prewarmBullets = 128;
        }

        int cap = Mathf.Max(128, prewarmBullets); // bezpieczne minimum

        _bullets = new List<Bullet>(cap);
        _bulletPool = new Queue<Bullet>(cap);

        PrewarmBullets(prewarmBullets);
    }

    //Klatka symulacji areny – obsluga botow i pociskow.
    public void Elapse(float deltaTime)
    {
        // 1) Agenci (NN i Random)
        for (int i = 0, n = _agents.Count; i < n; i++)
            if (_agents[i] != null && !_agents[i].IsDead())
                _agents[i].Elapse(deltaTime);


        // 2) Pociski
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            if (b == null || !b.IsActive)
            {
                _bullets.RemoveAt(i);
                continue;
            }

            if (b.Elapse(deltaTime, this))
            {
                // zwraca true, gdy trzeba zwrocic do puli (trafienie lub zycie minelo)
                ReleaseBullet(b);
                _bullets.RemoveAt(i);
            }
        }
    }

    //Czysci arene z botow i pociskow.
    public void ClearArena()
    {
        // pociski
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            if (b != null) ReleaseBullet(b);
        }
        _bullets.Clear();

        // boty AI
        for (int i = _bots.Count - 1; i >= 0; i--)
        {
            if (_bots[i] != null) Destroy(_bots[i].gameObject);
        }
        _bots.Clear();

        // Random
        for (int i = _agents.Count - 1; i >= 0; i--)
        {
            // usun tylko te, ktore NIE sa w _bots (bo AI usunieci wyzej)
            if (_agents[i] is RandomBot rb && rb != null)
                Destroy(rb.gameObject);
        }
        _agents.Clear();
    }

    public void SpawnAllAgents(List<Genome> genomes, int randomCount)
    {
        if (genomes == null) return;

        int aiCount = genomes.Count;
        int totalAgents = aiCount + randomCount;
        int spCount = _spawnPoints.Length;

        // Walidacja
        if (spCount != totalAgents)
        {
            Debug.LogError($"[ArenaManager:{name}] Liczba spawnow ({spCount}) nie rowna sie liczbie agentow ({totalAgents}). " +
                           $"AI={aiCount}, Random={randomCount}. Sprawdz konfiguracje!");
            return;
        }

        // Permutacja spawnpointow
        int[] order = new int[spCount];
        for (int i = 0; i < spCount; i++) order[i] = i;
        for (int i = spCount - 1; i > 0; i--)
        {
            int j = Matrix.MatrixRand.RangeInt(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // Spawn AI (BotAgent)
        for (int i = 0; i < aiCount; i++)
        {
            var t = _spawnPoints[order[i]];
            Vector3 pos = t.position;
            float rotZ = t.eulerAngles.z;

            var go = Instantiate(botPrefab, pos, Quaternion.Euler(0, 0, rotZ), botsRoot);
            var agent = go.GetComponent<BotAgent>();
            if (agent == null)
            {
                Debug.LogError("[ArenaManager] Bot prefab nie ma komponentu BotAgent!");
                Destroy(go);
                continue;
            }

            agent.Init(genomes[i], this);
            agent.wallMask = wallMask;

            RegisterBot(agent);
            RegisterAgent(agent);
        }

        //Spawn RandomBot
        for (int r = 0; r < randomCount; r++)
        {
            int idx = aiCount + r;
            var t = _spawnPoints[order[idx]];
            Vector3 pos = t.position;
            float rotZ = t.eulerAngles.z;

            var go = Instantiate(randomBotPrefab, pos, Quaternion.Euler(0, 0, rotZ), botsRoot);
            var rb = go.GetComponent<RandomBot>();
            if (rb == null)
            {
                Destroy(go);
                continue;
            }

            rb.Init(this, pos, rotZ);
            rb.wallMask = wallMask;

            RegisterAgent(rb); // randomy tylko do listy ogolnej
        }
    }



    // Rejestruje trafialnego agenta (AI-bot lub RandomBot)

    public void RegisterBot(BotAgent agent)
    {
        if (agent != null && !_bots.Contains(agent))
            _bots.Add(agent);
    }
    private void RegisterAgent(IAgent a)
    {
        if (a != null && !_agents.Contains(a))
            _agents.Add(a);
    }

    public void UnregisterAgent(IAgent a)
    {
        if (a != null) _agents.Remove(a);
        if (a is BotAgent ba) _bots.Remove(ba);
    }

    // Zwraca referencje do listy wszystkich botow na arenie.
    // Bot powinien pominac samego siebie podczas iteracji.
    // Brak alokacji – nie tworzymy nowych list.
    //public IReadOnlyList<BotAgent> GetEnemiesFor(BotAgent self) => _bots; - przed randomowymi botami

    // Po wprowadzeniu RandomBotow, zwraca liste wszystkich trafialnych agentow
    // Dla BotAgent.FillInputs
    public IReadOnlyList<IAgent> GetAgents() => Agents;


    private void PrewarmBullets(int count)
    {
        if (bulletPrefab == null || bulletsRoot == null) return;
        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(bulletPrefab, bulletsRoot);
            var b = go.GetComponent<Bullet>();
            if (b == null) { Destroy(go); continue; }
            b.Deactivate();
            _bulletPool.Enqueue(b);
        }
    }

    private Bullet GetBullet()
    {
        Bullet b;
        if (_bulletPool.Count > 0)
            b = _bulletPool.Dequeue();
        else
        {
            var go = Instantiate(bulletPrefab, bulletsRoot);
            b = go.GetComponent<Bullet>();
            if (b == null) { Destroy(go); return null; }
            b.Deactivate(); // na wszelki
        }
        _bullets.Add(b);
        return b;
    }

    private void ReleaseBullet(Bullet b)
    {
        if (b == null) return;
        if (!b.IsActive) return;    // juz w puli
        b.Deactivate();
        _bulletPool.Enqueue(b);
    }

    // Prosba o strzal – instancjuje pocisk i nadaje mu predkosc/kierunek.
    // Pociski: owner moze byc null (gdy strzela RandomBot)
    // --- zmiana: RequestShot uzywa puli ---
    public void RequestShot(IAgent shooter, Vector2 pos, Vector2 dir)
    {
        if (bulletPrefab == null || bulletsRoot == null) return;

        // normalizacja kierunku i "wyjecie z lufy"
        Vector2 ndir = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.right;
        float shooterRadius = shooter != null ? shooter.BodyRadius : 0f;
        float muzzleOffset = shooterRadius + bulletRadius + 0.01f; // epsilon
        Vector2 spawnPos = pos + ndir * muzzleOffset;


        var bullet = GetBullet();
        if (bullet == null) return;

        // kredyt do fitnessu tylko jesli strzela BotAgent (AI)
        BotAgent ownerForCredit = shooter as BotAgent;

        bullet.Activate(
           ownerAgent: shooter,                 // IAgent (BotAgent lub RandomBot)
           ownerForCredit: ownerForCredit,      // BotAgent albo null
           startPos: spawnPos,
           velocity: ndir * bulletSpeed,
           lifeTime: bulletLife,
           radius: bulletRadius,
           damage: bulletDamage,
           wallMask: wallMask
       );
    }


    //pomocnicze
    public LayerMask GetWallMask() => wallMask;



    // Opcjonalnie: wyczysc same pociski (uzyteczne przy mid-resecie)
    public void ClearBullets()
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            if (b != null) ReleaseBullet(b);
        }
        _bullets.Clear();
    }

    // Na wszelki Release dla Bullet (jesli kiedys bede chcial wolac z Bulleta)
    public void ReturnBulletToPool(Bullet b) => ReleaseBullet(b);

    // Instant respawn randoma po smierci
    public void RespawnRandomImmediately(RandomBot rb)
    {
        if (rb == null) return;
        // losowy spawn
        Vector3 pos; float rotZ = 0f;
        if (_spawnPoints.Length > 0)
        {
            int idx = Matrix.MatrixRand.RangeInt(0, _spawnPoints.Length);
            pos = _spawnPoints[idx].position;
            rotZ = _spawnPoints[idx].eulerAngles.z;
        }
        else pos = transform.position;

        rb.RespawnAt(pos, rotZ);
    }


    public void RespawnAllAgentsMidway(bool reviveAI = true, bool respawnRandoms = true, bool clearBullets = true)
    {
        if (clearBullets) ClearBullets();

        int aiCount = _bots.Count;
        int randCount = 0;
        for (int i = 0; i < _agents.Count; i++)
            if (_agents[i] is RandomBot) randCount++;

        int totalAgents = aiCount + randCount;
        int spCount = _spawnPoints.Length;

        if (spCount != totalAgents)
        {
            Debug.LogWarning($"[ArenaManager:{name}] MidReset: spawnPoints={spCount} != agents={totalAgents} (AI={aiCount}, RND={randCount}). Pomijam respawn all.");
            return;
        }

        // permutacja spawnow
        int[] order = new int[spCount];
        for (int i = 0; i < spCount; i++) order[i] = i;
        for (int i = spCount - 1; i > 0; i--)
        {
            //int j = Random.Range(0, i + 1);
            int j = Matrix.MatrixRand.RangeInt(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // najpierw AI
        for (int i = 0; i < aiCount; i++)
        {
            var bot = _bots[i];
            if (bot == null) continue;
            var t = _spawnPoints[order[i]];
            bot.MidReset(t.position, t.eulerAngles.z, reviveAI);
        }

        // potem randomy
        if (respawnRandoms)
        {
            int idx = aiCount;
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i] is RandomBot rb && rb != null)
                {
                    var t = _spawnPoints[order[idx++]];
                    rb.RespawnAt(t.position, t.eulerAngles.z);
                }
            }
        }
    }

//    // DEBUG
//#if UNITY_EDITOR
//    [ContextMenu("Debug/Rescan & register agents in children")]
//    private void DebugRescanAgentsInChildren()
//    {
//        _agents.Clear();
//        _bots.Clear();

//        var foundAgents = GetComponentsInChildren<IAgent>(true);
//        int aCount = 0, bCount = 0;
//        foreach (var a in foundAgents)
//        {
//            if (a == null) continue;
//            if (!_agents.Contains(a)) { _agents.Add(a); aCount++; }
//            if (a is BotAgent ba && !_bots.Contains(ba)) { _bots.Add(ba); bCount++; }
//        }

//        Debug.Log($"[Arena:{name}] Registered {_agents.Count} agents (AI={_bots.Count}). Added now: {aCount} / {bCount}.");
//    }
//#endif

}