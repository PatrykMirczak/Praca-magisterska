using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class NeuralNetwork
{
    public int[] layers;
    public Matrix[] weights;
    public Matrix[] biases;

    public NeuralNetwork(int[] layers)
    {
        this.layers = (int[])layers.Clone();
        weights = new Matrix[layers.Length - 1];
        biases = new Matrix[layers.Length - 1];

        // Xavier/Glorot normal – dobra baza pod sigmoid
        for (int i = 0; i < weights.Length; i++)
        {
            int fanIn = layers[i];
            int fanOut = layers[i + 1];

            // Var = (przyblizenie) 2/(fanIn+fanOut)  -> std:
            float std = Mathf.Sqrt(2f / (fanIn + fanOut));

            weights[i] = Matrix.RandomNormal(fanOut, fanIn, 0f, std);
            biases[i] = Matrix.RandomNormal(fanOut, 1, 0f, 0.01f); // malutkie biasy
        }

        //for (int i = 0; i < weights.Length; i++)
        //{
        //    weights[i] = Matrix.Random(layers[i + 1], layers[i]);
        //    biases[i] = Matrix.Random(layers[i + 1], 1);
        //}
    }

    public float[] FeedForward(float[] inputArray)
    {
        Matrix input = Matrix.FromArray(inputArray);
        Matrix output = input;

        for (int i = 0; i < weights.Length; i++)
        {
            output = Matrix.Multiply(weights[i], output);
            output = Matrix.Add(output, biases[i]);
            //output.ApplyActivation(x => (float)Math.Tanh(x));
            output.ApplyActivation(x => 1f / (1f + (float)Math.Exp(-x))); // sigmoid

        }

        return output.ToArray();

    }

    public NeuralNetwork Copy()
    {
        NeuralNetwork clone = new NeuralNetwork(layers);
        for (int i = 0; i < weights.Length; i++)
        {
            clone.weights[i] = weights[i].Copy();
            clone.biases[i] = biases[i].Copy();
        }
        return clone;
    }


    //Stare
    //public void Mutate(float mutationRate)
    //{
    //    foreach (var w in weights)
    //        w.Mutate(mutationRate);
    //    foreach (var b in biases)
    //        b.Mutate(mutationRate);
    //}

    public void Mutate(float mutationRate)
    {
        // wagi zmieniane smielej niz biasy
        foreach (var w in weights) w.Mutate(mutationRate, 0.08f);
        foreach (var b in biases) b.Mutate(mutationRate, 0.02f);
    }






    // KLASYCZNE KRZYZOWANIE - uzywane
    public static NeuralNetwork Crossover(NeuralNetwork a, NeuralNetwork b, float mixChance = 0.0f)
    {
        NeuralNetwork child = new NeuralNetwork(a.layers);
        for (int i = 0; i < a.weights.Length; i++)
        {
            // A/B + opcjonalna domieszka wartosci posrednich (mixChance w [0..1])
            child.weights[i] = Matrix.Crossover(a.weights[i], b.weights[i], mixChance);
            child.biases[i] = Matrix.Crossover(a.biases[i], b.biases[i], mixChance);

            // --- Wariant BLX-a (eksperymentalnie) — ZAKOMENTOWANY ---
            // child.weights[i] = Matrix.Blend(a.weights[i], b.weights[i], 0.25f);
            // child.biases[i]  = Matrix.Blend(a.biases[i],  b.biases[i],  0.10f);
        }
        return child;
    }




    //BLX - mozliwe ze nawet tego nie uzyje
    //public static NeuralNetwork Crossover(NeuralNetwork a, NeuralNetwork b)
    //{
    //    NeuralNetwork child = new NeuralNetwork(a.layers);
    //    for (int i = 0; i < a.weights.Length; i++)
    //    {
    //        //25% szansy na wybranie wagi pomiedzy rodzicami
    //        //child.weights[i] = Matrix.Blend(a.weights[i], b.weights[i], 0.25f);
    //        //child.biases[i] = Matrix.Blend(a.biases[i], b.biases[i], 0.10f);

    //        //0% szansy na wybranie wagi pomiedzy rodzicami - ustawienie bazowe
    //        child.weights[i] = Matrix.Blend(a.weights[i], b.weights[i], 0.25f);
    //        child.biases[i] = Matrix.Blend(a.biases[i], b.biases[i], 0.10f);
    //    }
    //    return child;
    //}

}
