using System;
using UnityEngine;

[Serializable]
public class Matrix
{
    public int rows;
    public int cols;
    public float[,] data;

    public Matrix(int rows, int cols)
    {
        this.rows = rows;
        this.cols = cols;
        data = new float[rows, cols];
    }

    // Tworzenie macierzy z jednowymiarowej tablicy (kolumna)
    public static Matrix FromArray(float[] array)
    {
        Matrix result = new Matrix(array.Length, 1);
        for (int i = 0; i < array.Length; i++)
            result.data[i, 0] = array[i];
        return result;
    }

    // Zwracanie danych z macierzy jako jednowymiarowa tablica (z kolumny)
    public float[] ToArray()
    {
        float[] result = new float[rows];
        for (int i = 0; i < rows; i++)
            result[i] = data[i, 0];
        return result;
    }

    //Podejscie 3 
    public static class MatrixRand
    {
        // Wspolny RNG, by uniknac powtarzalnosci przy wielu new Random() w jednej klatce
        public static System.Random Rng = new System.Random();

        // Box–Muller -> N(0,1)
        public static double Gauss01()
        {
            double u1 = 1.0 - Rng.NextDouble(); // (0,1]
            double u2 = 1.0 - Rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // POMOCNICZE:
        public static float NextFloat() => (float)Rng.NextDouble(); // [0,1)
        public static float RangeFloat(float min, float max) => min + (float)Rng.NextDouble() * (max - min); // [min,max)
        public static int RangeInt(int min, int max) => Rng.Next(min, max); // [min,max)
        public static bool Chance(float p) => Rng.NextDouble() < p;
    }

    public static Matrix RandomNormal(int rows, int cols, float mean, float std)
    {
        Matrix m = new Matrix(rows, cols);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                m.data[i, j] = mean + (float)MatrixRand.Gauss01() * std;
        return m;
    }


    // Dodanie dwoch macierze (element po elemencie)
    public static Matrix Add(Matrix a, Matrix b)
    {
        if (a.rows != b.rows || a.cols != b.cols)
            throw new ArgumentException("Macierze musza miec takie same wymiary, aby je dodac.");

        Matrix result = new Matrix(a.rows, a.cols);
        for (int i = 0; i < a.rows; i++)
            for (int j = 0; j < a.cols; j++)
                result.data[i, j] = a.data[i, j] + b.data[i, j];
        return result;
    }

    // Mnozenie macierzy
    public static Matrix Multiply(Matrix a, Matrix b)
    {
        if (a.cols != b.rows)
            throw new ArgumentException("Liczba kolumn pierwszej macierzy musi byc rowna liczbie wierszy drugiej.");

        Matrix result = new Matrix(a.rows, b.cols);
        for (int i = 0; i < result.rows; i++)
        {
            for (int j = 0; j < result.cols; j++)
            {
                float sum = 0f;
                for (int k = 0; k < a.cols; k++)
                    sum += a.data[i, k] * b.data[k, j];
                result.data[i, j] = sum;
            }
        }
        return result;
    }

    // Zastosowanie funkcji aktywacji do kazdego elementu
    public void ApplyActivation(Func<float, float> activation)
    {
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                data[i, j] = activation(data[i, j]);
    }


    // Tworzenie kopii macierzy
    public Matrix Copy()
    {
        Matrix result = new Matrix(rows, cols);
        Array.Copy(data, result.data, data.Length);
        return result;
    }

    // Mutacja elementow z podanym prawdopodobienstwem
    // Zastapic duze skoki mutacja w postaci szumu Gaussowskiego
    //public void Mutate(float mutationRate)
    //{
    //    for (int i = 0; i < rows; i++)
    //        for (int j = 0; j < cols; j++)
    //            if (UnityEngine.Random.value < mutationRate)
    //                data[i, j] += UnityEngine.Random.Range(-0.5f, 0.5f);
    //}
    

    // Czestsze, mniejsze perturbacje
    public void Mutate(float mutationRate, float sigma)
    {
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                if (MatrixRand.Chance(mutationRate))
                    data[i, j] += (float)MatrixRand.Gauss01() * sigma;
    }

