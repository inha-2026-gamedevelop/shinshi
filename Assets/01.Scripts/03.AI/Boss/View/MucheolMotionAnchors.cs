// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 사건마다 아는 시각이 달라서(예고 사건은 타 시각, 돌진 사건은 돌진 시각) 모르는 값은 NaN으로 둔다.
    // 모르는 기준에 기대는 키가 있으면 그 구간은 만들지 않는다
    /// <summary> 한 시전의 모션 시간 키를 실제 서버 시각으로 바꾸는 기준 시각 묶음 </summary>
    public struct MucheolMotionAnchors
    {
        /****************************************
        *              Properties
        ****************************************/

        public double CastStart { get; set; }

        public double TelegraphEnd { get; set; }

        /// <summary> 타별 확정 서버 시각, 비어 있으면 TelegraphEnd를 0번째 타로 본다 </summary>
        public IReadOnlyList<double> Strikes { get; set; }

        public double DashStart { get; set; }

        public double DashEnd { get; set; }

        public double Event { get; set; }

        public double GroggyEnd { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 모든 기준 시각을 모름으로 둔 묶음 </summary>
        public static MucheolMotionAnchors Unknown()
        {
            return new MucheolMotionAnchors
            {
                CastStart    = double.NaN,
                TelegraphEnd = double.NaN,
                Strikes      = null,
                DashStart    = double.NaN,
                DashEnd      = double.NaN,
                Event        = double.NaN,
                GroggyEnd    = double.NaN
            };
        }

        /// <summary> 키의 기준 시각, 모르면 NaN이다 </summary>
        public readonly double Resolve(in MucheolMotionKey key)
        {
            switch (key.Anchor)
            {
                case EMucheolMotionAnchor.CastStart:
                    return CastStart;
                case EMucheolMotionAnchor.TelegraphEnd:
                    return TelegraphEnd;
                case EMucheolMotionAnchor.Strike:
                    return ResolveStrike(key.StrikeIndex);
                case EMucheolMotionAnchor.DashStart:
                    return DashStart;
                case EMucheolMotionAnchor.DashEnd:
                    return DashEnd;
                case EMucheolMotionAnchor.Event:
                    return Event;
                case EMucheolMotionAnchor.GroggyEnd:
                    return GroggyEnd;
                default:
                    return double.NaN;
            }
        }

        // 단발 패턴(부채꼴, 원형, 전장 공격)은 타 목록 없이 예고 끝에 판정한다
        private readonly double ResolveStrike(int index)
        {
            if ((Strikes == null) || (Strikes.Count == 0))
            {
                return (index == 0) ? TelegraphEnd : double.NaN;
            }

            return ((index >= 0) && (index < Strikes.Count)) ? Strikes[index] : double.NaN;
        }
    }
}
