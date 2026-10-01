// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> GameDB 도메인 SO의 공통 기반, 스키마 버전과 검증 진입점을 둔다 </summary>
    public abstract class GameDataSO : ScriptableObject, IDataValidatable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 필드 구조를 바꾸면 올린다. 서버와 클라이언트의 콘텐츠 비교, 저장 데이터 이관 판단에 쓴다
        [SerializeField] private int _schemaVersion = 1;

        public int SchemaVersion => _schemaVersion;

        /****************************************
        *                Methods
        ****************************************/

        public void Validate(List<string> errors)
        {
            DataValidation.RequirePositive(errors, GetType().Name, nameof(_schemaVersion), _schemaVersion);
            ValidateValues(errors);
        }

        /// <summary> 이 SO 안의 값만 검사한다 </summary>
        protected abstract void ValidateValues(List<string> errors);

        /// <summary> 다른 카탈로그의 ID를 참조하면 재정의한다. 모든 SO의 값 검사가 통과한 뒤에만 불린다 </summary>
        public virtual void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
        }

        // 인스펙터에서 값을 바꾸는 즉시 오류를 보여 준다. 재정의하면 base를 호출한다
        protected virtual void OnValidate()
        {
            var errors = new List<string>();
            Validate(errors);

            for (int i = 0; i < errors.Count; ++i)
            {
                Debug.LogError($"[GameDB] {errors[i]}", this);
            }
        }
    }
}
