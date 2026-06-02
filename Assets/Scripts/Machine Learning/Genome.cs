using System;

[Serializable]
public class Genome
{
    public NeuralNetwork network;
    public float fitness;

    public Genome(int[] layerSizes)
    {
        network = new NeuralNetwork(layerSizes);
        fitness = 0f;
    }

    public Genome(NeuralNetwork network)
    {
        this.network = network;
        fitness = 0f;
    }

    public Genome Copy()
    {
        return new Genome(network.Copy()) { fitness = this.fitness };
    }

    public void Mutate(float mutationRate)
    {
        network.Mutate(mutationRate);
    }

    // NOWE: crossover z parametrem miksowania (0..1)
    public static Genome Crossover(Genome parent1, Genome parent2, float mixChance = 0.0f)
    {
        var childNetwork = NeuralNetwork.Crossover(parent1.network, parent2.network, mixChance);
        return new Genome(childNetwork);
    }

    // stare
    //public static Genome Crossover(Genome parent1, Genome parent2)
    //{
    //    var childNetwork = NeuralNetwork.Crossover(parent1.network, parent2.network);
    //    return new Genome(childNetwork);
    //}
}
