using System;
using System.Collections.Generic;
public class GeneticAlgorithm
{
    public List<Genome> population;
    public int generation;
    public float mutationRate;
    public int[] layerSizes;

    // Tworzenie nowej populacji genomow o zadany rozmiarze i strukturze
    public GeneticAlgorithm(int populationSize, int[] layerSizes, float mutationRate)
    {
        this.layerSizes = layerSizes;
        this.mutationRate = mutationRate;
        this.generation = 0;

        population = new List<Genome>();
        for (int i = 0; i < populationSize; i++)
        {
            population.Add(new Genome(layerSizes));
        }
    }

    //stara wersja 20.09.2025
    //Selekcja rodzica metoda turniejowa - wybieramy k losowych genomow i zwracamy najlepszego
    //private Genome SelectParentTournament(int k = 3)
    //{
    //    Genome best = null;
    //    for (int i = 0; i < k; i++)
    //    {
    //        var cand = population[Matrix.MatrixRand.RangeInt(0, population.Count)];
    //        if (best == null || cand.fitness > best.fitness) best = cand;
    //    }
    //    return best;
    //}

    // Selekcja rodzica metoda turniejowa - wybieramy k losowych genomow i zwracamy najlepszego
    private Genome SelectParentTournament(int k = 3)
    {
        k = Math.Max(2, Math.Min(k, population.Count));
        int bestIdx = -1;
        float bestFit = float.NegativeInfinity;

        // losujemy k roznych indeksow
        // (prosto: losuj do skutku, bo k << N; dla duzych k zrob mini-tasowanie)
        var used = new HashSet<int>();
        for (int t = 0; t < k; t++)
        {
            int idx;
            do { idx = Matrix.MatrixRand.RangeInt(0, population.Count); }
            while (!used.Add(idx));

            float f = population[idx].fitness;
            if (f > bestFit) { bestFit = f; bestIdx = idx; }
        }
        return population[bestIdx];
    }


    // Obliczanie fitnessu kazdego genomu w populacji
    public void EvaluateFitness(Func<Genome, float> fitnessFunction)
    {
        foreach (var genome in population)
        {
            genome.fitness = fitnessFunction(genome);
        }
    }

    //Kolejna generacja  - kopiowanie rodzica oraz mutacja
    //public void Evolve()
    //{
    //    generation++;
    //    UnityEngine.Debug.Log($"Ewolucja: generacja {generation}");

    //    List<Genome> newPopulation = new List<Genome>();

    //    // Sortujemy po fitness malejaco
    //    population = population.OrderByDescending(g => g.fitness).ToList();

    //    // Elita – bez mutacji
    //    int eliteCount = (int)(population.Count * 0.1f); // 10% najlepszych
    //    for (int i = 0; i < eliteCount; i++)
    //    {
    //        newPopulation.Add(population[i].Copy());
    //    }

    //    // Reszta populacji – wybierani rodzice, kopiowani z mutacja
    //    while (newPopulation.Count < population.Count)
    //    {
    //        Genome parent = SelectParent();
    //        Genome child = parent.Copy();
    //        child.Mutate(mutationRate);
    //        newPopulation.Add(child);
    //    }

    //    population = newPopulation;
    //}



    //Kolejna generacja - crossOver i mutacja
    //public void Evolve()
    //{
    //    generation++;
    //    UnityEngine.Debug.Log($"Ewolucja: generacja {generation}");

    //    List<Genome> newPopulation = new List<Genome>();

    //    // Sortowanie po fitnessie malejaco
    //    population = population.OrderByDescending(g => g.fitness).ToList();

    //    // Zachowanie niewielkiej grupy najlepszych osobnikow bez zadnych zmian
    //    int eliteCount = (int)(population.Count * 0.1f); // 10% elity
    //    for (int i = 0; i < eliteCount; i++)
    //        newPopulation.Add(population[i].Copy());

    //    // Tworzenie potomkow przez krzyzowanie
    //    while (newPopulation.Count < population.Count)
    //    {
    //        Genome parent1 = SelectParent();
    //        Genome parent2 = SelectParent();

    //        Genome child = Genome.Crossover(parent1, parent2);
    //        child.Mutate(mutationRate);

    //        newPopulation.Add(child);
    //    }

    //    population = newPopulation;
    //}

