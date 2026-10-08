// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 경제 페이지가 시나리오 값을 채우고 그리는 공용 도우미, 없는 키만 채우고 사용자 값은 덮어쓰지 않는다 </summary>
    public static class EconomyScenarioValues
    {
        /****************************************
        *                Methods
        ****************************************/

        public static void SeedInt(UiSandboxScenarioSO scenario, string key, int value)
        {
            if (!scenario.Contains(key))
            {
                scenario.SetInt(key, value);
            }
        }

        public static void SeedLong(UiSandboxScenarioSO scenario, string key, long value)
        {
            if (!scenario.Contains(key))
            {
                scenario.SetLong(key, value);
            }
        }

        public static void SeedBool(UiSandboxScenarioSO scenario, string key, bool value)
        {
            if (!scenario.Contains(key))
            {
                scenario.SetBool(key, value);
            }
        }

        public static void SeedString(UiSandboxScenarioSO scenario, string key, string value)
        {
            if (!scenario.Contains(key))
            {
                scenario.SetString(key, value);
            }
        }

        public static int DrawInt(UiSandboxScenarioSO scenario, string key, string label, int min, int max)
        {
            int current = scenario.GetInt(key);
            int edited  = Mathf.Clamp(EditorGUILayout.IntField(label, current), min, max);
            if (edited != current)
            {
                scenario.SetInt(key, edited);
            }

            return edited;
        }

        public static long DrawLong(UiSandboxScenarioSO scenario, string key, string label, long min, long max)
        {
            long current = scenario.GetLong(key);
            long edited  = Math.Min(Math.Max(EditorGUILayout.LongField(label, current), min), max);
            if (edited != current)
            {
                scenario.SetLong(key, edited);
            }

            return edited;
        }

        public static bool DrawBool(UiSandboxScenarioSO scenario, string key, string label)
        {
            bool current = scenario.GetBool(key);
            bool edited  = EditorGUILayout.Toggle(label, current);
            if (edited != current)
            {
                scenario.SetBool(key, edited);
            }

            return edited;
        }

        public static string DrawString(UiSandboxScenarioSO scenario, string key, string label)
        {
            string current = scenario.GetString(key);
            string edited  = EditorGUILayout.TextField(label, current);
            if (edited != current)
            {
                scenario.SetString(key, edited);
            }

            return edited;
        }

        /// <summary> 정수 값을 이름 목록에서 고르는 팝업, 값은 목록 번호다 </summary>
        public static int DrawPopup(UiSandboxScenarioSO scenario, string key, string label, string[] options)
        {
            int current = Mathf.Clamp(scenario.GetInt(key), 0, options.Length - 1);
            int edited  = EditorGUILayout.Popup(label, current, options);
            if (edited != scenario.GetInt(key))
            {
                scenario.SetInt(key, edited);
            }

            return edited;
        }
    }
}
