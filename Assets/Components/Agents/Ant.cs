using Antymology.Terrain;
using System.Collections.Generic;
using UnityEngine;

namespace Antymology.Agents
{
    public class Ant : MonoBehaviour
    {
        #region Configuration
        private const float MAX_HEALTH = 100f;
        private const float BASE_DECAY = 1.0f;
        private const float ACID_MULTIPLIER = 2.0f;
        private const float HEAL_AMOUNT = 30f;
        private const float BUILD_COST = 30f;
        #endregion

        #region State
        public float CurrentHealth;
        public bool IsQueen;
        public float[] Genome; // Weights for decision making
        private Vector3 _currentPos;
        #endregion

        public void Init(bool isQueen, float[] genome)
        {
            CurrentHealth = MAX_HEALTH;
            IsQueen = isQueen;
            Genome = genome;
            _currentPos = transform.position;
            
            // Visual distinction
            if (IsQueen) GetComponent<Renderer>().material.color = Color.red;
            else GetComponent<Renderer>().material.color = Color.black;
        }

        // Called by SimulationManager every tick
        public void OnTick()
        {
            if (CurrentHealth <= 0) return;

            // Environmental Effects & Health Decay
            ApplyHealthDecay();

            // Decision
            DecideAndAct();
        }

        private void ApplyHealthDecay()
        {
            float decay = BASE_DECAY;
            AbstractBlock blockUnder = GetBlockAt(_currentPos + Vector3.down);

            // Acid Check
            if (blockUnder is AcidicBlock) decay *= ACID_MULTIPLIER;

            CurrentHealth -= decay;

            if (CurrentHealth <= 0) Die();
        }

        private void DecideAndAct()
        {
            // Simple inputs for our genome
            // 0: Health is Low?
            // 1: Standing on Mulch?
            // 2: Standing on Container?
            // 3: Random Noise
            float[] inputs = new float[] {
                CurrentHealth < 50 ? 1f : 0f,
                GetBlockAt(_currentPos + Vector3.down) is MulchBlock ? 1f : 0f,
                GetBlockAt(_currentPos + Vector3.down) is ContainerBlock ? 1f : 0f,
                Random.value
            };

            // Calculate desire for each action based on Genome weights
            
            float moveDesire = inputs[3] * Genome[0]; 
            float eatDesire = inputs[1] * inputs[0] * Genome[1]; // Desire to eat rises if health is low and food is present
            float digDesire = inputs[2] * Genome[2];
            float buildDesire = (IsQueen && CurrentHealth >= BUILD_COST) ? inputs[3] * Genome[3] : -1f;

            // Select highest desire
            float max = Mathf.Max(moveDesire, eatDesire, digDesire, buildDesire);

            if (max == eatDesire) TryEat();
            else if (max == buildDesire) TryBuildNest();
            else if (max == digDesire) TryDig();
            else TryMove(); // Default to moving
        }

        #region Actions

        private void TryMove()
        {
            // Get valid neighbors
            List<Vector3> validMoves = new List<Vector3>();
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            foreach (var dir in directions)
            {
                Vector3 target = _currentPos + dir;
                
                // Can only move if Y difference is <= 2
                int surfaceY = FindSurfaceY((int)target.x, (int)target.z);
                
                if (Mathf.Abs(surfaceY - _currentPos.y) <= 2)
                {
                    validMoves.Add(new Vector3(target.x, surfaceY + 1, target.z));
                }
            }

            if (validMoves.Count > 0)
            {
                Vector3 chosen = validMoves[Random.Range(0, validMoves.Count)];
                transform.position = chosen;
                _currentPos = chosen;
            }
        }

        private void TryEat()
        {
            Vector3 targetBlockPos = _currentPos + Vector3.down;
            if (GetBlockAt(targetBlockPos) is MulchBlock)
            {
                // Cannot eat if another ant is here
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new AirBlock());
                CurrentHealth = Mathf.Min(CurrentHealth + HEAL_AMOUNT, MAX_HEALTH);
            }
        }

        private void TryBuildNest()
        {
            if (!IsQueen || CurrentHealth < BUILD_COST) return;

            // Build nest at current location
            Vector3 targetBlockPos = _currentPos;
            WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new NestBlock());
            CurrentHealth -= BUILD_COST;
            
            SimulationManager.Instance.ReportNestBuilt();
        }

        private void TryDig()
        {
            Vector3 targetBlockPos = _currentPos + Vector3.down;
            AbstractBlock block = GetBlockAt(targetBlockPos);
            
            // Cannot dig Container blocks
            if (!(block is ContainerBlock) && !(block is AirBlock))
            {
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new AirBlock());
            }
        }

        private void Die()
        {
            SimulationManager.Instance.RemoveAnt(this);
            Destroy(gameObject);
        }

        #endregion

        #region Helpers
        private AbstractBlock GetBlockAt(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z);
        }

        private int FindSurfaceY(int x, int z)
        {
            // Raycast logic or iterative search using WorldManager to find top non-air block
            // Simplified iterative search from current height + 2 down to 0
            for(int y = (int)_currentPos.y + 2; y >= 0; y--)
            {
                if(!(WorldManager.Instance.GetBlock(x, y, z) is AirBlock)) return y;
            }
            return 0;
        }
        #endregion
    }
}