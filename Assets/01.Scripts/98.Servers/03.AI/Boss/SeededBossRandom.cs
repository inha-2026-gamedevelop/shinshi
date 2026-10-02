// System
using System;

namespace Shinshi.AI
{
    /// <summary> 시드를 받는 기본 난수, 같은 시드는 같은 선택을 낸다 </summary>
    public sealed class SeededBossRandom : IBossRandom
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Random _random;

        /****************************************
        *                Methods
        ****************************************/

        public SeededBossRandom(int seed)
        {
            _random = new Random(seed);
        }

        public float NextFloat01()
        {
            return (float)_random.NextDouble();
        }

        public int NextInt(int maxExclusive)
        {
            return _random.Next(maxExclusive);
        }
    }
}
