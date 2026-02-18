using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antymology.Terrain
{
    /// <summary>
    /// Centralized pheromone grid for performance.
    /// 
    /// Key ideas:
    /// - Ants "deposit" into a 3D float grid (O(1)).
    /// - Each tick we apply decay + one diffusion iteration ONLY within an active bounding box.
    /// - Values below Epsilon are clamped to 0, shrinking the active region over time.
    /// 
    /// This replaces per-AirBlock dictionaries + per-deposit 3D diffusion.
    /// </summary>
    public class PheromoneField : MonoBehaviour
    {
        public static PheromoneField Instance { get; private set; }

        [Header("Diffusion/Decay")]
        [Range(0f, 1f)] public float DiffusionRate = 0.25f;   // 0=no diffuse, 1=full neighbor averaging
        public int DiffusionIterations = 1;                  // keep at 1 for speed
        public float Epsilon = 0.05f;                        // below this -> 0

        // Allocate on demand per pheromone type
        private readonly Dictionary<byte, float[,,]> _field = new Dictionary<byte, float[,,]>();
        private readonly Dictionary<byte, float[,,]> _buffer = new Dictionary<byte, float[,,]>();

        private int _sx, _sy, _sz;
        private bool _initialized;

        // Active bounds (union across all pheromone types)
        private bool _hasActive;
        private int _minX, _minY, _minZ, _maxX, _maxY, _maxZ;

        private static readonly Vector3Int[] Neigh6 =
        {
            new Vector3Int(1,0,0), new Vector3Int(-1,0,0),
            new Vector3Int(0,1,0), new Vector3Int(0,-1,0),
            new Vector3Int(0,0,1), new Vector3Int(0,0,-1),
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            var wm = WorldManager.Instance;
            if (wm == null) return;

            // Prefer public size properties if you added them, otherwise fall back to reflection on private Blocks[,,].
            try
            {
                var t = wm.GetType();
                var px = t.GetProperty("SizeX");
                var py = t.GetProperty("SizeY");
                var pz = t.GetProperty("SizeZ");

                if (px != null && py != null && pz != null)
                {
                    _sx = (int)px.GetValue(wm);
                    _sy = (int)py.GetValue(wm);
                    _sz = (int)pz.GetValue(wm);
                    _initialized = true;
                    return;
                }

                var f = t.GetField("Blocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null)
                {
                    var arr = f.GetValue(wm) as Array;
                    if (arr != null && arr.Rank == 3)
                    {
                        _sx = arr.GetLength(0);
                        _sy = arr.GetLength(1);
                        _sz = arr.GetLength(2);
                        _initialized = true;
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // ignore; will remain uninitialized this frame
            }
        }

        private float[,,] GetGrid(byte type)
        {
            EnsureInitialized();
            if (!_initialized) return null;

            if (!_field.TryGetValue(type, out var g))
            {
                g = new float[_sx, _sy, _sz];
                _field[type] = g;
            }
            return g;
        }

        private float[,,] GetBuffer(byte type)
        {
            EnsureInitialized();
            if (!_initialized) return null;

            if (!_buffer.TryGetValue(type, out var b))
            {
                b = new float[_sx, _sy, _sz];
                _buffer[type] = b;
            }
            return b;
        }

        private static int Clamp(int v, int min, int max) => (v < min) ? min : (v > max) ? max : v;

        private void ExpandActiveBounds(int x, int y, int z, int radius)
        {
            if (!_hasActive)
            {
                _hasActive = true;
                _minX = _maxX = x;
                _minY = _maxY = y;
                _minZ = _maxZ = z;
            }
            else
            {
                _minX = Math.Min(_minX, x);
                _minY = Math.Min(_minY, y);
                _minZ = Math.Min(_minZ, z);
                _maxX = Math.Max(_maxX, x);
                _maxY = Math.Max(_maxY, y);
                _maxZ = Math.Max(_maxZ, z);
            }

            // expand for diffusion neighborhood
            _minX = Math.Max(0, _minX - radius);
            _minY = Math.Max(0, _minY - radius);
            _minZ = Math.Max(0, _minZ - radius);
            _maxX = Math.Min(_sx - 1, _maxX + radius);
            _maxY = Math.Min(_sy - 1, _maxY + radius);
            _maxZ = Math.Min(_sz - 1, _maxZ + radius);
        }

        /// <summary>
        /// Deposit pheromone into the grid. No immediate diffusion; diffusion happens in Step().
        /// </summary>
        public void Deposit(int x, int y, int z, byte type, float amount)
        {
            if (amount <= 0f) return;
            EnsureInitialized();
            if (!_initialized) return;

            if (x < 0 || y < 0 || z < 0 || x >= _sx || y >= _sy || z >= _sz) return;

            var g = GetGrid(type);
            g[x, y, z] += amount;

            // track active bounds; radius=1 is enough for 6-neighbor diffusion
            ExpandActiveBounds(x, y, z, radius: 1);
        }

        public float Get(int x, int y, int z, byte type)
        {
            EnsureInitialized();
            if (!_initialized) return 0f;
            if (x < 0 || y < 0 || z < 0 || x >= _sx || y >= _sy || z >= _sz) return 0f;

            if (!_field.TryGetValue(type, out var g)) return 0f;
            return g[x, y, z];
        }

        /// <summary>
        /// Apply decay + diffusion on active region only.
        /// Call once per simulation tick.
        /// </summary>
        public void Step(float decayRate)
        {
            EnsureInitialized();
            if (!_initialized) return;
            if (!_hasActive) return;

            decayRate = Mathf.Clamp01(decayRate);

            // We'll compute next active bounds while we update.
            bool nextHas = false;
            int nMinX = int.MaxValue, nMinY = int.MaxValue, nMinZ = int.MaxValue;
            int nMaxX = int.MinValue, nMaxY = int.MinValue, nMaxZ = int.MinValue;

            // For each pheromone type that exists, run decay+diffuse inside current bounds.
            foreach (var kv in _field)
            {
                byte type = kv.Key;
                var g = kv.Value;

                // 1) Decay in-place for current active region
                for (int x = _minX; x <= _maxX; x++)
                for (int y = _minY; y <= _maxY; y++)
                for (int z = _minZ; z <= _maxZ; z++)
                {
                    float v = g[x, y, z] * decayRate;
                    g[x, y, z] = (v < Epsilon) ? 0f : v;
                }

                // 2) Diffusion iterations (6-neighbor) using buffer
                for (int iter = 0; iter < DiffusionIterations; iter++)
                {
                    var b = GetBuffer(type);

                    // Copy region into buffer with diffusion update
                    float t = Mathf.Clamp01(DiffusionRate);
                    float keep = 1f - t;

                    for (int x = _minX; x <= _maxX; x++)
                    for (int y = _minY; y <= _maxY; y++)
                    for (int z = _minZ; z <= _maxZ; z++)
                    {
                        float center = g[x, y, z];
                        if (center <= 0f)
                        {
                            b[x, y, z] = 0f;
                            continue;
                        }

                        float sum = 0f;
                        int cnt = 0;
                        foreach (var d in Neigh6)
                        {
                            int nx = x + d.x, ny = y + d.y, nz = z + d.z;
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= _sx || ny >= _sy || nz >= _sz) continue;
                            sum += g[nx, ny, nz];
                            cnt++;
                        }

                        float avg = (cnt > 0) ? (sum / cnt) : 0f;
                        float nv = keep * center + t * avg;
                        b[x, y, z] = (nv < Epsilon) ? 0f : nv;
                    }

                    // Swap region back into g
                    for (int x = _minX; x <= _maxX; x++)
                    for (int y = _minY; y <= _maxY; y++)
                    for (int z = _minZ; z <= _maxZ; z++)
                        g[x, y, z] = b[x, y, z];
                }

                // 3) Update next active bounds (union across types)
                for (int x = _minX; x <= _maxX; x++)
                for (int y = _minY; y <= _maxY; y++)
                for (int z = _minZ; z <= _maxZ; z++)
                {
                    if (g[x, y, z] <= 0f) continue;

                    if (!nextHas)
                    {
                        nextHas = true;
                        nMinX = nMaxX = x;
                        nMinY = nMaxY = y;
                        nMinZ = nMaxZ = z;
                    }
                    else
                    {
                        if (x < nMinX) nMinX = x;
                        if (y < nMinY) nMinY = y;
                        if (z < nMinZ) nMinZ = z;
                        if (x > nMaxX) nMaxX = x;
                        if (y > nMaxY) nMaxY = y;
                        if (z > nMaxZ) nMaxZ = z;
                    }
                }
            }

            // Expand next bounds by 1 to allow diffusion neighborhood next tick.
            if (nextHas)
            {
                _hasActive = true;
                _minX = Math.Max(0, nMinX - 1);
                _minY = Math.Max(0, nMinY - 1);
                _minZ = Math.Max(0, nMinZ - 1);
                _maxX = Math.Min(_sx - 1, nMaxX + 1);
                _maxY = Math.Min(_sy - 1, nMaxY + 1);
                _maxZ = Math.Min(_sz - 1, nMaxZ + 1);
            }
            else
            {
                _hasActive = false;
            }
        }

        /// <summary>
        /// Clear all pheromones efficiently by zeroing only the active region.
        /// </summary>
        public void ClearAll()
        {
            EnsureInitialized();
            if (!_initialized) return;

            if (!_hasActive)
            {
                _field.Clear();
                _buffer.Clear();
                return;
            }

            foreach (var kv in _field)
            {
                var g = kv.Value;
                for (int x = _minX; x <= _maxX; x++)
                for (int y = _minY; y <= _maxY; y++)
                for (int z = _minZ; z <= _maxZ; z++)
                    g[x, y, z] = 0f;
            }

            _hasActive = false;
        }
    }
}