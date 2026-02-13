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
        public float TimeScale = 10.0f;
        public int MaxTicksPerGeneration = 1000;

        private List<Ant> _ants = new List<Ant>();
        private int _currentTick = 0;
        private int _generationCount = 1;
        private int _nestsBuiltThisGen = 0;
        
        // Evolution State
        private List<float[]> _parentGenomes = new List<float[]>();
        private int _bestFitnessSoFar = -1;
        private float[] _currentTestedGenome;

        void Start()
        {
            // Initial Random Population
            SpawnGeneration();
        }

        void Update()
        {
            // Speed control
            Time.timeScale = TimeScale;

            // Game Loop
            if (_currentTick < MaxTicksPerGeneration && _ants.Count > 0)
            {
                // Run Tick for all ants
                for (int i = _ants.Count - 1; i >= 0; i--)
                {
                    if (_ants[i] != null) _ants[i].OnTick();
                }
                _currentTick++;
            }
            else
            {
                EndGeneration();
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
                _currentTestedGenome = new float[] { Random.value, Random.value, Random.value, Random.value };
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

        public void ReportNestBuilt()
        {
            _nestsBuiltThisGen++;
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
    }
}