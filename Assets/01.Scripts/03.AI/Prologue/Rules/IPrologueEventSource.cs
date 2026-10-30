// System
using System;

namespace Shinshi.AI
{
    /// <summary> 서버 세션이 프롤로그 진행기에 알리는 사건, 실제 구현은 서버 세션이 맡고 시험은 가짜로 대신한다 </summary>
    public interface IPrologueEventSource
    {
        // 기본 공격 사용, 명중 여부와 상관없다
        event Action AttackUsed;

        // Q 기술 사용, 명중 여부와 상관없다
        event Action SkillQUsed;

        // 처치한 적의 소환 번호
        event Action<int> EnemyKilled;

        event Action TelegraphStarted;
        event Action TelegraphEnded;

        // 플레이어 체력 0
        event Action PlayerDown;

        // 임시 물약 한 개를 실제로 썼다
        event Action PotionUsed;

        // 플레이어가 부적에 닿았다, 이 순간부터 조사 화면이 떠 있는 동안 입력을 막는다
        event Action TalismanApproached;

        // 부적 조사 화면을 닫았다
        event Action TalismanConfirmed;

        // 월백 등장 일러스트를 닫거나 건너뛰었다, 이 뒤에 서버가 월백을 소환하고 플레이어를 옮긴다
        event Action WolbaekIntroFinished;

        // 월백 등장 연출이 끝나 조작이 가능해졌다
        event Action WolbaekControlStarted;

        // 도입이나 마무리 일러스트 구간이 끝났다, 어느 구간인지는 진행기의 현재 단계로 가른다
        event Action IllustrationFinished;
    }
}
