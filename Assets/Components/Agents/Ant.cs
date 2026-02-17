using Antymology.Terrain;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
        private const byte PHEROMONE_QUEEN = 2; 
        private const float HEALTH_TRANSFER_AMOUNT = 10f;
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

            if (IsQueen)
            {
                BroadcastQueenScent();
            }

            if (!IsQueen)
            {
                TryHealQueen();
            }

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

            float queenSmell = 0f;
            AirBlock currentAir = GetAirBlock(_currentPos);
            if (currentAir != null)
            {
                 // Check neighbors for max scent to see if we are "near" the trail
                 queenSmell = (float)currentAir.GetPheromone(PHEROMONE_QUEEN);
            }

            float[] inputs = new float[] {
                CurrentHealth < 50 ? 1f : 0f,
                GetBlockAt(_currentPos + Vector3.down) is MulchBlock ? 1f : 0f,
                GetBlockAt(_currentPos + Vector3.down) is ContainerBlock ? 1f : 0f,
                Random.value,
                queenSmell > 0 ? 1f : 0f,
                GetBlockAt(_currentPos + Vector3.down) is AcidicBlock ? 10.0f : 0f

            };

            // Calculate desire for each action based on Genome weights
       
            float moveDesire = 0f, eatDesire = 0f, digDesire = 0f, buildDesire = 0f, seekQueenDesire = 0f;

            if (IsQueen)
            {
                moveDesire = inputs[5] + inputs[3];
                eatDesire     = inputs[1] * inputs[0];
                buildDesire = (CurrentHealth >= 2 * BUILD_COST) ? 0.5f : 0f;


            }
            else
            {
                moveDesire          = inputs[3] * Genome[0]; 
                eatDesire           = inputs[1] * inputs[0] * Genome[1]; // Desire to eat rises if health is low and food is present
                digDesire           = inputs[2] * Genome[2];
                seekQueenDesire     = inputs[4] * Genome[4];
            }

            // Select highest desire
            float max = Mathf.Max(moveDesire, eatDesire, digDesire, buildDesire, seekQueenDesire);

            bool _ant_type = IsQueen;

            if (max == eatDesire) 
            {
                if (_ant_type) Debug.Log("try eat");
                TryEat();
            }    
            else if (max == buildDesire)
            {
                if (_ant_type) Debug.Log("try build");
                TryBuildNest();
            }
            else if (max == digDesire)
            {
                if (_ant_type) Debug.Log("try dig");
                TryDig();
            }
            else if (max == seekQueenDesire) 
            {
                if (_ant_type) Debug.Log("try seek");
                TryMoveTowardsQueen(); 
            }
            else 
            {
                if (_ant_type) Debug.Log("try move");
                TryMove(); // Default to moving
            }
        }

        #region Actions

        private void TryMove()
        {
            // Get valid neighbors
            List<Vector3> validMoves = CheckMoves(2);

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

            List<Vector3> validMoves = CheckMoves(0);

            if (validMoves.Count > 0)
            {
                // Build nest at current location
                Vector3 targetBlockPos = _currentPos;
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new NestBlock());
                CurrentHealth -= BUILD_COST;

                Debug.Log($"Nestblock placed, current health: {CurrentHealth}");
                
                SimulationManager.Instance.ReportNestBuilt();
            }
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

        private void TryMoveTowardsQueen()
        {
            Vector3 bestMove = _currentPos;
            double maxScent = -1.0;

            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            
            foreach (var dir in directions)
            {
                Vector3 target = _currentPos + dir;
                int surfaceY = FindSurfaceY((int)target.x, (int)target.z);

                if (Mathf.Abs(surfaceY - _currentPos.y) <= 2)
                {
                    Vector3 potentialPos = new Vector3(target.x, surfaceY + 1, target.z);
                    AirBlock air = GetAirBlock(potentialPos);
                    
                    if (air != null)
                    {
                        double scent = air.GetPheromone(PHEROMONE_QUEEN);
                        if (scent > maxScent)
                        {
                            maxScent = scent;
                            bestMove = potentialPos;
                        }
                    }
                }
            }

            // Move to the strongest scent
            if (maxScent > 0)
            {
                transform.position = bestMove;
                _currentPos = bestMove;
            }
            else
            {
                // If we lost the trail, move randomly
                TryMove();
            }
        }

        private void Die()
        {
            if (IsQueen) Debug.Log("The Queen is dead.");
            SimulationManager.Instance.RemoveAnt(this);
            Destroy(gameObject);
        }

        private void BroadcastQueenScent()
        {
            // Deposit strong scent at current location
            AirBlock current = GetAirBlock(_currentPos);
            if (current != null) current.DepositPheromone(PHEROMONE_QUEEN, 100.0);

            // Deposit weaker scent in neighbors to create a gradient
            // This helps workers find the queen even if they aren't on the exact same block
            Vector3[] offsets = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (var offset in offsets)
            {
                AirBlock neighbor = GetAirBlock(_currentPos + offset);
                if (neighbor != null) neighbor.DepositPheromone(PHEROMONE_QUEEN, 50.0);
            }
        }

        private void TryHealQueen()
        {
            // Check if there is an ant at my position
            Ant otherAnt = SimulationManager.Instance.GetAntAt(_currentPos);

            // If found, and it is the Queen, and I have health to spare
            if (otherAnt != null && otherAnt.IsQueen && CurrentHealth > HEALTH_TRANSFER_AMOUNT)
            {
                CurrentHealth -= HEALTH_TRANSFER_AMOUNT;
                otherAnt.ReceiveHealth(HEALTH_TRANSFER_AMOUNT);
            }
        }

        public void ReceiveHealth(float amount)
        {
            CurrentHealth += amount;
            // Cap at Max Health if desired, though PDF didn't specify a hard cap for Queen
        }

        #endregion

        #region Helpers
        private AbstractBlock GetBlockAt(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z);
        }

        private List<Vector3> CheckMoves(int y_target)
        {
            List<Vector3> validMoves = new List<Vector3>();
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            foreach (var dir in directions)
            {
                Vector3 target = _currentPos + dir;
                
                int surfaceY = FindSurfaceY((int)target.x, (int)target.z);
                
                if (Mathf.Abs(surfaceY - _currentPos.y) <= y_target)
                {
                    validMoves.Add(new Vector3(target.x, surfaceY + 1, target.z));
                }
            }

            return validMoves;
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

        private AirBlock GetAirBlock(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z) as AirBlock;
        }

        #endregion
    }
}