// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 월백 기믹 사건 값의 표본이다. 값마다 다른 숫자를 넣어 필드가 뒤섞이면 비교에서 드러나게 한다
    /// <summary> 월백 기믹 직렬화와 기록 시험의 표본 값 </summary>
    public static class BossWolbaekGimmickSamples
    {
        /****************************************
        *              Constants
        ****************************************/

        public const int    BOSS_ID   = 900;
        public const int    MEMBER_A  = 101;
        public const int    MEMBER_B  = 102;
        public const double START     = 1000.0;
        public const int    PAIR      = 2;
        public const int    YELLOWS   = 3;

        /****************************************
        *                Methods
        ****************************************/

        public static WolbaekTalismanStart Talisman(int castId)
        {
            var structures = new[]
            {
                new WolbaekTalismanStructure(0, EWolbaekTalismanColor.Red, 1, new Vector3(10f, 0f, 0f), 3f, MEMBER_A),
                new WolbaekTalismanStructure(2, EWolbaekTalismanColor.Green, 3, new Vector3(-10f, 0f, 0f), 3.5f, MEMBER_B)
            };
            return new WolbaekTalismanStart(BOSS_ID, castId, START, START + 3.0, 5f, new Vector3(1f, 0f, 2f), structures);
        }

        public static WolbaekTalismanAttack LineAttack(int attackId, double telegraphAt)
        {
            return WolbaekTalismanAttack.Line(attackId, telegraphAt, telegraphAt + 1.2, new Vector3(1f, 0f, 1f), Vector3.right, 12f,
                3f, MEMBER_B);
        }

        public static WolbaekTalismanAttack RingAttack(int attackId, double telegraphAt)
        {
            return WolbaekTalismanAttack.Ring(attackId, telegraphAt, telegraphAt + 1.2, new Vector3(2f, 0f, 3f), 7f, 10f);
        }

        public static WolbaekWardStart Ward(int castId)
        {
            var members = new[]
            {
                new WolbaekWardMember(MEMBER_A, EWolbaekWardColor.Red, 4),
                new WolbaekWardMember(MEMBER_B, EWolbaekWardColor.Blue, 5)
            };
            return new WolbaekWardStart(BOSS_ID, castId, START, new Vector3(0f, 0f, 1f), 5f, 8f, 3f, 6f, 9f, members);
        }

        public static WolbaekWardStage Stage(int index, double startTime)
        {
            return new WolbaekWardStage(index, startTime, startTime + 4.0, 2f, 3, new Vector3(5f, 0f, 0f), new Vector3(-5f, 0f, 0f),
                3f);
        }

        public static WolbaekWardStrike Strike(int stageIndex, int strikeIndex, double telegraphAt)
        {
            return new WolbaekWardStrike(stageIndex, strikeIndex, telegraphAt, telegraphAt + 1.5, new Vector3(0f, 0f, 4f), 1.5f);
        }

        public static WolbaekSealStart Seal(int castId)
        {
            return new WolbaekSealStart(BOSS_ID, castId, START, START + 60.0, 3, 3f, 4f, 2f, 1f, 1.5f, 1.25f,
                new Vector3(0f, 0f, -1f));
        }

        public static WolbaekSealTurn Turn(int index, double startTime)
        {
            var cores = new[]
            {
                new WolbaekSealCore(0, new Vector3(6f, 0f, 0f)),
                new WolbaekSealCore(1, new Vector3(-6f, 0f, 0f))
            };
            var foxfires = new[]
            {
                new WolbaekSealFoxfire(0, MEMBER_A),
                new WolbaekSealFoxfire(1, MEMBER_B)
            };
            return new WolbaekSealTurn(index, startTime, startTime + 3.0, startTime + 7.0, startTime + 9.0, startTime + 10.0, cores,
                foxfires);
        }

        public static WolbaekSealSweep Sweep(int turnIndex, int sweepIndex, double telegraphAt)
        {
            return new WolbaekSealSweep(turnIndex, sweepIndex, telegraphAt, telegraphAt + 0.8, new Vector3(0f, 0f, 1f), Vector3.forward,
                16f, 60f, 1f);
        }

        public static WolbaekFinalStart Final(int castId)
        {
            return new WolbaekFinalStart(BOSS_ID, castId, START, START + 4.0, 8f, YELLOWS, 8000f, 4f, 2f, 2f,
                new Vector3(0f, 0f, 3f));
        }

        public static WolbaekFinalYellow[] Yellows()
        {
            var yellows = new WolbaekFinalYellow[YELLOWS];
            for (int i = 0; i < YELLOWS; ++i)
            {
                yellows[i] = new WolbaekFinalYellow(i, 500 + i, new Vector3(i, 0f, 1f), 8000f);
            }

            return yellows;
        }

        public static WolbaekFinalBundle Bundle(int index, double startTime)
        {
            var reds = new[]
            {
                new WolbaekFinalRed(0, MEMBER_A),
                new WolbaekFinalRed(1, MEMBER_B)
            };
            return new WolbaekFinalBundle(index, startTime, startTime + 4.0, startTime + 6.0, reds);
        }
    }
}
