using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PopulationManager : MonoBehaviour
{
    public static PopulationManager Instance { get; private set; }

    [Header("Genetyka")]
    [SerializeField] private int populationSize = 120;     // 15 aren x po 8 botow z nn = 120
    [SerializeField] private float mutationRate = 0.05f;
    [SerializeField] private int[] layerSizes = new int[] { 18, 16, 4 }; // wymiary sieci

    [Header("Losowosc (bieg = replicate)")]
    [Tooltip("Ile generacji trwa jeden bieg (replicate).")]
    [SerializeField] private int generationsPerRun = 1000;

    [Header("Koniec serii")]
    [SerializeField] private bool autoQuitOnFinish = true;
    [SerializeField] private float quitDelaySeconds = 0.25f; // realtime

    [Tooltip("Seedy uzywane dla kolejnych biegow (replicate_id = indeks na liscie).")]
    [SerializeField] private int[] replicateSeeds = new int[] { 111, 222, 333, 444, 555 };

    [Header("Ewaluacja po treningu (freeze & evaluate)")]
    [Tooltip("Liczba epizodow testowych bez ewolucji po zakonczeniu biegu.")]
    [SerializeField] private int evalEpisodes = 10;

    [Tooltip("Wspolne (CRN) seedy dla epizodow ewaluacyjnych. Te same dla wszystkich biegow.")]
    [SerializeField] private int[] evalSeeds = new int[] { 1001, 2002, 3003, 4004, 5005, 6006, 7007, 8008, 9009, 1010 };


    [Header("Random bots")]
    [SerializeField] private int randomPerArena = 8; // ilosc botow bez nn na arene (reszta z nn)

    [Header("Areny")]
    [SerializeField] private List<ArenaManager> arenas = new List<ArenaManager>();

    [Header("Czas trwania generacji (sekundy SYMULACJI)")]
    [SerializeField] private float generationDurationSimSeconds = 10f;

    [Header("Mid-reset w polowie generacji")]
    [SerializeField] private bool midResetEnabled = true;
    [SerializeField, Range(0.1f, 0.9f)]
    private float midResetAtFraction = 0.5f;            // 0.5 = polowa
    [SerializeField] private bool midResetReviveDead = true;  // wskrzeszaj martwych
    [SerializeField] private bool midResetClearBullets = true;  // czyscic pociski

    private bool midResetDone = false;

    [Header("Statystyki / Logowanie")]
    [SerializeField] private bool writeStatsCsv = true;
    [SerializeField] private string statsCsvFile = "PerBotStats.csv";
    [Tooltip("Zapisuj statystyki co N generacji (1 = kazda generacja).")]
    [SerializeField] private int csvWriteEveryNGenerations = 1;


    private System.Random rng; // << JEDYNY PRNG w calej probie

    public GeneticAlgorithm GA { get; private set; }
    public int AliveCount { get; private set; }
    private int totalPopulationThisGen = 0;

    public int Generation => GA?.generation ?? 0;
    public int ReplicateId => replicateIndex;   // 0..(replicateSeeds.Length-1)
    public int CurrentSeed => currentSeed;      // seed biegu(trening)
    public int ActiveSeed => activeSeed;              // seed uzyty w tej generacji (train: CurrentSeed, eval: evalSeed)
    public bool ExperimentsFinished => experimentsFinished;

    private enum Phase { Train, Eval, Done }
    private Phase phase = Phase.Train;

    // Stan biezacego biegu
    private int replicateIndex = 0;      // 0..replicateSeeds.Length-1
    private int currentSeed = 0;         // seed faktycznie uzyty w tym biegu
    private int activeSeed = 0;       // seed biezacej generacji (patrz wyzej)
    private bool experimentsFinished = false;

    // Eval
    private int evalEpisodeIndex = -1;   // 0..evalEpisodes-1 gdy phase==Eval


    private float simTimeThisGen = 0f;


    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Jesli nie przypisano aren w Inspectorze, sprobuj znalezc w scenie
        if (arenas.Count == 0)
        {
            // Wersja "tylko aktywne obiekty"
            var found = UnityEngine.Object.FindObjectsByType<ArenaManager>(FindObjectsSortMode.None);

            // Jesli rowniez nieaktywne:
            //var found = UnityEngine.Object.FindObjectsByType<ArenaManager>(
            //    FindObjectsSortMode.None,
            //    FindObjectsInactive.Include
            //);

            arenas.AddRange(found);
            arenas.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        }

        // Start pierwszego biegu
        replicateIndex = 0;
        phase = Phase.Train;
        InitRngForCurrentReplicate();

    }

    private void InitRngForCurrentReplicate()
    {
        // Wybierz seed z listy (jesli brak – uzyj losowego TickCount)
        if (replicateSeeds != null && replicateSeeds.Length > 0)
        {
            int idx = Mathf.Clamp(replicateIndex, 0, replicateSeeds.Length - 1);
            currentSeed = replicateSeeds[idx];
        }
        else
        {
            currentSeed = Environment.TickCount;
        }

        rng = new System.Random(currentSeed);
        Matrix.MatrixRand.Rng = rng;
        activeSeed = currentSeed;

        Debug.Log($"[PopulationManager] Start replicate {replicateIndex + 1}/{Mathf.Max(1, replicateSeeds?.Length ?? 0)} | seed(train) = {currentSeed}");
    }

    // Wywoluj z GameManagera co "krok symulacji"
    public void Tick(float simDt)
    {
        if (experimentsFinished) return;

        simTimeThisGen += simDt;

        // Mid-reset w trakcie generacji
        if (midResetEnabled && !midResetDone &&
            simTimeThisGen >= generationDurationSimSeconds * midResetAtFraction)
        {
            foreach (var arena in arenas)
            {
                arena.RespawnAllAgentsMidway(reviveAI: midResetReviveDead, respawnRandoms: true,
                clearBullets: midResetClearBullets);
            }
            //arena.RespawnBotsMidway(midResetReviveDead, midResetClearBullets);

            // jezeli wskrzeszamy martwych, to AliveCount wraca do pelnej liczby
            if (midResetReviveDead)
                AliveCount = totalPopulationThisGen;

            midResetDone = true;
            Debug.Log($"[PopulationManager] Gen {Generation}: mid-reset at t={simTimeThisGen:F2}s");
        }


        if (simTimeThisGen >= generationDurationSimSeconds)
        {
            FinalizeCurrentGeneration();

            if (phase == Phase.Train)
            {

                // Czy konczymy bieg? (liczymy dokladnie N finalizacji na bieg) - 0...999
                //bool endOfRun = (GA != null) && ((GA.generation + 1) >= generationsPerRun);

                // MA BYC (dla gen. 0..1000 przy generationsPerRun = 1000):
                bool endOfRun = (GA != null) && (GA.generation >= generationsPerRun);

                if (endOfRun && evalEpisodes > 0)
                {
                    BeginEvalPhase(); // zaczynamy ewaluacje bez ewolucji
                }
                else if (endOfRun) // brak ewaluacji – od razu kolejny bieg
                {
                    AdvanceReplicate();
                    if (!experimentsFinished) StartNewGeneration(true);
                }
                else
                {
                    StartNewGeneration(true); // normalny trening
                }
            }
            else if (phase == Phase.Eval)
            {
                // kolejny epizod lub koniec ewaluacji
                evalEpisodeIndex++;
                if (evalEpisodeIndex < evalEpisodes)
                {
                    StartEvalEpisode(evalEpisodeIndex);
                }
                else
                {
                    // koniec ewaluacji -> kolejny bieg
                    AdvanceReplicate();
                    if (!experimentsFinished)
                    {
                        phase = Phase.Train;
                        StartNewGeneration(true);
                    }
                }
            }
        }
    }
    // --------------- FAZA TRAIN/EVAL ---------------
    private void BeginEvalPhase()
    {
        phase = Phase.Eval;
        evalEpisodeIndex = 0;
        Debug.Log($"[PopulationManager] >>> Begin EVAL phase: {evalEpisodes} episodes (CRN) <<<");

        StartEvalEpisode(evalEpisodeIndex);
    }

    private void StartEvalEpisode(int epIndex)
    {
        // ustaw RNG na seed epizodu (CRN = te same evalSeeds dla wszystkich biegow)
        int s = GetEvalSeed(epIndex);
        rng = new System.Random(s);
        Matrix.MatrixRand.Rng = rng;
        activeSeed = s;

        // bez ewolucji – respawn tej samej populacji
        StartNewGeneration(false);
        Debug.Log($"[PopulationManager] EVAL episode {epIndex + 1}/{evalEpisodes} | evalSeed={s}");
    }

    private int GetEvalSeed(int ep)
    {
        if (evalSeeds != null && ep >= 0 && ep < evalSeeds.Length) return evalSeeds[ep];
        // fallback deterministyczny, gdy ktos skroci tablice:
        unchecked { return currentSeed * 1000 + (ep + 1); }
    }

    private void AdvanceReplicate()
    {
        Debug.Log($"[PopulationManager] End of replicate {replicateIndex} (trainSeed={currentSeed}).");

        replicateIndex++;
        if (replicateSeeds != null && replicateIndex < replicateSeeds.Length)
        {
            GA = null;                 // swiezy start
            simTimeThisGen = 0f;
            midResetDone = false;
            phase = Phase.Train;
            InitRngForCurrentReplicate();
        }
        else
        {
            experimentsFinished = true;
            phase = Phase.Done;
            Debug.Log("[PopulationManager] All replicates finished.");
            if (autoQuitOnFinish) StartCoroutine(QuitAfter(quitDelaySeconds));
        }
    }

    //Uruchom pierwsza generacje (lub kolejna, jesli GA juz istnieje)
    public void StartNewGeneration(bool evolve = true)
    {

        // 1) Inicjalizacja / ewolucja
        if (GA == null)
        {
            GA = new GeneticAlgorithm(populationSize, layerSizes, mutationRate);
        }
        else if (evolve && phase == Phase.Train)
        {
            GA.Evolve();
        }
        // else: EVAL -> brak Evolve()

        // 2) Wyczysc areny
        foreach (var arena in arenas)
            arena.ClearArena();

        // 3) Rozdziel genomy na areny i zespawnuj boty
        int arenasCount = Mathf.Max(1, arenas.Count);
        int perArena = Mathf.CeilToInt((float)GA.population.Count / arenasCount);

        int index = 0;
        foreach (var arena in arenas)
        {
            // wycinek genomow dla tej areny
            var slice = new List<Genome>(perArena);
            for (int i = 0; i < perArena && index < GA.population.Count; i++, index++)
                slice.Add(GA.population[index]);

            // Wspolne spawnowanie (AI + Random)
            arena.SpawnAllAgents(slice, randomPerArena);
        }

        // AliveCount licz tylko AI
        AliveCount = GA.population.Count;
        totalPopulationThisGen = AliveCount;

        simTimeThisGen = 0f; // reset licznika

        midResetDone = false;

        string phaseStr = (phase == Phase.Eval) ? $"EVAL ep={evalEpisodeIndex + 1}" : "TRAIN";
        Debug.Log($"[PopulationManager] {phaseStr} | Rep {ReplicateId} | Gen {Generation} start. Alive: {AliveCount} (random/arena={randomPerArena})");
    }

    //BotAgent powinien to wywolac w Die()
    public void OnAgentDied(BotAgent agent)
    {
        if (agent == null || agent.genome == null) return;

        // zapisz fitness do genomu
        agent.genome.fitness = agent.GetFitness();

        AliveCount = Mathf.Max(0, AliveCount - 1);
    }

    private void FinalizeCurrentGeneration()
    {
        // dopisz fitnessy zywych
        foreach (var arena in arenas)
        {
            var bots = arena.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                var b = bots[i];
                if (b == null) continue;
                if (!b.IsDead() && b.genome != null)
                    b.genome.fitness = b.GetFitness();
            }
        }

        if (writeStatsCsv && (csvWriteEveryNGenerations <= 1 || (Generation % csvWriteEveryNGenerations) == 0))
            AppendPerBotCsv(Generation);

        string phaseStr = (phase == Phase.Eval) ? $"EVAL ep={evalEpisodeIndex + 1}" : "TRAIN";
        Debug.Log($"[PopulationManager] {phaseStr} | Rep {ReplicateId} | Gen {Generation} finalized (timeboxed).");
    }

    //Pomocniczo: zwroc liste aren (np. dla GameManagera)
    public IReadOnlyList<ArenaManager> GetArenas() => arenas;

    //Korutyna do ladnego zakonczenia aplikacji po krotkim opoznieniu
    private IEnumerator QuitAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // zatrzymaj Play Mode
