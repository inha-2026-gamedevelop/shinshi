// System
using System;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 무철 P06 돌진 결과를 받아 구조물 파괴를 확정하고 돌진 유도 기믹에 알린다. 충돌이 곧 파괴가 아니라서
    // 파괴를 정하는 일은 이 연결이 맡는다. 순서가 계약이다:
    //  1. 무철 중계로 돌진 결과(OnChargeEnded)를 먼저 보낸다. 표현은 충돌 반동을 본 뒤에 파괴를 받는다
    //  2. 구조물 충돌이고 기믹이 Active일 때만 조회에서 그 구조물을 부순다
    //  3. 파괴 사건(OnChargeStructureDestroyed)을 보낸다
    //  4. 기믹에 확인을 넘긴다. 마지막 지정 구조물이면 기믹이 그로기 대기를 알리고 중계가 파괴 사건 뒤에 보낸다
    //
    // 기믹 발동 전(Dormant)이나 끝난 뒤의 충돌은 부수지 않는다(임시 기본값, 기획 미정). 발동 전에 지정 구조물이 부서지면
    // 남은 지정 수가 필요 수보다 적어져 기믹을 끝낼 수 없고, 기믹이 2페이즈 전환을 보류하므로 전환이 영구히 막힐 수 있다.
    // 기믹이 Active인 동안에는 지정이 아닌 구조물도 부서지며 진행도에는 들어가지 않는다
    /// <summary> 무철 돌진 결과로 구조물 파괴를 확정하고 사건과 기믹 확인을 순서대로 보내는 서버 연결 </summary>
    public sealed class BossChargeStructureDirector : IBossChargeOutcomeListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossMucheolEventRelay    _relay;
        private readonly BossChargeStructureField _field;
        private readonly IBossEventRelayHost      _host;

        private MucheolChargeGimmick _gimmick; // 기믹은 팩토리보다 나중에 만들어져 BindGimmick으로 잇는다

        /****************************************
        *              Properties
        ****************************************/

        public BossChargeStructureField Field => _field;

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeStructureDirector(BossMucheolEventRelay relay, BossChargeStructureField field, IBossEventRelayHost host)
        {
            _relay = relay ?? throw new ArgumentNullException(nameof(relay));
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _host  = host ?? throw new ArgumentNullException(nameof(host));
        }

        // 기믹과 팩토리가 서로를 알아야 해서 기믹을 생성자로 받을 수 없다. 보스전이 시작되기 전에 한 번 잇는다
        /// <summary> 파괴 확인을 받을 돌진 유도 기믹을 잇는다, 비우면 아무것도 부수지 않는다 </summary>
        public void BindGimmick(MucheolChargeGimmick gimmick)
        {
            _gimmick = gimmick;
        }

        public void OnChargeEnded(int castInstanceId, EBossChargeEnd end, BossChargeCollision collision, float recoverySeconds)
        {
            ((IBossChargeOutcomeListener)_relay).OnChargeEnded(castInstanceId, end, collision, recoverySeconds);

            if ((end != EBossChargeEnd.Collision) || (collision.Kind != EBossChargeObstacleKind.Structure))
            {
                return;
            }

            if ((_gimmick == null) || (_gimmick.State != EMucheolChargeGimmickState.Active))
            {
                return;
            }

            int structureId = collision.StructureId;
            if (!_field.TryDestroy(structureId))
            {
                return;
            }

            _relay.NotifyChargeStructureDestroyed(castInstanceId, structureId);
            _gimmick.ConfirmStructureDestroyed(structureId, _host.TickTime, castInstanceId);
        }
    }
}
