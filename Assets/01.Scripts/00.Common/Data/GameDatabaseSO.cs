// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 모든 도메인 SO를 모으는 루트, Resources/GameDB.asset 하나만 둔다 </summary>
    [CreateAssetMenu(fileName = "GameDB", menuName = "Shinshi/GameDatabaseSO")]
    public class GameDatabaseSO : ScriptableObject, IDataValidatable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버와 클라이언트의 콘텐츠가 같은지 비교하는 값
        [SerializeField] private string _contentVersion = "0.1.0";

        [SerializeField] private PlayerConfigSO  _player;
        [SerializeField] private SessionConfigSO _session;

        public string          ContentVersion => _contentVersion;
        public PlayerConfigSO  Player         => _player;
        public SessionConfigSO Session        => _session;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 루트와 하위 설정을 모두 검사한다 </summary>
        public void Validate(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(_contentVersion))
            {
                errors.Add($"{nameof(GameDatabaseSO)}.{nameof(_contentVersion)}: 콘텐츠 버전이 비어 있다");
            }

            if (_player == null)
            {
                errors.Add($"{nameof(GameDatabaseSO)}.{nameof(_player)}: {nameof(PlayerConfigSO)} 참조가 없다");
            }
            else
            {
                _player.Validate(errors);
            }

            if (_session == null)
            {
                errors.Add($"{nameof(GameDatabaseSO)}.{nameof(_session)}: {nameof(SessionConfigSO)} 참조가 없다");
            }
            else
            {
                _session.Validate(errors);
            }
        }
    }
}
