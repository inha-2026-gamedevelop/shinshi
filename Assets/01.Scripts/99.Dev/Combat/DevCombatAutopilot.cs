#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Dev
{
    /// <summary> 개발 클라이언트가 소유 플레이어로 기본 공격, 회피, 스킬 0~3을 차례로 요청해 전투 연출을 반복 확인한다 </summary>
    public sealed class DevCombatAutopilot : MonoBehaviour
    {
        // 판정은 서버가 하므로 실제 입력 판독기가 쓰는 같은 요청 창구(PlayerInputState)만 쓴다. 조준은 현재 바라보는 방향이다

        /****************************************
        *                Fields
        ****************************************/

        private const string ARG_COMBAT_AUTOPILOT = "-dev-combat-autopilot";

        private const double ACTION_INTERVAL_SECONDS = 1.5;
        private const int    ACTION_BASIC            = 0;
        private const int    ACTION_DODGE            = 1;
        private const int    ACTION_SKILL_FIRST      = 2;
        private const int    SKILL_SLOT_COUNT        = 4;
        private const int    ACTION_COUNT            = ACTION_SKILL_FIRST + SKILL_SLOT_COUNT;

        private PlayerInputState _input      = null;
        private PlayerCombatState _combat    = null;
        private int              _nextAction = 0;
        private double           _nextTime   = 0.0;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (!DevArgs.Has(ARG_COMBAT_AUTOPILOT))
            {
                return;
            }

            var host = new GameObject(nameof(DevCombatAutopilot));
            DontDestroyOnLoad(host);
            host.AddComponent<DevCombatAutopilot>();
        }

        private void Update()
        {
            if ((_input == null) || (_combat == null))
            {
                TryBind();
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (now < _nextTime)
            {
                return;
            }

            _nextTime = now + ACTION_INTERVAL_SECONDS;
            SendAction(_nextAction);
            _nextAction = (_nextAction + 1) % ACTION_COUNT;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TryBind()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return;
            }

            if ((!localPlayer.TryGetComponent(out _input)) || (!localPlayer.TryGetComponent(out _combat)))
            {
                _input  = null;
                _combat = null;
                return;
            }

            _nextAction = 0;
            _nextTime   = Time.unscaledTimeAsDouble + ACTION_INTERVAL_SECONDS;
            Debug.Log("[CombatAutopilot] 시작");
        }

        private void SendAction(int action)
        {
            Vector3 aim = FacingRules.DecodeYaw(_combat.FacingYaw);

            if (action == ACTION_BASIC)
            {
                _input.RequestAttack(aim);
                Debug.Log("[CombatAutopilot] basic attack");
            }
            else if (action == ACTION_DODGE)
            {
                _input.RequestDodge(aim);
                Debug.Log("[CombatAutopilot] dodge");
            }
            else
            {
                byte slot = (byte)(action - ACTION_SKILL_FIRST);
                _input.RequestSkill(ESkillRequestKind.Regular, slot, _input.transform.position, aim);
                Debug.Log("[CombatAutopilot] skill " + slot);
            }
        }
    }
}
#endif
