using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Antymology.Terrain
{
    /// <summary>
    /// The air type of block. Contains the internal data representing phermones in the air.
    /// </summary>
    public class AirBlock : AbstractBlock
    {

        #region Fields

        private Dictionary<byte, double> _pheromones = new Dictionary<byte, double>();
        private const double MAX_PHEROMONE = 1000.0;

        /// <summary>
        /// Statically held is visible.
        /// </summary>
        private static bool _isVisible = false;


        #endregion

        #region Methods

        /// <summary>
        /// Air blocks are going to be invisible.
        /// </summary>
        public override bool isVisible()
        {
            return _isVisible;
        }

        /// <summary>
        /// Air blocks are invisible so asking for their tile map coordinate doesn't make sense.
        /// </summary>
        public override Vector2 tileMapCoordinate()
        {
            throw new Exception("An invisible tile cannot have a tile map coordinate.");
        }

        public void DepositPheromone(byte type, double amount, bool is_diffuse = false)
        {
            
            if (!_pheromones.ContainsKey(type))
            {
                if(_pheromones.Count == 0) SimulationManager.Instance.ReportPheromoneDeposit(this);
            
                _pheromones[type] = 0;

            } 

            _pheromones[type] += amount;
            if (_pheromones[type] > MAX_PHEROMONE) _pheromones[type] = MAX_PHEROMONE;
            if (!is_diffuse)
            {
                Diffuse(type, amount);
            }

        }

        public double GetPheromone(byte type)
        {
            if (_pheromones.ContainsKey(type)) return _pheromones[type];
            return 0.0;
        }

        public void Decay(float decayRate)
        {
            var keys = new List<byte>(_pheromones.Keys);
            foreach (var key in keys)
            {
                _pheromones[key] *= decayRate;
                if (_pheromones[key] < 0.1) _pheromones.Remove(key);
            }
            if (_pheromones.Count == 0) SimulationManager.Instance.RemovePheromoneBlock(this);
        }

        private void Diffuse(byte type, double amount, int r = 3)
        {
            var neighbors = new List<AirBlock>();
            Vector3 cur_pos = new Vector3(this.worldXCoordinate, this.worldYCoordinate, this.worldZCoordinate);

            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                AirBlock neighbor = GetAirBlock(cur_pos + new Vector3(dx, dy, dz));
                int max = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy), Mathf.Abs(dz));
                amount = amount/(2*max);
                if (neighbor != null) neighbor.DepositPheromone(type, amount, true);
            }


        }

        private AirBlock GetAirBlock(Vector3 pos)
        {
            return WorldManager.Instance.GetBlock((int)pos.x, (int)pos.y, (int)pos.z) as AirBlock;
        }

        #endregion


    }
}
