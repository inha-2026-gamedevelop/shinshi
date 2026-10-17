// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 정상 소멸 중인 효과도 세션 종료 시 즉시 회수할 수 있도록 소유 참조를 유지한다. </summary>
    public sealed class WolbaekEffectTails
    {
        private readonly List<GameObject> _effects = new List<GameObject>();

        public void Keep(GameObject effect)
        {
            for (int i = _effects.Count - 1; i >= 0; --i)
            {
                if (_effects[i] == null)
                {
                    _effects.RemoveAt(i);
                }
            }
            if ((effect != null) && (!_effects.Contains(effect)))
            {
                _effects.Add(effect);
            }
        }

        /// <summary> 다른 소유자에게 넘긴 효과를 목록에서 뺀다. 이후 Clear가 그 효과를 지우지 않는다 </summary>
        public void Release(GameObject effect)
        {
            _effects.Remove(effect);
        }

        public void Clear()
        {
            for (int i = 0; i < _effects.Count; ++i)
            {
                if (_effects[i] != null)
                {
                    _effects[i].SetActive(false);
                    Object.Destroy(_effects[i]);
                }
            }
            _effects.Clear();
        }
    }
}
