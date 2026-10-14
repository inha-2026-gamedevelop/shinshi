// System
using System;
using System.Security.Cryptography;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 부적 등록, 장비 강화, 장비 옵션 재설정(요청마다 네 번 호출)의 결과를 가르는 값이라 예측할 수 없는 난수를 쓴다. System.Random은 시드를 알면 다음 값을
    // 맞출 수 있다. 나머지 편향이 없도록 4바이트 값 중 균등하게 나눠지는 범위만 받는다. 메인 스레드에서만 부른다
    /// <summary> 마을 서버가 뽑는 확률 판정 값(0 이상 GrowthCodes.BASIS_POINTS 미만) </summary>
    public sealed class GrowthRollSource : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int ROLL_BYTES = 4;

        // BASIS_POINTS의 배수 중 uint 범위에서 가장 큰 값, 이 이상은 버리고 다시 뽑는다
        private const uint ACCEPT_LIMIT = uint.MaxValue - (uint.MaxValue % (uint)GrowthCodes.BASIS_POINTS);

        private readonly RandomNumberGenerator _random = RandomNumberGenerator.Create();
        private readonly byte[]                _buffer = new byte[ROLL_BYTES];

        /****************************************
        *                Methods
        ****************************************/

        public int Next()
        {
            uint value;
            do
            {
                _random.GetBytes(_buffer);
                value = BitConverter.ToUInt32(_buffer, 0);
            }
            while (value >= ACCEPT_LIMIT);

            return (int)(value % (uint)GrowthCodes.BASIS_POINTS);
        }

        public void Dispose()
        {
            _random.Dispose();
        }
    }
}
