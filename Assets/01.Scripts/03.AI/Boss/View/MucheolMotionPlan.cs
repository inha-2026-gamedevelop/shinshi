// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 엔진 오브젝트를 만들지 않는 계산이라 EditMode에서 그대로 시험한다. Animator 재생은 MucheolMotionDirector가 한다
    /// <summary> 무철 모션 구간을 서버 시각으로 바꾸고, 어느 시각의 클립 시간과 재생 속도를 계산한다 </summary>
    public static class MucheolMotionPlan
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double TIME_EPSILON = 0.0001;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구간의 키를 서버 시각으로 바꾼다, 기준 시각을 모르는 키가 있으면 null이다 </summary>
        public static MucheolMotionTrack Resolve(in MucheolMotionSegment segment, in MucheolMotionAnchors anchors)
        {
            MucheolMotionKey[] keys = segment.Keys;
            if ((keys == null) || (keys.Length == 0) || string.IsNullOrEmpty(segment.ClipName))
            {
                return null;
            }

            var times = new double[keys.Length];
            var clips = new float[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                double anchor = anchors.Resolve(in keys[i]);
                if (double.IsNaN(anchor))
                {
                    return null;
                }

                // 그로기가 아주 짧으면 내려앉기 끝보다 일어나기 시작이 앞설 수 있다, 그때는 유지 없이 바로 이어 간다
                double time = anchor + keys[i].Offset;
                times[i] = (i > 0) ? Math.Max(time, times[i - 1]) : time;
                clips[i] = keys[i].ClipSeconds;
            }

            return new MucheolMotionTrack(segment.ClipName, times, clips);
        }

        /// <summary> now의 클립 시간과 그 순간의 클립 진행 속도(클립 초 / 실제 초), 첫 키 전과 마지막 키 뒤는 그 키에 멈춘 것으로 본다 </summary>
        public static void Sample(MucheolMotionTrack track, double now, out float clipSeconds, out float speed)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            int last = track.KeyCount - 1;
            if (now <= track.StartTime)
            {
                clipSeconds = track.StartClip;
                speed       = 0f;
                return;
            }

            if (now >= track.EndTime)
            {
                clipSeconds = track.EndClip;
                speed       = 0f;
                return;
            }

            for (int i = 1; i <= last; ++i)
            {
                double to = track.GetTime(i);
                if (now > to)
                {
                    continue;
                }

                double from     = track.GetTime(i - 1);
                float  fromClip = track.GetClip(i - 1);
                float  toClip   = track.GetClip(i);
                double span     = to - from;
                if (span <= TIME_EPSILON)
                {
                    clipSeconds = toClip;
                    speed       = 0f;
                    return;
                }

                double t = (now - from) / span;
                clipSeconds = (float)(fromClip + ((toClip - fromClip) * t));
                speed       = (float)((toClip - fromClip) / span);
                return;
            }

            clipSeconds = track.EndClip;
            speed       = 0f;
        }

        // 돌진이 예정보다 일찍 멈출 때 들이받는 구간을 그 시각에서 끊는다. 끊은 지점의 클립 시간을 마지막 키로 두어 다음 구간과 잇는다
        /// <summary> at에서 끊은 구간, at이 구간 시작 이전이거나 끝 이후이면 null이다 </summary>
        public static MucheolMotionTrack TruncateAt(MucheolMotionTrack track, double at)
        {
            if ((track == null) || double.IsNaN(at) || (at <= track.StartTime) || (at >= track.EndTime))
            {
                return null;
            }

            var times = new List<double>();
            var clips = new List<float>();
            for (int i = 0; i < track.KeyCount; ++i)
            {
                if (track.GetTime(i) >= at)
                {
                    break;
                }

                times.Add(track.GetTime(i));
                clips.Add(track.GetClip(i));
            }

            Sample(track, at, out float clipAt, out _);
            times.Add(at);
            clips.Add(clipAt);
            return new MucheolMotionTrack(track.ClipName, times.ToArray(), clips.ToArray());
        }

        /// <summary> at부터 seconds 동안 fromClip에서 toClip까지 가는 구간 </summary>
        public static MucheolMotionTrack Span(string clipName, double at, float fromClip, float toClip, float seconds)
        {
            return new MucheolMotionTrack(clipName, new[] { at, at + Math.Max(0f, seconds) }, new[] { fromClip, toClip });
        }

        /// <summary> 앞 구간의 끝과 다음 구간의 시작이 같은 클립의 같은 순간이면 섞지 않고 속도만 바꿔 이어 간다 </summary>
        public static bool IsContinuous(MucheolMotionTrack previous, MucheolMotionTrack next, float clipTolerance)
        {
            if ((previous == null) || (next == null) || (previous.ClipName != next.ClipName))
            {
                return false;
            }

            return Math.Abs(previous.EndClip - next.StartClip) <= clipTolerance;
        }
    }
}
