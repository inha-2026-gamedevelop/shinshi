// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    /// <summary> 서버 세션 대신 사건을 직접 일으키고, 진행기가 시킨 일을 기록하는 시험용 가짜 </summary>
    public sealed class FakePrologueSession : IPrologueEventSource, IPrologueHost
    {
        /****************************************
        *                Fields
        ****************************************/

        public double Now = 100.0;

        public readonly List<(EPrologueEnemyKind Kind, int Index)> Spawns  = new List<(EPrologueEnemyKind, int)>();
        public readonly List<(EPrologueStage Stage, int Potions)>  Reports = new List<(EPrologueStage, int)>();
        public readonly List<float>                                Revives = new List<float>();
        public readonly List<EWolbaekEnd>                          Ends    = new List<EWolbaekEnd>();

        public int PotionStock = -1;
        public int WolbaekStarts = 0;

        /****************************************
        *              Events
        ****************************************/

        public event Action      AttackUsed;
        public event Action      SkillQUsed;
        public event Action<int> EnemyKilled;
        public event Action      TelegraphStarted;
        public event Action      TelegraphEnded;
        public event Action      PlayerDown;
        public event Action      PotionUsed;
        public event Action      TalismanConfirmed;
        public event Action      WolbaekIntroFinished;
        public event Action      WolbaekControlStarted;
        public event Action      IllustrationFinished;

        /****************************************
        *            Raise (event source)
        ****************************************/

        public void RaiseAttack()           { AttackUsed?.Invoke(); }
        public void RaiseSkillQ()           { SkillQUsed?.Invoke(); }
        public void RaiseKilled(int index)  { EnemyKilled?.Invoke(index); }
        public void RaiseTelegraphStart()   { TelegraphStarted?.Invoke(); }
        public void RaiseTelegraphEnd()     { TelegraphEnded?.Invoke(); }
        public void RaisePlayerDown()       { PlayerDown?.Invoke(); }
        public void RaisePotionUsed()       { PotionUsed?.Invoke(); }
        public void RaiseTalisman()         { TalismanConfirmed?.Invoke(); }
        public void RaiseWolbaekIntro()     { WolbaekIntroFinished?.Invoke(); }
        public void RaiseWolbaekControl()   { WolbaekControlStarted?.Invoke(); }
        public void RaiseIllustration()     { IllustrationFinished?.Invoke(); }

        // 구독자가 남았는지 확인하는 용도다
        public bool HasSubscribers => (AttackUsed != null) || (SkillQUsed != null) || (EnemyKilled != null)
            || (TelegraphStarted != null) || (TelegraphEnded != null) || (PlayerDown != null) || (PotionUsed != null)
            || (TalismanConfirmed != null) || (WolbaekIntroFinished != null) || (WolbaekControlStarted != null)
            || (IllustrationFinished != null);

        /****************************************
        *               Host
        ****************************************/

        public void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex) { Spawns.Add((kind, spawnIndex)); }
        public void SetPotions(int count)                                { PotionStock = count; }
        public void ReviveToFull(float invulnerableSeconds)              { Revives.Add(invulnerableSeconds); }
        public void StartWolbaek()                                       { ++WolbaekStarts; }
        public void EndEngagement(EWolbaekEnd reason)                    { Ends.Add(reason); }
        public void ReportStage(EPrologueStage stage, int wolbaekEntryPotions) { Reports.Add((stage, wolbaekEntryPotions)); }
    }
}
