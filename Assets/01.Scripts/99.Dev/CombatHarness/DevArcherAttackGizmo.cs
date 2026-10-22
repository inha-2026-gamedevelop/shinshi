// System
using System.Reflection;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> 궁수 기본 공격이 누구를 맞힐지 정하는 서버 판정을 씬 뷰에 그려 보여 주는 시험용 표시 </summary>
    [RequireComponent(typeof(DevHitFeedbackCombatHost))]
    public sealed class DevArcherAttackGizmo : MonoBehaviour
    {
        // 판정은 PlayerServer.TickArrow와 같은 식을 그대로 쓴다. 조준선(사거리 x 폭)에 몬스터 중심점이 들어오고 높이 차가 한도 안이면 맞은 것이고,
        // 그중 시전자에 가장 가까운 하나만 맞힌다. 몸 크기(충돌체)는 판정에 쓰이지 않고 중심점만 쓰이므로 둘을 함께 그려 비교하게 한다.
        // 게임 뷰에서 보려면 게임 뷰 위쪽의 기즈모 버튼을 켠다

        /****************************************
        *                Fields
        ****************************************/

        private const float LINE_LIFT       = 0.1f;  // m, 땅에 묻히지 않게 띄운다
        private const float POINT_RADIUS    = 0.12f; // m, 몬스터 중심점 표시 크기
        private const float LABEL_LIFT      = 2.2f;  // m
        private const float ASSIST_POINT_RADIUS = 0.2f; // m, 보정이 잡은 점 표시 크기
        private const int   CIRCLE_SEGMENTS = 32;
        private const float MIN_DIRECTION_SQR = 0.0001f;

        private static readonly Color _corridorColor = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color _hitColor      = new Color(1f, 0.2f, 0.2f, 1f);
        private static readonly Color _insideColor   = new Color(0.3f, 1f, 0.3f, 1f);
        private static readonly Color _missColor     = new Color(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color _arrowColor    = new Color(0.2f, 0.9f, 1f, 1f);
        private static readonly Color _assistConeColor  = new Color(0.7f, 0.4f, 1f, 1f);
        private static readonly Color _assistPointColor = new Color(1f, 0.2f, 0.9f, 1f);

        [SerializeField] private bool _showLabels = true;

        private DevHitFeedbackPlayer     _player;
        private DevHitFeedbackCombatHost _host;
        private FieldInfo _arrowActiveField = null;
        private FieldInfo _arrowOriginField = null;
        private FieldInfo _arrowAimField    = null;

        /****************************************
        *              Unity Event
        ****************************************/

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if ((!Application.isPlaying) || (!ResolveReferences()) || (_host.Server == null))
            {
                return;
            }

            PlayerConfigSO.BasicAttackSpec attack = GameDB.Player.GetBasicAttack(_host.PlayerClass);
            if ((attack.ArcAngle > 0f) || (attack.ProjectileSpeed <= 0f))
            {
                return;
            }

            Vector3 origin    = transform.position;
            Vector3 cursorAim = ReadAim();
            float   halfWidth = attack.ProjectileWidth * Constants.Combat.SKILL_HALF_WIDTH_SCALE;
            float   range     = attack.Range;

            DrawAssist(origin, cursorAim, range);

            // 실제로 나가는 방향은 보정이 잡았으면 보정된 쪽이다
            Vector3 aim = _host.HasAssistTarget ? _host.AttackAim : cursorAim;

            DrawCorridor(origin, aim, range, halfWidth);
            DrawTargets(origin, aim, range, halfWidth);
            DrawLiveArrow(halfWidth);
        }

        private bool ResolveReferences()
        {
            if (_host == null)
            {
                _host = GetComponent<DevHitFeedbackCombatHost>();
            }

            if (_player == null)
            {
                _player = GetComponent<DevHitFeedbackPlayer>();
            }

            return (_host != null) && (_player != null);
        }

        // 서버가 받는 조준과 같은 방향이다. 커서가 지면을 못 가리키면 바라보는 방향을 쓴다
        private Vector3 ReadAim()
        {
            Vector3 aim = transform.forward;
            if (_player.TryReadCursorPoint(out Vector3 cursor))
            {
                aim = cursor - transform.position;
            }

            aim.y = 0f;
            return (aim.sqrMagnitude > MIN_DIRECTION_SQR) ? aim.normalized : Vector3.forward;
        }

        // 보라 부채꼴이 보정 범위(커서 기준 좌우 반각)고, 마젠타 구와 선이 보정이 잡아 당긴 몬스터 중심이다. 하늘색 선은 원래 커서 방향이다
        private void DrawAssist(Vector3 origin, Vector3 cursorAim, float range)
        {
            Vector3 start = origin + (Vector3.up * LINE_LIFT);
            float   half  = _host.AimAssistHalfAngle;

            if (half > 0f)
            {
                Gizmos.color = _assistConeColor;
                Gizmos.DrawLine(start, start + (Quaternion.Euler(0f, half, 0f) * cursorAim * range));
                Gizmos.DrawLine(start, start + (Quaternion.Euler(0f, -half, 0f) * cursorAim * range));
            }

            Gizmos.color = _arrowColor;
            Gizmos.DrawLine(start, start + (cursorAim * range));

            if (_host.HasAssistTarget)
            {
                Vector3 point = _host.AssistPoint + (Vector3.up * LINE_LIFT);
                Gizmos.color = _assistPointColor;
                Gizmos.DrawLine(start, point);
                Gizmos.DrawSphere(point, ASSIST_POINT_RADIUS);
                Gizmos.DrawWireSphere(point, ASSIST_POINT_RADIUS * 2f);
            }
        }

        // 노란 직사각형이 화살이 맞힐 수 있는 띠다. 폭은 좌우 반폭의 두 배다
        private void DrawCorridor(Vector3 origin, Vector3 aim, float range, float halfWidth)
        {
            Vector3 lift  = Vector3.up * LINE_LIFT;
            Vector3 side  = new Vector3(aim.z, 0f, -aim.x) * halfWidth;
            Vector3 start = origin + lift;
            Vector3 end   = start + (aim * range);

            Gizmos.color = _corridorColor;
            Gizmos.DrawLine(start + side, end + side);
            Gizmos.DrawLine(start - side, end - side);
            Gizmos.DrawLine(start + side, start - side);
            Gizmos.DrawLine(end + side, end - side);
            Gizmos.DrawLine(start, end);
        }

        private void DrawTargets(Vector3 origin, Vector3 aim, float range, float halfWidth)
        {
            var   monsters  = DevHitFeedbackMonster.Alive;
            int   firstIdx  = -1;
            float firstAlong = float.MaxValue;

            for (int i = 0; i < monsters.Count; ++i)
            {
                if ((IsInCorridor(monsters[i], origin, aim, range, halfWidth, out float along)) && (along < firstAlong))
                {
                    firstAlong = along;
                    firstIdx   = i;
                }
            }

            for (int i = 0; i < monsters.Count; ++i)
            {
                DevHitFeedbackMonster monster = monsters[i];
                if (monster.IsDead)
                {
                    continue;
                }

                Vector3 position = monster.transform.position;
                bool    inside   = IsInCorridor(monster, origin, aim, range, halfWidth, out float along);

                Gizmos.color = (i == firstIdx) ? _hitColor : (inside ? _insideColor : _missColor);
                Vector3 point = position + (Vector3.up * LINE_LIFT);
                Gizmos.DrawSphere(point, POINT_RADIUS);

                // 몸 충돌체 윤곽. 중심점이 띠 밖이면 이 윤곽이 띠에 걸려도 맞지 않는다
                if (monster.TryGetComponent(out Collider body))
                {
                    float bodyRadius = Mathf.Max(body.bounds.extents.x, body.bounds.extents.z);
                    DrawCircle(point, bodyRadius);
                }

                if ((_showLabels) && ((i == firstIdx) || (inside) || (Vector3.Distance(origin, position) <= range)))
                {
                    DrawLabel(position, origin, aim, halfWidth, i == firstIdx, inside);
                }
            }
        }

        private static bool IsInCorridor(DevHitFeedbackMonster monster, Vector3 origin, Vector3 aim, float range, float halfWidth,
            out float along)
        {
            along = 0f;
            if (monster.IsDead)
            {
                return false;
            }

            Vector3 position = monster.transform.position;
            return (BasicAttackRules.TryProjectOnRay(origin, aim, position, range, halfWidth, out along))
                && (BasicAttackRules.IsWithinHeight(origin, position, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE));
        }

        private static void DrawLabel(Vector3 position, Vector3 origin, Vector3 aim, float halfWidth, bool isFirst, bool inside)
        {
            Vector3 delta   = position - origin;
            float   along2  = (delta.x * aim.x) + (delta.z * aim.z);
            float   lateral = Mathf.Abs((delta.x * aim.z) - (delta.z * aim.x));
            string  verdict = isFirst ? "명중" : (inside ? "띠 안(앞에 다른 적)" : "빗나감");

            UnityEditor.Handles.Label(position + (Vector3.up * LABEL_LIFT),
                $"{verdict}\n앞 {along2:0.0}m / 옆 {lateral:0.00}m (반폭 {halfWidth:0.00}m)");
        }

        // 날아가는 중인 화살의 현재 위치와 이번 틱에 훑는 선을 그린다. 서버 내부 상태라 반사로 읽는다
        private void DrawLiveArrow(float halfWidth)
        {
            if (_arrowActiveField == null)
            {
                const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
                _arrowActiveField = typeof(PlayerServer).GetField("_arrowActive", FLAGS);
                _arrowOriginField = typeof(PlayerServer).GetField("_arrowOrigin", FLAGS);
                _arrowAimField    = typeof(PlayerServer).GetField("_arrowAim", FLAGS);
            }

            if ((_arrowActiveField == null) || (_arrowOriginField == null) || (_arrowAimField == null)
                || (!(bool)_arrowActiveField.GetValue(_host.Server)))
            {
                return;
            }

            Vector3 position = (Vector3)_arrowOriginField.GetValue(_host.Server) + (Vector3.up * LINE_LIFT);
            Vector3 aim      = (Vector3)_arrowAimField.GetValue(_host.Server);

            Gizmos.color = _arrowColor;
            Gizmos.DrawSphere(position, halfWidth);
            Gizmos.DrawLine(position, position + (aim * halfWidth * 4f));
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            Vector3 previous = center + (Vector3.right * radius);
            for (int i = 1; i <= CIRCLE_SEGMENTS; ++i)
            {
                float   angle = (Mathf.PI * 2f * i) / CIRCLE_SEGMENTS;
                Vector3 next  = center + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif
    }
}
