using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    [Header("Symulacja")]
    [SerializeField] private float simulationStep = 0.02f; // 50 Hz
    [SerializeField] private int simulationSpeed = 1;      // np. 5 = 5x szybciej

    [Header("UI (opcjonalnie)")]
    [SerializeField] private Text speedLabel;

    private PopulationManager pop;
    private IReadOnlyList<ArenaManager> arenas;

    private int pendingSpeedDelta = 0;

    private void Awake()
    {
        pop = PopulationManager.Instance;
        if (pop == null)
        {
            pop = Object.FindFirstObjectByType<PopulationManager>();
        }
    }

    private void Start()
    {
        arenas = pop.GetArenas();
        pop.StartNewGeneration();

        UpdateSpeedLabel();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return; // asekuracja (np. brak klawiatury)

        // Z = szybciej, X = wolniej
        if (kb.zKey.wasPressedThisFrame) pendingSpeedDelta++;
        if (kb.xKey.wasPressedThisFrame) pendingSpeedDelta--;
    }


    private void FixedUpdate()
    {
        if (pendingSpeedDelta != 0)
        {
            simulationSpeed = Mathf.Max(0, simulationSpeed + pendingSpeedDelta);
            pendingSpeedDelta = 0;
            UpdateSpeedLabel();
        }

        if (simulationSpeed <= 0) return;      // PAUZA

        for (int i = 0; i < simulationSpeed; i++)
        {
            StepSimulation(simulationStep);
            pop.Tick(simulationStep);
        }
    }

    //Stare - przed przyspieszanianiem z klawiatury i GUI
    //private void FixedUpdate()
    //{
    //    // Petla przyspieszenia
    //    for (int i = 0; i < simulationSpeed; i++)
    //    {
    //        StepSimulation(simulationStep);
    //        pop.Tick(simulationStep); // liczenie "czasu symulacji" – niezaleznie od przyspieszenia
    //    }
    //}

    private void StepSimulation(float dt)
    {
        if (arenas == null) return;

        // 1) Symuluj areny (pociski, timery itd.)
        for (int a = 0; a < arenas.Count; a++)
        {
            arenas[a].Elapse(dt);
        }
    }

    public void SetSimulationSpeed(int speed)
    {
        //simulationSpeed = Mathf.Max(1, speed);    stara wersja - bez pauzy
        simulationSpeed = Mathf.Max(0, speed);
        UpdateSpeedLabel();
    }

    public void IncreaseSpeed(int delta = 1) => SetSimulationSpeed(simulationSpeed + delta);
    public void DecreaseSpeed(int delta = 1) => SetSimulationSpeed(simulationSpeed - delta);

    private void UpdateSpeedLabel()
    {
        if (speedLabel != null)
            speedLabel.text = simulationSpeed == 0 ? "PAUZA" : $"Speed: x{simulationSpeed}";
    }

}
