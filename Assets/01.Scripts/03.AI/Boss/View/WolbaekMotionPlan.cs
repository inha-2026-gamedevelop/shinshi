// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 판정 시각에 클립의 힘 순간을 맞추는 계산과 P07 워프 표현 시간표다. 엔진 오브젝트를 만들지 않아 EditMode에서 그대로 시험한다
    // 시간표 값은 시험 씬에서 사용자가 확정한 값이고 서버 값이 아니라 표현 상수다
    /// <summary> 모션 시작 지연과 재생 속도, 돌진 배속, 워프 표현 단계를 계산한다 </summary>
    public static class WolbaekMotionPlan
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 초, 확정한 돌진 클립(move_Flying_front)의 길이 </summary>
        public const float DASH_CLIP_SECONDS = 1.333f;

        /// <summary> 돌진 재생 속도 상한, 시험으로 조정할 수 있는 시작값이다 </summary>
        public const float DASH_MAX_SPEED = 4f;

        /// <summary> 초, 돌진을 마친 뒤 재생 속도를 1배로 되돌리며 Idle로 섞는 시간 </summary>
        public const float DASH_RECOVER_SECONDS = 0.5f;

        /// <summary> m, 이보다 짧은 돌진은 모션 없이 곧장 복귀한다 </summary>
        public const float DASH_MIN_DISTANCE = 0.01f;

        /// <summary> 초, 워프 시전 시작부터 공중제비 한가운데서 분해하고 모델을 숨기는 시각 </summary>
        public const float WARP_BREAKUP_SECONDS = 1f;

        /// <summary> 초, 착지 위치에서 재결합을 시작하는 시각 </summary>
        public const float WARP_REFORM_SECONDS = 1.15f;

        /// <summary> 초, 모델을 다시 보이는 시각 </summary>
        public const float WARP_SHOW_SECONDS = 1.4f;

        /// <summary> 초, 클립 기준 힘 순간 뒤 이 시간 안에서만 늦게 받은 모션을 이어서 재생한다. 그 뒤는 이미 지난 일회성 동작이라 다시 보이지 않는다 </summary>
        public const float RESUME_TAIL_SECONDS = 1f;

        /// <summary> 초, 쓰러진 자세에서 디졸브를 시작하기 전에 정지하는 시간. 시험 씬 DeathSequencePreview의 holdDelay다 </summary>
        public const float DEATH_HOLD_SECONDS = 0.5f;

        /// <summary> 초, 디졸브 컨트롤러의 소멸 지속 시간. 시험 씬 DissolveController의 duration이다 </summary>
        public const float DEATH_DISSOLVE_SECONDS = 3f;

        /// <summary> 초, 처치 클립 상태에 들어갔는지 애니메이터가 알려 주기를 기다리는 시간 </summary>
        public const float DEATH_ENTER_SECONDS = 0.15f;

        /// <summary> 초, 처치 상태가 끝나지 않아도 정지 대기로 넘어가는 한계. 컨트롤러에 상태가 없을 때 연출이 멈추지 않게 한다 </summary>
        public const float DEATH_FALL_LIMIT_SECONDS = 6f;

        private const float MIN_SPEED = 0.01f;
        private const float HALF      = 0.5f;

        /****************************************
        *               Types
        ****************************************/

        /// <summary> 워프 시전 시작 기준 경과 시간에 따른 모델 표현 단계 </summary>
        public enum EWarpStage
        {
            /// <summary> 출발 위치에서 공중제비 중, 모델이 보인다 </summary>
            Somersault = 0,

            /// <summary> 분해되어 모델이 숨겨진 상태 </summary>
            Hidden = 1,

            /// <summary> 착지 위치에서 재결합 중, 모델은 아직 숨겨져 있다 </summary>
            Reforming = 2,

            /// <summary> 모델이 착지 위치에 보인다 </summary>
            Shown = 3
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 판정 시각까지의 시간(strikeSeconds)에 힘 순간을 맞춘 재생 속도와 시작 지연을 정한다 </summary>
        public static void ResolveStart(float strikeSeconds, in WolbaekMotionEntry entry, out float speed, out float delay)
        {
            // 시전 전체에 클립을 고르게 편다, 이 경우 strikeSeconds는 첫 판정이 아니라 시전 전체 길이다
            if ((entry.FitClipSeconds > 0f) && (strikeSeconds > 0f))
            {
                speed = entry.FitClipSeconds / strikeSeconds;
                delay = 0f;
                return;
            }

            if (entry.ForcedSpeed > 0f)
            {
                speed = entry.ForcedSpeed;
                delay = Mathf.Max(0f, strikeSeconds - (entry.ClipPeakSeconds / speed));
                return;
            }

            // 힘 순간이 판정 시각보다 늦으면 재생 속도를 올려 맞춘다, 이르면 그만큼 늦게 시작한다
            if ((strikeSeconds > 0f) && (entry.ClipPeakSeconds > strikeSeconds))
            {
                speed = entry.ClipPeakSeconds / strikeSeconds;
                delay = 0f;
                return;
            }

            speed = 1f;
            delay = Mathf.Max(0f, strikeSeconds - entry.ClipPeakSeconds);
        }

        /// <summary> 느리게 늘리는 구간 안이면 구간 속도를, 아니면 기본 속도를 돌려준다. clipTime은 클립 기준 초다 </summary>
        public static float ResolveSpeed(in WolbaekMotionEntry entry, float baseSpeed, float clipTime)
        {
            if (!entry.HasSlowSegment)
            {
                return baseSpeed;
            }

            bool isInside = (clipTime >= entry.SlowFromClip) && (clipTime <= entry.SlowToClip);
            return isInside ? entry.SlowSpeed : baseSpeed;
        }

        /// <summary> 모션을 시작한 뒤 실제로 흐른 시간(초)에 해당하는 클립 시간. 느리게 늘리는 구간의 속도까지 반영한다 </summary>
        public static float ResolveClipTimeAt(in WolbaekMotionEntry entry, float baseSpeed, float elapsedSeconds)
        {
            float elapsed = Mathf.Max(0f, elapsedSeconds);
            float speed   = Mathf.Max(MIN_SPEED, baseSpeed);

            if (!entry.HasSlowSegment)
            {
                return elapsed * speed;
            }

            float beforeSlow = entry.SlowFromClip / speed;
            if (elapsed <= beforeSlow)
            {
                return elapsed * speed;
            }

            float slowReal = (entry.SlowToClip - entry.SlowFromClip) / entry.SlowSpeed;
            float inSlow   = elapsed - beforeSlow;
            if (inSlow <= slowReal)
            {
                return entry.SlowFromClip + (inSlow * entry.SlowSpeed);
            }

            return entry.SlowToClip + ((inSlow - slowReal) * speed);
        }

        /// <summary> 늦게 받은 모션을 이어서 재생해도 되는가. 힘 순간에서 꼬리 구간을 지났으면 이미 끝난 동작이다 </summary>
        public static bool IsResumable(in WolbaekMotionEntry entry, float clipTime)
        {
            return clipTime <= (entry.ClipPeakSeconds + RESUME_TAIL_SECONDS);
        }

        /// <summary> 돌진 발동 뒤 흐른 시간(초)이 서버가 정한 돌진 끝 전이면 이어서 재생한다 </summary>
        public static bool IsDashResumable(float executeAgeSeconds, float dashEndAgeSeconds)
        {
            return executeAgeSeconds < dashEndAgeSeconds;
        }

        /// <summary> 돌진 시간(거리 / 서버 속도)에 맞춘 재생 속도, 상한을 넘으면 상한으로 고정한다 </summary>
        public static float ResolveDashSpeed(float dashSeconds)
        {
            if (dashSeconds <= MIN_SPEED)
            {
                return DASH_MAX_SPEED;
            }

            return Mathf.Min(DASH_CLIP_SECONDS / dashSeconds, DASH_MAX_SPEED);
        }

        /// <summary> 돌진 뒤 Idle로 섞는 길이, 재생 속도가 높으면 전환이 실제보다 빨리 끝나므로 평균 속도만큼 늘린다 </summary>
        public static float ResolveDashBlendSeconds(float dashPlaySpeed)
        {
            return DASH_RECOVER_SECONDS * ((dashPlaySpeed + 1f) * HALF);
        }

        /// <summary> 돌진 복귀 중 경과 비율에서의 재생 속도, 돌진 속도에서 1배까지 ease-out으로 낮춘다 </summary>
        public static float ResolveDashRecoverSpeed(float dashPlaySpeed, float elapsedSeconds)
        {
            float t     = Mathf.Clamp01(elapsedSeconds / DASH_RECOVER_SECONDS);
            float eased = 1f - ((1f - t) * (1f - t));
            return Mathf.Lerp(dashPlaySpeed, 1f, eased);
        }

        /// <summary> 워프 시전 시작 기준 경과 시간의 모델 표현 단계 </summary>
        public static EWarpStage ResolveWarpStage(double elapsedSeconds)
        {
            if (elapsedSeconds < WARP_BREAKUP_SECONDS)
            {
                return EWarpStage.Somersault;
            }

            if (elapsedSeconds < WARP_REFORM_SECONDS)
            {
                return EWarpStage.Hidden;
            }

            return (elapsedSeconds < WARP_SHOW_SECONDS) ? EWarpStage.Reforming : EWarpStage.Shown;
        }
    }
}
