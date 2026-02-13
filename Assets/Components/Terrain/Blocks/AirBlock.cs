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

        public void DepositPheromone(byte type, double amount)
        {
            if (!_pheromones.ContainsKey(type)) _pheromones[type] = 0;
            _pheromones[type] += amount;
            if (_pheromones[type] > MAX_PHEROMONE) _pheromones[type] = MAX_PHEROMONE;
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
        }

        /// <summary>
        /// THIS CURRENTLY ONLY EXISTS AS A WAY OF SHOWING YOU WHATS POSSIBLE.
        /// </summary>
        /// <param name="neighbours"></param>
        public void Diffuse(AbstractBlock[] neighbours)
        {
            throw new NotImplementedException();
        }

        #endregion

    }
}
