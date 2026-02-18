using Antymology.Terrain;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Antymology.Agents
{
    public class SimulationManager : Singleton<SimulationManager>
    {
        public GameObject AntPrefab;
        public int PopulationSize = 20;
        public float DecayRate = 0.8f;
        public float TimeScale = 1.0f;
        public int MaxTicksPerGeneration = 1000;

        private List<Ant> _ants = new List<Ant>();
        private int _currentTick = 0;
        private int _generationCount = 1;
        private int _nestsBuiltThisGen = 0;
        
        // Evolution State
        private List<float[]> _parentGenomes = new List<float[]>();
        private int _bestFitnessSoFar = -1;
        private float[] _currentTestedGenome;

        private float _timer = 0f;
        private const float TICK_DELAY = 1f / 60f;
        private List<AirBlock> _pheromoneBlocks = new List<AirBlock>();

        void Start()
        {
            // Initial Random Population
            SpawnGeneration();
        }

        void Update()
        {
            // 1. Set Unity's TimeScale so other things (like Camera/Physics) sync up
            Time.timeScale = TimeScale;

            // 2. Accumulate time. 
            // Time.deltaTime scales with Time.timeScale automatically.
            // If TimeScale is 10, deltaTime is 10x larger, so we add more time here.
            _timer += Time.deltaTime;

            // 3. Run as many ticks as needed to catch up
            while (_timer >= TICK_DELAY)
            {
                _timer -= TICK_DELAY;
                
                // Game Loop Logic
                if (_currentTick < MaxTicksPerGeneration && _ants.Count > 0)
                {
                    DecayPheromone();
                    for (int i = _ants.Count - 1; i >= 0; i--)
                    {
                        if (_ants[i] != null) _ants[i].OnTick();
                    }
                    _currentTick++;
                }
                else
                {
                    EndGeneration();
                    // Break the loop so we don't start a new generation 
                    // and instantly run 500 ticks of it in the same frame
                    _timer = 0; 
                    break; 
                }
            }
        }

        private void SpawnGeneration()
        {
            _currentTick = 0;
            _nestsBuiltThisGen = 0;

            if (_parentGenomes.Count > 0)
            {
                // EVOLUTION: Take the best parent and mutate it to try and find an improvement.
                float[] parent = _parentGenomes[0]; 
                _currentTestedGenome = Mutate(parent);
            }
            else
            {
                // INITIALIZATION: First generation gets random weights.
                _currentTestedGenome = new float[] { Random.value, Random.value, Random.value, Random.value, Random.value };
            }
            
            for (int i = 0; i < PopulationSize; i++)
            {
                GameObject antObj = Instantiate(AntPrefab);
                Ant ant = antObj.AddComponent<Ant>();
                
                // Position randomly on surface
                int x = Random.Range(10, 100); 
                int z = Random.Range(10, 100);
                int y = FindSurfaceY(x, z) + 1;
                antObj.transform.position = new Vector3(x, y, z);

                // // If we have parents (Gen > 1), mutate them. Otherwise random.
                // float[] genes;
                // if (_parentGenomes.Count > 0)
                // {
                //     float[] parent = _parentGenomes[Random.Range(0, _parentGenomes.Count)];
                //     genes = Mutate(parent);
                // }
                // else
                // {
                //     genes = new float[] { Random.value, Random.value, Random.value, Random.value };
                // }

                // 1 Queen, rest workers
                bool isQueen = (i == 0); 
                ant.Init(isQueen, _currentTestedGenome);
                _ants.Add(ant);
            }
            Debug.Log($"Generation {_generationCount} Started.");
        }

        private void EndGeneration()
        {
            // Calculate Fitness

            Debug.Log($"Generation {_generationCount} Ended. Fitness: {_nestsBuiltThisGen}");

            if (_nestsBuiltThisGen >= _bestFitnessSoFar)
            {
                _bestFitnessSoFar = _nestsBuiltThisGen;
                
                // Save this genome as the new parent
                _parentGenomes.Clear();
                _parentGenomes.Add(_currentTestedGenome);
                
                Debug.Log($"<color=green>NEW RECORD! Saved Genome.</color>");
            }
            else
            {
                Debug.Log($"<color=red>Genome Failed. Reverting to previous best.</color>");
                // We do nothing to _parentGenomes. 
                // Next SpawnGeneration will grab the OLD best genome and try a DIFFERENT mutation.
            }


            foreach(var ant in _ants) if(ant != null) Destroy(ant.gameObject);
            _ants.Clear();
            ClearPheromone();

            _generationCount++;
            SpawnGeneration();
        }

        // Genetic Helpers
        private float[] Mutate(float[] parent)
        {
            float[] child = (float[])parent.Clone();
            int geneToMutate = Random.Range(0, child.Length);
            child[geneToMutate] += Random.Range(-0.2f, 0.2f); // Small mutation
            return child;
        }

        private void DecayPheromone()
        {
            if (_pheromoneBlocks.Count != 0)
            {
                Debug.Log("Pheromone Decaied");
            }
            foreach(var airBlock in _pheromoneBlocks) 
            {
                airBlock.Decay(DecayRate);
            }
        }

        private void ClearPheromone()
        {
            foreach(var airBlock in _pheromoneBlocks) 
            {
                airBlock.Clear();
            }
            _pheromoneBlocks.Clear();
        }

        public void ReportNestBuilt()
        {
            _nestsBuiltThisGen++;
        }

        public void ReportPheromoneDeposit(AirBlock airBlock)
        {
            if (!_pheromoneBlocks.Contains(airBlock)) _pheromoneBlocks.Add(airBlock);
        }

        public void RemovePheromoneBlock(AirBlock airBlock)
        {
            if (_pheromoneBlocks.Contains(airBlock)) _pheromoneBlocks.Remove(airBlock);
        }

        public void RemoveAnt(Ant ant)
        {
            _ants.Remove(ant);
        }
        
        // Helper to find ground for spawning
        private int FindSurfaceY(int x, int z)
        {
            for(int y = 20; y >= 0; y--)
                if(!(WorldManager.Instance.GetBlock(x, y, z) is AirBlock)) return y;
            return 0;
        }

        
        public Ant GetAntAt(Vector3 position)
        {
            // Simple check: Is there an ant at these exact coordinates?
            foreach (var ant in _ants)
            {
                if (ant != null && Vector3.Distance(ant.transform.position, position) < 0.5f)
                {
                    return ant;
                }
            }
            return null;
        }
    }
}