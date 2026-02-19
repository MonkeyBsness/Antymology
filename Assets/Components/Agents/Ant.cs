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

        // Pheromones
        private const byte PHEROMONE_QUEEN  = 2;
        private const byte PHEROMONE_FOOD   = 3;
        private const byte PHEROMONE_DANGER = 4;
        private const byte PHEROMONE_WORKER = 5;

        #endregion

        #region Genome layout (must match SimulationManager.GenomeLength)
        private const int INPUTS = 9;
        // Worker
        private const int OFF_EAT        = 0;              
        private const int OFF_SEEK_FOOD  = OFF_EAT + INPUTS;    //0-8   
        private const int OFF_SEEK_QUEEN = OFF_SEEK_FOOD + INPUTS; //9-17
        private const int OFF_EXPLORE    = OFF_SEEK_QUEEN + INPUTS;     //18-26
        private const int IDX_TRANSFER_MIN_HEALTH   = OFF_EXPLORE + INPUTS;     //27 -35

        // Queen
        private const int OFF_EAT_QUEEN = IDX_TRANSFER_MIN_HEALTH + INPUTS; // 36 - 44
        private const int OFF_SEEK_FOOD_QUEEN   = OFF_EAT_QUEEN + INPUTS; // 45 - 53
        private const int OFF_SEEK_WORKER_QUEEN = OFF_SEEK_FOOD_QUEEN + INPUTS; // 54 - 62
        private const int OFF_EXPLORE_QUEEN = OFF_SEEK_WORKER_QUEEN + INPUTS; // 63 - 71
        private const int OFF_BUILD_QUEEN   = OFF_EXPLORE_QUEEN + INPUTS; // 72 - 80

        // Other
        private const int IDX_QUEEN_TARGET_HEALTH   = OFF_BUILD_QUEEN + 1; //81
        private const int IDX_TRANSFER_AMOUNT       = OFF_BUILD_QUEEN + 2; 
        private const int IDX_FOOD_DEPOSIT_STRENGTH = OFF_BUILD_QUEEN + 3;
        private const int IDX_DANGER_DEPOSIT_STRENGTH = OFF_BUILD_QUEEN + 4;
        private const int IDX_QUEEN_SCENT_STRENGTH  =  OFF_BUILD_QUEEN + 5;
        private const int IDX_QUEEN_BUILD_THRESHOLD = OFF_BUILD_QUEEN + 6;
        private const int IDX_QUEEN_BUILD_AGGRESSIVENESS = OFF_BUILD_QUEEN + 7;
        #endregion

        #region State
        public float CurrentHealth;
        public bool IsQueen;
        public float[] Genome;
        private int tickCount;

        private Vector3 _currentPos;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        #endregion

        public void Init(bool isQueen, float[] genome)
        {
            CurrentHealth = MAX_HEALTH;
            IsQueen = isQueen;
            Genome = genome;
            tickCount = 0;

            _currentPos = transform.position;
            _lastPos = _currentPos;
            _hasLastPos = false;

            // Visual distinction
            if (IsQueen) GetComponent<Renderer>().material.color = Color.red;
            else GetComponent<Renderer>().material.color = Color.black;
        }

        public void OnTick()
        {
            if (CurrentHealth <= 0) return;

            // Queen always leaves a trail home (strength is gene-controlled)
            if (tickCount % 2 == 0)
            {
                if (IsQueen)
                {
                    BroadcastQueenScent();
                }
                else
                {
                    BroadcastWorkerScent();
                }
            }

            if (IsQueen)
            {
                BroadcastQueenScent();
            } 

            // Workers can feed/transfer health to queen if close
            if (!IsQueen) TryHealQueen();

            // Environmental effects & health decay
            ApplyHealthDecay();

            // Decision
            DecideAndAct();
        }

        private void ApplyHealthDecay()
        {
            float decay = BASE_DECAY;
            AbstractBlock blockUnder = GetBlockAt(_currentPos + Vector3.down);

            if (blockUnder is AcidicBlock) decay *= ACID_MULTIPLIER;

            CurrentHealth -= decay;

            if (CurrentHealth <= 0) Die();
        }

        private void DecideAndAct()
        {
            if (IsQueen) TempQueenAct();
            else WorkerAct();
        }

        // -------------------- Worker policy --------------------

        private void TempQueenAct()
        {
            float workerSmell   = Smell01(PHEROMONE_WORKER);
            float foodSmell     = Smell01(PHEROMONE_FOOD);
            float dangerSmell   = Smell01(PHEROMONE_DANGER);

            AbstractBlock under = GetBlockAt(_currentPos + Vector3.down);

            float[] inputs = new float[INPUTS]
            {
                CurrentHealth < 30 ? 1f : 0f,                    // 0 lowHealth
                CurrentHealth > 60 ? 1f : 0f,                    // 1 highHealth   
                under is MulchBlock ? 1f : 0f,                   // 2 onMulch
                under is ContainerBlock ? 1f : 0f,               // 3 onContainer
                workerSmell,                                      // 4 workerSmell
                foodSmell,                                       // 5 foodSmell
                dangerSmell,                                     // 6 dangerSmell
                Random.value,                                    // 7 noise
                under is AcidicBlock ? 1f : 0f                   // 8 onAcid
            };

            float eatDesire         = Score(OFF_EAT_QUEEN, inputs);
            float seekFoodDesire    = Score(OFF_SEEK_FOOD_QUEEN, inputs);
            float seekWorkerDesire  = Score(OFF_SEEK_WORKER_QUEEN, inputs);
            float exploreDesire     = Score(OFF_EXPLORE_QUEEN, inputs);
            float buildDesire       = Score(OFF_BUILD_QUEEN, inputs);

            float max = Mathf.Max(eatDesire, seekFoodDesire, seekWorkerDesire, exploreDesire, buildDesire);

            if (max == eatDesire)
            {
                // Debug.Log("try eat");
                TryEat();
            }
            else if (max == seekFoodDesire)
            {
                // Debug.Log("try seekFood");
                TryMoveTowardsPheromone(PHEROMONE_FOOD, avoidDanger: true);
            }
            else if (max == seekWorkerDesire)
            {
                // Debug.Log("try seekQueen");
                TryMoveTowardsPheromone(PHEROMONE_WORKER, avoidDanger: true);
            }
            else if (max == buildDesire)
            {
                TryBuildNest();
            }
            else
            {
                // Debug.Log("try Move");
                TryMoveForward();
            }
        }

        private void WorkerAct()
        {
            // Smells (normalized)
            float queenSmell  = Smell01(PHEROMONE_QUEEN);
            float foodSmell   = Smell01(PHEROMONE_FOOD);
            float dangerSmell = Smell01(PHEROMONE_DANGER);

            AbstractBlock under = GetBlockAt(_currentPos + Vector3.down);

            float[] inputs = new float[INPUTS]
            {
                CurrentHealth < 30 ? 1f : 0f,                    // 0 lowHealth
                CurrentHealth > 60 ? 1f : 0f,                    // 1 highHealth   
                under is MulchBlock ? 1f : 0f,                   // 2 onMulch
                under is ContainerBlock ? 1f : 0f,               // 3 onContainer
                queenSmell,                                      // 4 queenSmell
                foodSmell,                                       // 5 foodSmell
                dangerSmell,                                     // 6 dangerSmell
                Random.value,                                    // 7 noise
                under is AcidicBlock ? 1f : 0f                   // 8 onAcid
            };

            // Deposit pheromones as a *side effect* (gene controlled)
            if (tickCount % 2 == 0)
            {
                TryDepositWorkerPheromones(inputs);
            }

            // If genome not present / too short, fallback to your old heuristic
            if (Genome == null || Genome.Length <= IDX_QUEEN_BUILD_AGGRESSIVENESS)
            {
                LegacyWorkerAct(inputs);
                if (Genome == null)
                {
                    Debug.Log("genome not null");
                    return;
                }
                
                Debug.Log($"genome not match {Genome.Length} : {IDX_QUEEN_BUILD_AGGRESSIVENESS}");
                return;
            }

            float eatDesire       = Score(OFF_EAT, inputs);
            float seekFoodDesire  = Score(OFF_SEEK_FOOD, inputs);
            float seekQueenDesire = Score(OFF_SEEK_QUEEN, inputs);
            float exploreDesire   = Score(OFF_EXPLORE, inputs);

            float max = Mathf.Max(eatDesire, seekFoodDesire, seekQueenDesire, exploreDesire);
            
            if (max == eatDesire)
            {
                // Debug.Log("try eat");
                TryEat();
            }
            else if (max == seekFoodDesire)
            {
                // Debug.Log("try seekFood");
                TryMoveTowardsPheromone(PHEROMONE_FOOD, avoidDanger: true);
            }
            else if (max == seekQueenDesire)
            {
                // Debug.Log("try seekQueen");
                TryMoveTowardsPheromone(PHEROMONE_QUEEN, avoidDanger: true);
            }
            else
            {
                // Debug.Log("try Move");
                TryMoveForward();
            }
        }

        private void LegacyWorkerAct(float[] inputs)
        {
            float moveDesire = inputs[7] + inputs[6];
            float eatDesire = inputs[1] * inputs[0];
            float seekQueenDesire = (CurrentHealth >= 60) ? 1f : 0f;

            float max = Mathf.Max(moveDesire, eatDesire, seekQueenDesire);

            if (max == eatDesire) TryEat();
            else if (max == seekQueenDesire) TryMoveTowardsPheromone(PHEROMONE_QUEEN);
            else TryMoveForward();
        }

        private void TryDepositWorkerPheromones(float[] inputs)
        {
            if (Genome == null || Genome.Length <= IDX_QUEEN_BUILD_AGGRESSIVENESS) return;

            // Decode ranges
            float foodStrength   = DecodeRange(Genome[IDX_FOOD_DEPOSIT_STRENGTH], 0f, 150f);
            float dangerStrength = DecodeRange(Genome[IDX_DANGER_DEPOSIT_STRENGTH], 0f, 200f);

            bool onMulch = inputs[1] > 0.5f;
            bool onAcid  = inputs[7] > 0.5f;

            // Leave food trail mainly when you find food (mulch)
            if (onMulch && foodStrength > 0.01f)
            {
                var air = GetAirBlock(_currentPos);
                air?.DepositPheromone(PHEROMONE_FOOD, foodStrength);
            }

            // Mark dangerous areas so others avoid
            if (onAcid && dangerStrength > 0.01f)
            {
                var air = GetAirBlock(_currentPos);
                air?.DepositPheromone(PHEROMONE_DANGER, dangerStrength);
            }
        }

        // -------------------- Queen policy --------------------

        private void QueenAct()
        {
            bool queenUseGenome = false;
            // If genome missing, keep your old behavior
            if (Genome == null || Genome.Length <= IDX_QUEEN_BUILD_AGGRESSIVENESS || !queenUseGenome)
            {
                // Old: build if enough health; else move away from acid
                if (GetBlockAt(_currentPos + Vector3.down) is AcidicBlock) TryMoveForward();
                else if (CurrentHealth < 40 && GetBlockAt(_currentPos + Vector3.down) is MulchBlock) TryEat();
                else if (CurrentHealth >= 2 * BUILD_COST) TryBuildNest();
                else TryMoveForward();
                return;
            }

            float buildThreshold = DecodeRange(Genome[IDX_QUEEN_BUILD_THRESHOLD], 30f, 120f);
            float buildAgg       = DecodeRange(Genome[IDX_QUEEN_BUILD_AGGRESSIVENESS], 0f, 1f);

            bool onAcid = GetBlockAt(_currentPos + Vector3.down) is AcidicBlock;

            // Avoid acid first
            if (onAcid)
            {
                TryMoveForward();
                return;
            }

            // Build with probability proportional to aggressiveness (stochastic helps exploration)
            if (CurrentHealth >= buildThreshold && Random.value < buildAgg)
            {
                TryBuildNest();
                return;
            }

            // Otherwise stay (workers should bring health). You can uncomment to let queen wander:
            if (Random.value < 0.05f) TryMoveForward();
        }

        // -------------------- Actions --------------------

        private void TryMove()
        {
            List<Vector3> validMoves = CheckMoves(2);

            if (validMoves.Count > 0)
            {
                Vector3 chosen = validMoves[Random.Range(0, validMoves.Count)];
                transform.position = chosen;
                _currentPos = chosen;
            }
        }

        private void TryMoveForward()
        {
            if (!_hasLastPos)
            {
                _lastPos = _currentPos;
                _hasLastPos = true;
                TryMove();
                return;
            }

            List<Vector3> validMoves = CheckMoves(2);
            if (validMoves.Count == 0) return;

            Vector3 chosen = PickForwardMoveWeighted(validMoves);

            Vector3 prev = _currentPos;
            transform.position = chosen;
            _currentPos = chosen;
            _lastPos = prev;
        }

        private Vector3 PickForwardMoveWeighted(List<Vector3> validMoves)
        {
            // Find the move that goes back to _lastPos (match by x/z only)
            Vector3? backMove = null;
            for (int i = 0; i < validMoves.Count; i++)
            {
                if (SameXZ(validMoves[i], _lastPos))
                {
                    backMove = validMoves[i];
                    break;
                }
            }

            // Build candidates for ranking (exclude the backMove from farthest ranking)
            List<Vector3> candidates = new List<Vector3>(validMoves.Count);
            for (int i = 0; i < validMoves.Count; i++)
            {
                if (!backMove.HasValue || validMoves[i] != backMove.Value)
                    candidates.Add(validMoves[i]);
            }

            // Sort by distance from _lastPos using x/z only (descending)
            candidates.Sort((a, b) => DistXZSq(b, _lastPos).CompareTo(DistXZSq(a, _lastPos)));

            // Assign rank weights: 50%, 35%, 10%, 5% backtrack
            List<(Vector3 pos, float w)> weighted = new List<(Vector3, float)>(4);

            if (candidates.Count >= 1) weighted.Add((candidates[0], 0.50f));
            if (candidates.Count >= 2) weighted.Add((candidates[1], 0.35f));
            if (candidates.Count >= 3) weighted.Add((candidates[2], 0.10f));
            if (backMove.HasValue) weighted.Add((backMove.Value, 0.05f));

            float sum = 0f;
            for (int i = 0; i < weighted.Count; i++) sum += weighted[i].w;

            if (sum <= 0f) return validMoves[Random.Range(0, validMoves.Count)];

            float r = Random.value * sum;
            float accum = 0f;

            for (int i = 0; i < weighted.Count; i++)
            {
                accum += weighted[i].w;
                if (r <= accum) return weighted[i].pos;
            }

            return weighted[weighted.Count - 1].pos;
        }

        private bool SameXZ(Vector3 a, Vector3 b)
        {
            return Mathf.RoundToInt(a.x) == Mathf.RoundToInt(b.x)
                && Mathf.RoundToInt(a.z) == Mathf.RoundToInt(b.z);
        }

        private float DistXZSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private void TryEat()
        {
            Vector3 targetBlockPos = _currentPos + Vector3.down;
            if (GetBlockAt(targetBlockPos) is MulchBlock)
            {
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new AirBlock());
                CurrentHealth = Mathf.Min(CurrentHealth + HEAL_AMOUNT, MAX_HEALTH);
            }
        }

        private void TryBuildNest()
        {
            if (!IsQueen || CurrentHealth < BUILD_COST) return;

            //List<Vector3> validMoves = CheckMoves(0);

            // if (validMoves.Count > 0)
            // {   
                Vector3 targetBlockPos = _currentPos;
                if (GetBlockAt(_currentPos) is ContainerBlock) return;
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y - 1, (int)targetBlockPos.z, new NestBlock());
                CurrentHealth -= BUILD_COST;

                SimulationManager.Instance.ReportNestBuilt();
            // }
        }

        private void TryDig()
        {
            Vector3 targetBlockPos = _currentPos + Vector3.down;
            AbstractBlock block = GetBlockAt(targetBlockPos);

            if (!(block is ContainerBlock) && !(block is AirBlock))
            {
                WorldManager.Instance.SetBlock((int)targetBlockPos.x, (int)targetBlockPos.y, (int)targetBlockPos.z, new AirBlock());
            }
        }

        private void TryMoveTowardsPheromone(byte type, bool avoidDanger = false)
        {
            double bestScent = -1.0;
            Vector3 bestMove = _currentPos;
            List<Vector3> validMoves = CheckMoves(2);

            if (validMoves.Count == 0) return;

            for (int i = 0; i < validMoves.Count; i++)
            {
                Vector3 pos = validMoves[i];
                AirBlock air = GetAirBlock(pos);
                if (air == null) continue;

                if (avoidDanger && type != PHEROMONE_DANGER)
                {
                    // If this move is too dangerous, skip it
                    double danger = air.GetPheromone(PHEROMONE_DANGER);
                    if (danger > 50.0) continue; // simple fixed threshold
                }

                double scent = air.GetPheromone(type);
                if (scent > bestScent)
                {
                    bestScent = scent;
                    bestMove = pos;
                }
            }

            if (bestScent > 0)
            {
                transform.position = bestMove;
                _currentPos = bestMove;
            }
            else
            {
                TryMoveForward();
            }
        }

        private void Die()
        {
            if(IsQueen) 
            {
                SimulationManager.Instance.ReportQueenDie();
            }
            else
            {
                SimulationManager.Instance.ReportWorkerDie();
            }
            SimulationManager.Instance.RemoveAnt(this);
            Destroy(gameObject);
        }

        private void BroadcastQueenScent()
        {
            AirBlock current = GetAirBlock(_currentPos);
            if (current == null) return;

            double strength = 100.0;

            // if (Genome != null && Genome.Length > IDX_QUEEN_BUILD_AGGRESSIVENESS)
            // {
            //     strength = DecodeRange(Genome[IDX_QUEEN_SCENT_STRENGTH], 20f, 200f);
            // }

            current.DepositPheromone(PHEROMONE_QUEEN, strength);
        }

        private void BroadcastWorkerScent()
        {
            AirBlock current = GetAirBlock(_currentPos);
            if (current == null) return;

            double strength = 20.0;

            current.DepositPheromone(PHEROMONE_WORKER, strength);
        }

        private void TryHealQueen()
        {
            if (Genome == null || Genome.Length <= IDX_QUEEN_BUILD_AGGRESSIVENESS)
            {
                LegacyHealQueen();
                return;
            }

            float transferMinHealth = DecodeRange(Genome[IDX_TRANSFER_MIN_HEALTH], 40f, 95f);
            float queenTargetHealth = DecodeRange(Genome[IDX_QUEEN_TARGET_HEALTH], 40f, 100f);
            float transferAmount = DecodeRange(Genome[IDX_TRANSFER_AMOUNT], 2f, 25f);

            List<Vector3> neighbors = CheckMoves(2);

            for (int i = 0; i < neighbors.Count; i++)
            {
                Ant other = SimulationManager.Instance.GetAntAt(neighbors[i]);
                if (other == null || !other.IsQueen) continue;

                if (CurrentHealth <= transferMinHealth) continue;
                if (other.CurrentHealth >= queenTargetHealth) continue;

                float amount = Mathf.Min(transferAmount, CurrentHealth - 1f);
                if (amount <= 0f) return;

                CurrentHealth -= amount;
                other.ReceiveHealth(amount);
                SimulationManager.Instance.ReportHealQueen();
                return;
            }
        }

        private void LegacyHealQueen()
        {
            const float HEALTH_TRANSFER_AMOUNT = 10f;

            List<Vector3> neighbors = CheckMoves(2);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Ant other = SimulationManager.Instance.GetAntAt(neighbors[i]);
                if (other != null && other.IsQueen && CurrentHealth > HEALTH_TRANSFER_AMOUNT)
                {
                    if (other.CurrentHealth >= 90.0f) return;
                    CurrentHealth -= HEALTH_TRANSFER_AMOUNT;
                    other.ReceiveHealth(HEALTH_TRANSFER_AMOUNT);
                    return;
                }
            }
        }

        public void ReceiveHealth(float amount)
        {
            CurrentHealth += amount;
            if (CurrentHealth >= MAX_HEALTH) CurrentHealth = MAX_HEALTH;
        }

        // -------------------- Helpers --------------------

        private float Score(int offset, float[] inputs)
        {
            float s = 0f;
            for (int i = 0; i < inputs.Length; i++)
            {
                s += Genome[offset + i] * inputs[i];
            }
            return s;
        }

        private float Decode01(float gene)
        {
            return Mathf.Clamp01((gene + 1f) * 0.5f);
        }

        private float DecodeRange(float gene, float min, float max)
        {
            return Mathf.Lerp(min, max, Decode01(gene));
        }

        private float Smell01(byte type)
        {
            AirBlock currentAir = GetAirBlock(_currentPos);
            if (currentAir == null) return 0f;

            // AirBlock caps at 1000
            return Mathf.Clamp01((float)(currentAir.GetPheromone(type) / 1000.0));
        }

        private AbstractBlock GetBlockAt(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z);
        }

        private List<Vector3> CheckMoves(int y_target)
        {
            List<Vector3> validMoves = new List<Vector3>();
            Vector3[] directions = {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right,
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(-1f, 0f, 1f),
                new Vector3(-1f, 0f, -1f)
            };

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
            for (int y = (int)_currentPos.y + 2; y >= 0; y--)
            {
                if (!(WorldManager.Instance.GetBlock(x, y, z) is AirBlock)) return y;
            }
            return 0;
        }

        private AirBlock GetAirBlock(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z) as AirBlock;
        }
    }
}
