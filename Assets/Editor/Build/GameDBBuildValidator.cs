// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Editor
{
    /// <summary> 빌드 전에 GameDB를 검증하고 오류가 있으면 빌드를 멈춘다 </summary>
    public class GameDBBuildValidator : IPreprocessBuildWithReport
    {
        /****************************************
        *              Properties
        ****************************************/

        public int callbackOrder => 0;

        /****************************************
        *                Methods
        ****************************************/

        public void OnPreprocessBuild(BuildReport report)
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            if (root == null)
            {
                throw new BuildFailedException("[Build] GameDB 루트 에셋이 없다");
            }

            var errors = new List<string>();
            root.Validate(errors);

            // 기획 결정 전 값은 개발 빌드에서만 허용한다
            bool isDevelopment = (report.summary.options & BuildOptions.Development) != 0;
            if ((!isDevelopment) && (root.Session != null) && (root.Session.ServiceCancelExtraEssence == EExtraEssencePolicy.Undecided))
            {
                errors.Add("SessionConfigSO._serviceCancelExtraEssence: Undecided 값으로 배포 빌드를 만들 수 없다");
            }

            if (errors.Count == 0)
            {
                return;
            }

            for (int i = 0; i < errors.Count; ++i)
            {
                Debug.LogError($"[Build] {errors[i]}");
            }

            throw new BuildFailedException($"[Build] GameDB 검증 실패 {errors.Count}건, 콘솔을 확인한다");
        }
    }
}