#else
    Application.Quit(0); // zamknij aplikacje
#endif
    }


    // ----------------------- CSV ------------------------
    private void AppendPerBotCsv(int generation)
    {
        try
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, statsCsvFile);
            bool writeHeader = !System.IO.File.Exists(path);

            // Zbierz 120 botow z wszystkich aren (rowniez martwych – sa w liscie)
            // i zapisz wiersz na bota.
            using (var sw = new System.IO.StreamWriter(path, append: true, System.Text.Encoding.UTF8))
            {
                if (writeHeader)
                {
                    sw.WriteLine(
                        "generation,replicate_id,bot_id,seed,fitness,time_alive,shots_fired,shots_hit,damage_taken,phase,eval_episode"
                    );
                }

                // Piszemy w stabilnej kolejnosci: po indeksie genomu, jesli mozliwe
                // Przygotuj mape: Genome -> index
                var genomeIndex = new Dictionary<Genome, int>(GA.population.Count);
                for (int i = 0; i < GA.population.Count; i++)
                    genomeIndex[GA.population[i]] = i;

                // Zbierz boty z aren
                var botsAll = new List<BotAgent>(GA.population.Count);
                foreach (var arena in arenas)
                {
                    var bots = arena.Bots;
                    for (int i = 0; i < bots.Count; i++)
                        if (bots[i] != null) botsAll.Add(bots[i]);
                }

                // Posortuj wg bot_id (indeks genomu) – ladniejszy plik
                botsAll.Sort((a, b) =>
                {
                    int ia = (a.genome != null && genomeIndex.TryGetValue(a.genome, out var xa)) ? xa : int.MaxValue;
                    int ib = (b.genome != null && genomeIndex.TryGetValue(b.genome, out var xb)) ? xb : int.MaxValue;
                    return ia.CompareTo(ib);
                });

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string phaseStr = (phase == Phase.Eval) ? "eval" : "train";
                int evalEpOut = (phase == Phase.Eval) ? (evalEpisodeIndex + 1) : 0; // 1..evalEpisodes, 0 = train

                for (int k = 0; k < botsAll.Count; k++)
                {
                    var bot = botsAll[k];
                    if (bot == null) continue;

                    int botId = (bot.genome != null && genomeIndex.TryGetValue(bot.genome, out var idx)) ? idx : k;
                    float fit = bot.genome != null ? bot.genome.fitness : 0f;

                    //Surowe metryki do liczenie E i accuracy
                    float tAlive = bot.TimeAlive;
                    int sFired = bot.ShotsFired;
                    int sHit = bot.ShotsHit;
                    float dmgT = bot.DamageTaken;

                    sw.WriteLine(
                        $"{generation}," +
                        $"{ReplicateId}," +
                        $"{botId}," +
                        $"{ActiveSeed}," +             // seed biezacej generacji (train: seed biegu, eval: seed epizodu)
                        $"{fit.ToString(inv)}," +
                        $"{tAlive.ToString(inv)}," +
                        $"{sFired.ToString(inv)}," +
                        $"{sHit.ToString(inv)}," +
                        $"{dmgT.ToString(inv)}," +
                        $"{phaseStr}," +
                        $"{evalEpOut}"
                    );
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PopulationManager] CSV per-bot write failed: {e.Message}");
        }
    }

}