    //Ruletka bezpieczna - fallback i bez LINQ - prawdopodobienstwo wyboru jest proporcjonalne do fitnessu
    private Genome SelectParentRouletteSafe()
    {
        float total = 0f;
        for (int i = 0; i < population.Count; i++)
            total += population[i].fitness;

        // fallback: gdy brak informacji rozrozniajacej – wybierz jednostajnie
        if (total <= 0f)
        {
            int idx = Matrix.MatrixRand.RangeInt(0, population.Count);
            return population[idx];
        }

        float r = Matrix.MatrixRand.RangeFloat(0f, total);
        float acc = 0f;
        for (int i = 0; i < population.Count; i++)
        {
            acc += population[i].fitness;
            if (acc >= r) return population[i];
        }
        return population[population.Count - 1]; // asekuracyjnie
    }



    //Kolejna generacja - crossOver i mutacja - wraz z selekcja turniejowa
    public void Evolve()
    {
        generation++;
        UnityEngine.Debug.Log($"Ewolucja: generacja {generation}");

        // sort i elita 
        //population = population.OrderByDescending(g => g.fitness).ToList();
        population.Sort((a, b) => b.fitness.CompareTo(a.fitness));

        List<Genome> newPopulation = new List<Genome>(population.Count);

        int eliteCount = Math.Max(1, (int)(population.Count * 0.10f)); // 5% elity - jest domyslnie (w pracy napisac ze 10)
        for (int i = 0; i < eliteCount; i++)
            newPopulation.Add(population[i].Copy());

        // 0% elity – brak niezmienianych kopii
        //int eliteCount = 0;
        // (petla kopiowania elity nic nie doda, bo eliteCount == 0)

        //reszta – turniej
        while (newPopulation.Count < population.Count)
        {
            var p1 = SelectParentTournament(3);
            var p2 = SelectParentTournament(3);
            var child = Genome.Crossover(p1, p2, 0.0f);    // 0% szans na mieszanie wag domyslnie
            child.Mutate(mutationRate);
            newPopulation.Add(child);
        }

        // 2) reszta – ROULETTE (zamiast turnieju)
        //while (newPopulation.Count < population.Count)
        //{
        //    var p1 = SelectParentRouletteSafe();
        //    var p2 = SelectParentRouletteSafe();

        //    var child = Genome.Crossover(p1, p2, 0.0f); // mixChance zostaje na 0%
        //    child.Mutate(mutationRate);
        //    newPopulation.Add(child);
        //}

        // 2% swiezej krwi (zawsze kilka zupelnie nowych)
        //int inject = Math.Max(1, population.Count / 50);
        //for (int i = 0; i < inject; i++)
        //    newPopulation[newPopulation.Count - 1 - i] = new Genome(layerSizes);
        int inject = Math.Max(1, population.Count / 50);
        inject = Math.Min(inject, newPopulation.Count - eliteCount);
        for (int i = 0; i < inject; i++)
        {
            //newPopulation[newPopulation.Count - 1 - i] = new Genome(layerSizes);
            int idx = Matrix.MatrixRand.RangeInt(eliteCount, newPopulation.Count);
            newPopulation[idx] = new Genome(layerSizes);
        }



        population = newPopulation;
    }

    // Wybieranie rodzica metoda ruletkowa — prawdopodobienstwo wyboru jest proporcjonalne do fitnessu.
    //private Genome SelectParent()
    //{
    //    // Proporcjonalna selekcja ruletkowa
    //    float totalFitness = population.Sum(g => g.fitness);
    //    float random = Matrix.MatrixRand.RangeFloat(0f, totalFitness);
    //    float runningSum = 0f;

    //    foreach (var genome in population)
    //    {
    //        runningSum += genome.fitness;
    //        if (runningSum >= random)
    //            return genome;
    //    }

    //    return population[0]; // wartosc domyslna w przypadku braku wyboru
    //}

    // Genom o najwyzszym fitnessie w obecnej populacji.
    public Genome GetBestGenome()
    {
        if (population == null || population.Count == 0) return null;
        var best = population[0];
        for (int i = 1; i < population.Count; i++)
            if (population[i].fitness > best.fitness) best = population[i];
        return best;
    }

    //public Genome GetBestGenome()
    //{
    //    return population.OrderByDescending(g => g.fitness).First();
    //}

}
