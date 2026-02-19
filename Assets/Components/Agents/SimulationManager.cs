using Antymology.Terrain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antymology.Agents
{
    /// <summary>
    /// The central orchestrator for the Antymology simulation.
    /// Responsibilities:
    /// 1. Manages the "Game Loop" (ticks) independent of Unity's frame rate.
    /// 2. Implements the Genetic Algorithm (GA) to evolve ant behavior over generations.
    /// 3. Manages the lifecycle of Episodes (spawning, resetting, scoring).
    /// 4. Controls global pheromone diffusion and decay updates.
    /// </summary>
    public class SimulationManager : Singleton<SimulationManager>
    {
        [Header("Scene")]
        public GameObject AntPrefab;

        [Header("Episode (simulation) settings")]
        [Tooltip("Number of ants spawned per episode (1 queen + workers).")]
        public int PopulationSize = 20;

        [Tooltip("Pheromone decay multiplier applied each tick (e.g., 0.8 means -20% per tick).")]
        [Range(0.0f, 1.0f)]
        public float DecayRate = 0.8f;
        public float DiffusionRate = 0.25f;
        public int DiffusionIterations = 1;
        public float PheromoneEpsilon = 0.05f;

        [Tooltip("Speed multiplier for the simulation.")]
        public float TimeScale = 1.0f;

        [Tooltip("Max ticks to run per episode (evaluation run).")]
        public int MaxTicksPerGeneration = 1000;

        [Header("Genetic Algorithm (GA) settings")]
        [Tooltip("How many genomes exist in a GA generation.")]
        public int GenomePopulationSize = 30;

        [Tooltip("How many episodes (with different random seeds) to average per genome.")]
        public int EpisodesPerGenome = 3;

        [Tooltip("Genome length. Must match Ant.cs' expected indexing.")]
        private int GenomeLength = 90;

        [Tooltip("How many top genomes to copy unchanged into next generation.")]
        public int Elitism = 2;

        [Tooltip("Tournament selection size.")]
        public int TournamentSize = 3;

        [Tooltip("Probability per gene to mutate.")]
        [Range(0.0f, 1.0f)]
        public float MutationRate = 0.05f;

        [Tooltip("Std-dev for gaussian mutation noise (in gene units).")]
        public float MutationStdDev = 0.15f;

        [Tooltip("Probability that crossover happens (otherwise child clones a parent).")]
        [Range(0.0f, 1.0f)]
        public float CrossoverRate = 0.8f;

        [Tooltip("Base seed used to make evaluations repeatable.")]
        public int RandomSeedBase = 12345;

        // ---- Simulation state ----
        private readonly List<Ant> _ants = new List<Ant>();
        private readonly List<AirBlock> _pheromoneBlocks = new List<AirBlock>();

        private int _currentTick = 0;
        private int _gaGeneration = 1;

        // Episode metrics
        private int _nestsBuiltThisEpisode = 0;
        private bool _queenAlive = true;
        private int _queenHealed = 0;
        private int _queenServivedTicks = 0;
        private int _workerDeathCount = 0;

        // Timing
        private float _timer = 0f;
        private const float TICK_DELAY = 1f / 60f;


        // ---- GA state ----
        /// <summary>
        /// Represents a single candidate solution (Genome) and its performance metrics.
        /// </summary>
        [Serializable]
        private class Individual
        {
            public float[] genome;
            public float fitnessSum;
            public int fitnessSamples;

            public float FitnessAvg => fitnessSamples > 0 ? (fitnessSum / fitnessSamples) : float.NegativeInfinity;

            public Individual(int length)
            {
                genome = new float[length];
            }

            public Individual(float[] g)
            {
                genome = (float[])g.Clone();
            }
        }

        private readonly List<Individual> _population = new List<Individual>();
        private int _currentIndividualIndex = 0;
        private int _currentEpisodeRepeat = 0;

        void Start()
        {
            InitPopulationRandom();
            BeginEpisodeForCurrentIndividual();
        }

        void Update()
        {
            Time.timeScale = TimeScale;

            _timer += Time.deltaTime;

            while (_timer >= TICK_DELAY)
            {
                _timer -= TICK_DELAY;

                // Run one tick
                if (_currentTick < MaxTicksPerGeneration && _ants.Count > 0)
                {
                    // Global Pheromone System
                    if (PheromoneField.Instance != null)
                    {
                        PheromoneField.Instance.DiffusionRate = DiffusionRate;
                        PheromoneField.Instance.DiffusionIterations = DiffusionIterations;
                        PheromoneField.Instance.Epsilon = PheromoneEpsilon;
                        PheromoneField.Instance.Step(DecayRate);
                    }

                    for (int i = _ants.Count - 1; i >= 0; i--)
                    {
                        if (_ants[i] != null) _ants[i].OnTick();
                    }

                    _currentTick++;
                }
                else
                {
                    EndEpisode();
                    // prevent running multiple episodes in same frame
                    _timer = 0f;
                    break;
                }
            }
        }

        // ------------------- GA -------------------

        /// <summary>
        /// Seeds the initial population with purely random genomes [-1, 1].
        /// </summary>
        private void InitPopulationRandom()
        {
            _population.Clear();

            for (int i = 0; i < GenomePopulationSize; i++)
            {
                var ind = new Individual(GenomeLength);
                for (int g = 0; g < GenomeLength; g++)
                {
                    // genes in [-1, 1]
                    ind.genome[g] = UnityEngine.Random.Range(-1f, 1f);
                }
                _population.Add(ind);
            }

            _currentIndividualIndex = 0;
            _currentEpisodeRepeat = 0;
            _gaGeneration = 1;

            Debug.Log($"GA: Initialized population of {GenomePopulationSize} genomes (length {GenomeLength}).");
        }

        /// <summary>
        /// Performs selection, crossover, and mutation to create the next generation.
        /// Called when all individuals in the current generation have been evaluated.
        /// </summary>
        private void EvolvePopulation()
        {
            // Sort by average fitness
            _population.Sort((a, b) => b.FitnessAvg.CompareTo(a.FitnessAvg));

            float best = _population[0].FitnessAvg;
            float median = _population[_population.Count / 2].FitnessAvg;

            Debug.Log($"<color=cyan>GA Generation {_gaGeneration} completed.</color> Best fitness={best:F1}, median={median:F1}");

            var next = new List<Individual>(_population.Count);

            // Elitism, Keep the best performers unchanged
            int eliteCount = Mathf.Clamp(Elitism, 0, _population.Count);
            for (int i = 0; i < eliteCount; i++)
            {
                next.Add(new Individual(_population[i].genome));
            }

            // Reproduction: Fill the rest of the population
            while (next.Count < _population.Count)
            {
                // Tournament Selection
                var p1 = TournamentSelect();
                var p2 = TournamentSelect();

                // Crossover
                float[] child = (UnityEngine.Random.value < CrossoverRate)
                    ? UniformCrossover(p1.genome, p2.genome)
                    : (float[])p1.genome.Clone();

                // Mutation
                MutateInPlace(child);

                next.Add(new Individual(child));
            }

            // Replace Population
            _population.Clear();
            _population.AddRange(next);

            // Reset fitness accumulators
            foreach (var ind in _population)
            {
                ind.fitnessSum = 0f;
                ind.fitnessSamples = 0;
            }

            _gaGeneration++;
            _currentIndividualIndex = 0;
            _currentEpisodeRepeat = 0;
        }

        /// <summary>
        /// Selects the best individual from a random subset of the population.
        /// </summary>
        private Individual TournamentSelect()
        {
            int n = Mathf.Clamp(TournamentSize, 2, _population.Count);
            Individual best = null;

            for (int i = 0; i < n; i++)
            {
                var cand = _population[UnityEngine.Random.Range(0, _population.Count)];
                if (best == null || cand.FitnessAvg > best.FitnessAvg) best = cand;
            }

            return best;
        }

        /// <summary>
        /// Creates a child genome by picking genes randomly from parent A or B (50/50).
        /// </summary>
        private float[] UniformCrossover(float[] a, float[] b)
        {
            float[] c = new float[a.Length];
            for (int i = 0; i < c.Length; i++)
            {
                c[i] = (UnityEngine.Random.value < 0.5f) ? a[i] : b[i];
            }
            return c;
        }

        /// <summary>
        /// Applies Gaussian noise to genes based on MutationRate.
        /// </summary>
        private void MutateInPlace(float[] g)
        {
            for (int i = 0; i < g.Length; i++)
            {
                if (UnityEngine.Random.value < MutationRate)
                {
                    float noise = NextGaussian(0f, MutationStdDev);
                    g[i] = Mathf.Clamp(g[i] + noise, -1f, 1f);
                }
            }
        }

        /// <summary>
        /// Generates a number from a normal distribution using the Box-Muller transform.
        /// </summary>
        private float NextGaussian(float mean, float stdDev)
        {
            float u1 = Mathf.Clamp01(UnityEngine.Random.value);
            float u2 = Mathf.Clamp01(UnityEngine.Random.value);
            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        // ------------------- Episode lifecycle -------------------

        /// <summary>
        /// Prepares the simulation for the current individual in the list.
        /// Sets a deterministic RNG seed so the same genome produces the same result on replay.
        /// </summary>
        private void BeginEpisodeForCurrentIndividual()
        {
            _currentTick = 0;
            ResetFitness();

            if (WorldManager.Instance != null)
            {
                WorldManager.Instance.ResetWorld();
            }

            int seed = RandomSeedBase
                + (_gaGeneration * 100000)
                + (_currentIndividualIndex * 100)
                + _currentEpisodeRepeat;

            UnityEngine.Random.InitState(seed);

            SpawnEpisode(_population[_currentIndividualIndex].genome);

            Debug.Log($"Episode: GAgen={_gaGeneration} genome={_currentIndividualIndex + 1}/{_population.Count} repeat={_currentEpisodeRepeat + 1}/{EpisodesPerGenome} seed={seed}");
        }

        /// <summary>
        /// Cleans up old agents and instantiates the new population.
        /// </summary>
        private void SpawnEpisode(float[] genome)
        {
            foreach (var ant in _ants) if (ant != null) Destroy(ant.gameObject);
            _ants.Clear();
            if (PheromoneField.Instance != null) PheromoneField.Instance.ClearAll();
            // ClearPheromone();

            for (int i = 0; i < PopulationSize; i++)
            {
                GameObject antObj = Instantiate(AntPrefab);

                Ant ant = antObj.GetComponent<Ant>();
                if (ant == null) ant = antObj.AddComponent<Ant>();

                int x = UnityEngine.Random.Range(10, 100);
                int z = UnityEngine.Random.Range(10, 100);
                int y = FindSurfaceY(x, z) + 1;
                antObj.transform.position = new Vector3(x, y, z);

                bool isQueen = (i == 0);
                ant.Init(isQueen, genome);
                _ants.Add(ant);
            }
        }

        /// <summary>
        /// Concludes the current episode, calculates fitness, and advances the GA state machine.
        /// </summary>
        private void EndEpisode()
        {
            // Calculate Fitness
            float workerServivePerpercentage = _workerDeathCount / PopulationSize;
            // Weighted fitness formula: Nests > Survival > Healing
            float fitness = (_nestsBuiltThisEpisode * 100f) + (_queenAlive ? 500f : 0f) + (workerServivePerpercentage*1000f) + (_queenHealed * 100f) ;
            Debug.Log($"Fitness:{fitness} || Nests Built: {_nestsBuiltThisEpisode * 1f} Queen Alive: {(_queenAlive ? 500f : 0f)} Worker Alive: {PopulationSize - _workerDeathCount} Worker Healed: {_queenHealed * 100f}");

            // Record stats
            var ind = _population[_currentIndividualIndex];
            ind.fitnessSum += fitness;
            ind.fitnessSamples++;

            // Cleanup
            foreach (var ant in _ants) if (ant != null) Destroy(ant.gameObject);
            _ants.Clear();
            if (PheromoneField.Instance != null) PheromoneField.Instance.ClearAll();

            // Advance GA State
            _currentEpisodeRepeat++;
            if (_currentEpisodeRepeat >= EpisodesPerGenome)
            {
                _currentEpisodeRepeat = 0;
                _currentIndividualIndex++;

                if (_currentIndividualIndex >= _population.Count)
                {
                    EvolvePopulation();
                }
            }

            BeginEpisodeForCurrentIndividual();
        }

        // ------------------- Pheromones -------------------

        private void EnsurePheromoneField()
        {
            if (PheromoneField.Instance != null) return;
            var go = new GameObject("PheromoneField");
            go.AddComponent<PheromoneField>();
        }

        private void DecayPheromone()
        {
            // for (int i = _pheromoneBlocks.Count - 1; i >= 0; i--)
            // {
            //     if (_pheromoneBlocks[i] != null)
            //     {
            //         _pheromoneBlocks[i].Decay(DecayRate);
            //     }
            // }
        }

        private void ClearPheromone()
        {
            // foreach (var airBlock in _pheromoneBlocks)
            // {
            //     airBlock?.Clear();
            // }
            // _pheromoneBlocks.Clear();
        }

        // ------------------- Fitness -------------------

        private void ResetFitness()
        {
            _nestsBuiltThisEpisode = 0;
            _queenAlive = true;
            _queenHealed = 0;
            _queenServivedTicks = 0;
            _workerDeathCount = 0;
        }

        public void ReportNestBuilt()
        {
            _nestsBuiltThisEpisode++;
        }

        public void ReportHealQueen()
        {
            _queenHealed++;
        }

        public void ReportWorkerDie()
        {
            _workerDeathCount++;
        }

        public void ReportQueenDie()
        {
            _queenAlive = false;
            _queenServivedTicks = _currentTick;
        }

        public void ReportPheromoneDeposit(AirBlock airBlock)
        {
            // if (airBlock != null && !_pheromoneBlocks.Contains(airBlock)) _pheromoneBlocks.Add(airBlock);
        }

        public void RemovePheromoneBlock(AirBlock airBlock)
        {
            // if (airBlock != null) _pheromoneBlocks.Remove(airBlock);
        }

        public void RemoveAnt(Ant ant)
        {
            _ants.Remove(ant);
        }

        // ------------------- Helpers -------------------

        private int FindSurfaceY(int x, int z)
        {
            for (int y = 20; y >= 0; y--)
                if (!(WorldManager.Instance.GetBlock(x, y, z) is AirBlock)) return y;
            return 0;
        }

        public Ant GetAntAt(Vector3 position)
        {
            for (int i = 0; i < _ants.Count; i++)
            {
                var ant = _ants[i];
                if (ant != null && Vector3.Distance(ant.transform.position, position) < 0.5f)
                    return ant;
            }
            return null;
        }
    }
}