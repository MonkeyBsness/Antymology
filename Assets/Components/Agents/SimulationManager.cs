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
        public float TimeScale = 1.0f;
        public int MaxTicksPerGeneration = 1000;

        private List<Ant> _ants = new List<Ant>();
        private int _currentTick = 0;
        private int _generationCount = 1;
        private int _nestsBuiltThisGen = 0;
        
        // Evolution State
        private List<float[]> _parentGenomes = new List<float[]>();

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
            
            for (int i = 0; i < PopulationSize; i++)
            {
                GameObject antObj = Instantiate(AntPrefab);
                Ant ant = antObj.AddComponent<Ant>();
                
                // Position randomly on surface
                int x = Random.Range(10, 100); 
                int z = Random.Range(10, 100);
                int y = FindSurfaceY(x, z) + 1;
                antObj.transform.position = new Vector3(x, y, z);

                // If we have parents (Gen > 1), mutate them. Otherwise random.
                float[] genes;
                if (_parentGenomes.Count > 0)
                {
                    float[] parent = _parentGenomes[Random.Range(0, _parentGenomes.Count)];
                    genes = Mutate(parent);
                }
                else
                {
                    genes = new float[] { Random.value, Random.value, Random.value, Random.value };
                }

                // 1 Queen, rest workers
                bool isQueen = (i == 0); 
                ant.Init(isQueen, genes);
                _ants.Add(ant);
            }
            Debug.Log($"Generation {_generationCount} Started.");
        }

        private void EndGeneration()
        {
            // Calculate Fitness

            Debug.Log($"Generation {_generationCount} Ended. Fitness: {_nestsBuiltThisGen}");


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
            child[geneToMutate] += Random.Range(-0.1f, 0.1f); // Small mutation
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