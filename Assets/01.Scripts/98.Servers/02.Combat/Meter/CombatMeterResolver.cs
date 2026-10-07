// System
using System;

namespace Shinshi.Combat
{
    /// <summary> 판정 결과를 바꾸지 않고 실제 적용 피해만 기록하는 피해 창구 데코레이터 </summary>
    public sealed class CombatMeterResolver : IDamageResolver
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IDamageResolver     _inner;
        private readonly CombatMeterRecorder _recorder;
        private readonly DamageResolver      _clock;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterResolver(IDamageResolver inner, CombatMeterRecorder recorder, DamageResolver clock)
        {
            _inner    = inner ?? throw new ArgumentNullException(nameof(inner));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _clock    = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public int IssueInstanceId() => _inner.IssueInstanceId();

        public DamageResult Apply(in DamageRequest request)
        {
            // Capture와 Record는 기록기 안에서 예외를 격리하므로 판정 호출과 반환은 항상 유지된다.
            CombatMeterHitContext context = _recorder.Capture(in request);
            DamageResult         result  = _inner.Apply(in request);
            _recorder.Record(in request, in result, in context, _clock.Now);
            return result;
        }
    }
}
