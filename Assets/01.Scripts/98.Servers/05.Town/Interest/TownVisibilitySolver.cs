// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 접속자별 가시 집합은 비대칭이다(A는 B를 보지만 B는 A를 못 볼 수 있다). Mirror는 객체마다 관측자 목록을 따로 들고
    // 그 목록에 든 연결에만 스폰과 동기화를 보내므로 한 방향 가시도 문제없다. 그래서 접속자 V의 집합을 먼저 구하고,
    // 이를 뒤집어 객체 P의 관측자 목록(P를 보는 V들)을 만든다. 초기 스폰 판정과 재구축이 같은 표를 읽는다
    /// <summary> 마을 접속자의 시야 격자와 가까운 K명 선택. Unity에 의존하지 않고, 갱신 중 할당이 없다 </summary>
    public sealed class TownVisibilitySolver
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CELL_NEIGHBOR_RANGE  = 1;
        private const int BUFFER_GROWTH_FACTOR = 2;
        private const int CELL_KEY_SHIFT_BITS  = 32;

        private readonly float _radiusSqr;
        private readonly float _inverseCell;
        private readonly int   _maxVisible;

        private readonly List<int>                   _ids         = new List<int>();
        private readonly List<float>                 _xs          = new List<float>();
        private readonly List<float>                 _zs          = new List<float>();
        private readonly Dictionary<int, int>        _indexById;
        private readonly Dictionary<long, List<int>> _cells       = new Dictionary<long, List<int>>();
        private readonly Stack<List<int>>            _cellPool    = new Stack<List<int>>();
        private readonly List<List<int>>             _visible     = new List<List<int>>();
        private readonly List<List<int>>             _observers   = new List<List<int>>();
        private readonly List<int>                   _peerScratch = new List<int>();

        private float[] _keys  = new float[Constants.Town.AOI_SOLVER_INITIAL_CANDIDATES];
        private int[]   _items = new int[Constants.Town.AOI_SOLVER_INITIAL_CANDIDATES];

        // 접속자 색인별 필수 대상 표시. 값이 현재 _markStamp와 같으면 표시된 것이라 접속자마다 비울 필요가 없다
        private int[] _marks     = Array.Empty<int>();
        private int   _markStamp = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _ids.Count;

        /****************************************
        *                Methods
        ****************************************/

        public TownVisibilitySolver(float viewRadius, float cellSize, int maxVisible, int capacity)
        {
            if (viewRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(viewRadius));
            }

            if (cellSize < viewRadius)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "셀 크기는 시야 반경 이상이어야 한다");
            }

            _radiusSqr   = viewRadius * viewRadius;
            _inverseCell = 1f / cellSize;
            _maxVisible  = Math.Max(1, maxVisible);
            _indexById   = new Dictionary<int, int>(capacity);
        }

        /// <summary> 모든 접속자를 비운다. 만든 리스트는 다음 갱신에 다시 쓴다 </summary>
        public void Clear()
        {
            _ids.Clear();
            _xs.Clear();
            _zs.Clear();
            _indexById.Clear();

            foreach (List<int> cell in _cells.Values)
            {
                cell.Clear();
                _cellPool.Push(cell);
            }

            _cells.Clear();
        }

        /// <summary> 접속자를 넣는다. 같은 번호를 두 번 넣으면 뒤의 것을 무시한다 </summary>
        public void Add(int id, float x, float z)
        {
            if (_indexById.ContainsKey(id))
            {
                return;
            }

            int index = _ids.Count;
            _indexById[id] = index;
            _ids.Add(id);
            _xs.Add(x);
            _zs.Add(z);

            long key = CellKey(CellOf(x), CellOf(z));
            if (!_cells.TryGetValue(key, out List<int> cell))
            {
                cell = (_cellPool.Count > 0) ? _cellPool.Pop() : new List<int>();
                _cells[key] = cell;
            }

            cell.Add(index);
        }

        /// <summary> 넣은 접속자 전원의 가시 집합과 그 역방향 표를 계산한다. party는 비워도 된다 </summary>
        public void Compute(ITownPartyPeers party)
        {
            int count = _ids.Count;
            while (_visible.Count < count)
            {
                _visible.Add(new List<int>());
                _observers.Add(new List<int>());
            }

            for (int i = 0; i < count; ++i)
            {
                _visible[i].Clear();
                _observers[i].Clear();
            }

            if (_marks.Length < count)
            {
                // 늘어난 칸은 0이고 _markStamp는 항상 1 이상이라 표시로 오인되지 않는다
                Array.Resize(ref _marks, Math.Max(count, _marks.Length * BUFFER_GROWTH_FACTOR));
            }

            for (int viewer = 0; viewer < count; ++viewer)
            {
                ComputeViewer(viewer, party);
            }

            for (int viewer = 0; viewer < count; ++viewer)
            {
                List<int> visible = _visible[viewer];
                for (int i = 0; i < visible.Count; ++i)
                {
                    _observers[visible[i]].Add(viewer);
                }
            }
        }

        public bool Contains(int id)
        {
            return _indexById.ContainsKey(id);
        }

        /// <summary> viewer가 target을 보는지, 둘 중 하나라도 풀에 없으면 거짓. 본인은 항상 참이다 </summary>
        public bool IsVisible(int viewerId, int targetId)
        {
            if ((!_indexById.TryGetValue(viewerId, out int viewer)) || (!_indexById.TryGetValue(targetId, out int target)))
            {
                return false;
            }

            List<int> visible = _visible[viewer];
            for (int i = 0; i < visible.Count; ++i)
            {
                if (visible[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> target을 보는 접속자 번호(본인 포함)를 into에 더한다 </summary>
        public void GetObserversOf(int targetId, List<int> into)
        {
            if (!_indexById.TryGetValue(targetId, out int target))
            {
                return;
            }

            List<int> observers = _observers[target];
            for (int i = 0; i < observers.Count; ++i)
            {
                into.Add(_ids[observers[i]]);
            }
        }

        /// <summary> 한 점에서 시야 반경 안에 있는 접속자 번호를 into에 더한다. 플레이어가 아닌 마을 객체용이다 </summary>
        public void GetViewersNear(float x, float z, List<int> into)
        {
            int cx = CellOf(x);
            int cz = CellOf(z);
            for (int dx = -CELL_NEIGHBOR_RANGE; dx <= CELL_NEIGHBOR_RANGE; ++dx)
            {
                for (int dz = -CELL_NEIGHBOR_RANGE; dz <= CELL_NEIGHBOR_RANGE; ++dz)
                {
                    if (!_cells.TryGetValue(CellKey(cx + dx, cz + dz), out List<int> cell))
                    {
                        continue;
                    }

                    for (int i = 0; i < cell.Count; ++i)
                    {
                        int index = cell[i];
                        float ox = _xs[index] - x;
                        float oz = _zs[index] - z;
                        if (((ox * ox) + (oz * oz)) <= _radiusSqr)
                        {
                            into.Add(_ids[index]);
                        }
                    }
                }
            }
        }

        // 본인과 파티원을 먼저 넣고, 남는 자리를 반경 안 가까운 순서로 채운다. 필수 대상만으로 상한을 넘어도 자르지 않는다
        private void ComputeViewer(int viewer, ITownPartyPeers party)
        {
            List<int> visible = _visible[viewer];
            visible.Add(viewer);

            if (_markStamp == int.MaxValue)
            {
                Array.Clear(_marks, 0, _marks.Length);
                _markStamp = 0;
            }

            ++_markStamp;
            _marks[viewer] = _markStamp;

            if (party != null)
            {
                _peerScratch.Clear();
                if (party.TryGetPeers(_ids[viewer], _peerScratch))
                {
                    for (int i = 0; i < _peerScratch.Count; ++i)
                    {
                        if ((_indexById.TryGetValue(_peerScratch[i], out int peer)) && (_marks[peer] != _markStamp))
                        {
                            _marks[peer] = _markStamp;
                            visible.Add(peer);
                        }
                    }
                }
            }

            int mandatory = visible.Count;
            float vx = _xs[viewer];
            float vz = _zs[viewer];
            int cx = CellOf(vx);
            int cz = CellOf(vz);
            int candidates = 0;

            for (int dx = -CELL_NEIGHBOR_RANGE; dx <= CELL_NEIGHBOR_RANGE; ++dx)
            {
                for (int dz = -CELL_NEIGHBOR_RANGE; dz <= CELL_NEIGHBOR_RANGE; ++dz)
                {
                    if (!_cells.TryGetValue(CellKey(cx + dx, cz + dz), out List<int> cell))
                    {
                        continue;
                    }

                    for (int i = 0; i < cell.Count; ++i)
                    {
                        int other = cell[i];
                        float ox = _xs[other] - vx;
                        float oz = _zs[other] - vz;
                        float distanceSqr = (ox * ox) + (oz * oz);
                        if ((distanceSqr > _radiusSqr) || (_marks[other] == _markStamp))
                        {
                            continue;
                        }

                        if (candidates == _keys.Length)
                        {
                            Array.Resize(ref _keys, candidates * BUFFER_GROWTH_FACTOR);
                            Array.Resize(ref _items, candidates * BUFFER_GROWTH_FACTOR);
                        }

                        _keys[candidates]  = distanceSqr;
                        _items[candidates] = other;
                        ++candidates;
                    }
                }
            }

            int slots = Math.Max(0, _maxVisible - mandatory);
            if (candidates > slots)
            {
                Array.Sort(_keys, _items, 0, candidates);
                candidates = slots;
            }

            for (int i = 0; i < candidates; ++i)
            {
                visible.Add(_items[i]);
            }
        }

        private int CellOf(float value)
        {
            return (int)Math.Floor(value * _inverseCell);
        }

        private static long CellKey(int cx, int cz)
        {
            return ((long)cx << CELL_KEY_SHIFT_BITS) | (uint)cz;
        }
    }
}