    //Podejscie 2 - mutacja jako szum Gaussowski
    //public void Mutate(float mutationRate, float sigma = 0.1f, float clampAbs = 5f)
    //{
    //    for (int i = 0; i < rows; i++)
    //    {
    //        for (int j = 0; j < cols; j++)
    //        {
    //            if (UnityEngine.Random.value < mutationRate)
    //            {
    //                // Box–Muller: losowanie N(0,1)
    //                float u1 = 1f - UnityEngine.Random.value; // unikamy log(0)
    //                float u2 = 1f - UnityEngine.Random.value;
    //                float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);

    //                // skalujemy przez sigma
    //                data[i, j] += z * sigma;

    //                // ograniczamy do przedzialu [-clampAbs, clampAbs], zeby wagi nie "uciekly"
    //                data[i, j] = Mathf.Clamp(data[i, j], -clampAbs, clampAbs);
    //            }
    //        }
    //    }
    //}


    public static Matrix Crossover(Matrix a, Matrix b, float blendChance = 0.0f)
    {
        if (a.rows != b.rows || a.cols != b.cols)
            throw new ArgumentException("Macierze musza miec te same wymiary do krzyzowania.");

        Matrix result = new Matrix(a.rows, a.cols);
        for (int i = 0; i < a.rows; i++)
        {
            for (int j = 0; j < a.cols; j++)
            {
                float r = MatrixRand.NextFloat();

                if (r < blendChance)
                {
                    // interpolacja liniowa pomiedzy a i b (wartosc "pomiedzy")
                    float t = MatrixRand.RangeFloat(0f, 1f);
                    result.data[i, j] = Mathf.Lerp(a.data[i, j], b.data[i, j], t);
                }
                else
                {
                    // czysty wybor genu A/B
                    result.data[i, j] = (MatrixRand.NextFloat() < 0.5f) ? a.data[i, j] : b.data[i, j];
                }
            }
        }
        return result;
    }


    // DO BLX MOZE SIE NIE PRZYDAC, wybralem klasyczne krzyzowanie z mozliwoscia blend
    //public static Matrix Crossover(Matrix a, Matrix b, float blendChance = 0.0f)
    //{
    //    if (a.rows != b.rows || a.cols != b.cols)
    //        throw new ArgumentException("Macierze musza miec te same wymiary do krzyzowania.");

    //    Matrix result = new Matrix(a.rows, a.cols);
    //    for (int i = 0; i < a.rows; i++)
    //    {
    //        for (int j = 0; j < a.cols; j++)
    //        {
    //            float r = MatrixRand.NextFloat();

    //            if (r < blendChance)
    //            {
    //                // interpolacja liniowa pomiedzy a i b
    //                float t = MatrixRand.RangeFloat(0f, 1f);
    //                result.data[i, j] = Mathf.Lerp(a.data[i, j], b.data[i, j], t);
    //            }
    //            else
    //            {
    //                // klasyczny wybor genu od jednego z rodzicow
    //                result.data[i, j] = (MatrixRand.NextFloat() < 0.5f) ? a.data[i, j] : b.data[i, j];
    //            }
    //        }
    //    }
    //    return result;
    //}


    //Testowanie blend - nie uzywane - zostawione na przyszlosc
    public static Matrix Blend(Matrix a, Matrix b, float alpha = 0.25f)
    {
        if (a.rows != b.rows || a.cols != b.cols)
            throw new ArgumentException("Dims mismatch");
        Matrix c = new Matrix(a.rows, a.cols);
        for (int i = 0; i < a.rows; i++)
            for (int j = 0; j < a.cols; j++)
            {
                float x = a.data[i, j];
                float y = b.data[i, j];
                float lo = Math.Min(x, y);
                float hi = Math.Max(x, y);
                float range = hi - lo;
                float min = lo - alpha * range;
                float max = hi + alpha * range;
                c.data[i, j] = MatrixRand.RangeFloat(min, max);
            }
        return c;
    }

    // Transponowanie macierzy
    public Matrix Transpose()
    {
        Matrix result = new Matrix(cols, rows);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                result.data[j, i] = data[i, j];
        return result;
    }

    //public NeuralNetwork NeuralNetwork
    //{
    //    get => default;
    //    set
    //    {
    //    }
    //}
}
