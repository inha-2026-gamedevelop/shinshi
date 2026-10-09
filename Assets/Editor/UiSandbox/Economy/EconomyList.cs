// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 목록 한 칸의 값 종류 </summary>
    public enum EEconomyFieldKind
    {
        Text,
        Int,
        Long,
        Bool
    }

    /// <summary> 목록 한 줄이 가진 값 하나의 정의, 선택지가 있으면 문자열 팝업으로 그린다 </summary>
    public sealed class EconomyField
    {
        /****************************************
        *              Properties
        ****************************************/

        public string            Key          { get; }
        public string            Label        { get; }
        public EEconomyFieldKind Kind         { get; }
        public string            DefaultValue { get; }
        public long              Min          { get; }
        public long              Max          { get; }
        public string[]          Options      { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EconomyField(string key, string label, EEconomyFieldKind kind, string defaultValue,
            long min = 0, long max = int.MaxValue, string[] options = null)
        {
            Key          = key;
            Label        = label;
            Kind         = kind;
            DefaultValue = defaultValue;
            Min          = min;
            Max          = max;
            Options      = options;
        }
    }

    /// <summary> 개수와 번호 붙은 키로 저장하는 편집 가능한 목록의 모양, 키는 prefix.count와 prefix.번호.필드다 </summary>
    public sealed class EconomyListSchema
    {
        /****************************************
        *              Properties
        ****************************************/

        public string          Prefix     { get; }
        public string          Title      { get; }
        public int             MaxCount   { get; }
        public EconomyField[]  Fields     { get; }
        public string[][]      SampleRows { get; }
        public string          CountKey   => Prefix + ".count";

        /****************************************
        *                Methods
        ****************************************/

        public EconomyListSchema(string prefix, string title, int maxCount, EconomyField[] fields, string[][] sampleRows)
        {
            Prefix     = prefix;
            Title      = title;
            MaxCount   = maxCount;
            Fields     = fields;
            SampleRows = sampleRows ?? Array.Empty<string[]>();
        }

        public string KeyOf(int index, string field)
        {
            return Prefix + "." + index.ToString(CultureInfo.InvariantCulture) + "." + field;
        }
    }

    /// <summary> 시나리오에 개수와 번호 키로 저장한 목록을 읽고, 바꾸고, 인스펙터에 그린다 </summary>
    public static class EconomyList
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INDEX_TOKEN = "{n}";

        private static readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 없는 키만 채운다. 개수 키가 없으면 샘플 줄 수로 시작한다 </summary>
        public static void Seed(UiSandboxScenarioSO scenario, EconomyListSchema schema)
        {
            if (!scenario.Contains(schema.CountKey))
            {
                scenario.SetInt(schema.CountKey, schema.SampleRows.Length);
            }

            int count = Count(scenario, schema);
            for (int i = 0; i < count; ++i)
            {
                for (int f = 0; f < schema.Fields.Length; ++f)
                {
                    EconomyField field = schema.Fields[f];
                    string key = schema.KeyOf(i, field.Key);
                    if (!scenario.Contains(key))
                    {
                        WriteText(scenario, field, key, DefaultOf(schema, i, f));
                    }
                }
            }
        }

        public static int Count(UiSandboxScenarioSO scenario, EconomyListSchema schema)
        {
            return Mathf.Clamp(scenario.GetInt(schema.CountKey), 0, schema.MaxCount);
        }

        public static string GetText(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field)
        {
            return scenario.GetString(schema.KeyOf(index, field));
        }

        public static int GetInt(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field)
        {
            return scenario.GetInt(schema.KeyOf(index, field));
        }

        public static long GetLong(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field)
        {
            return scenario.GetLong(schema.KeyOf(index, field));
        }

        public static bool GetBool(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field)
        {
            return scenario.GetBool(schema.KeyOf(index, field));
        }

        public static void SetInt(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field, int value)
        {
            scenario.SetInt(schema.KeyOf(index, field), value);
        }

        public static void SetLong(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field, long value)
        {
            scenario.SetLong(schema.KeyOf(index, field), value);
        }

        public static void SetText(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field, string value)
        {
            scenario.SetString(schema.KeyOf(index, field), value);
        }

        public static void SetBool(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index, string field, bool value)
        {
            scenario.SetBool(schema.KeyOf(index, field), value);
        }

        /// <summary> 줄을 하나 늘려 기본값으로 채운다. 상한이면 거짓 </summary>
        public static bool Add(UiSandboxScenarioSO scenario, EconomyListSchema schema)
        {
            int count = Count(scenario, schema);
            if (count >= schema.MaxCount)
            {
                return false;
            }

            ResetRow(scenario, schema, count);
            scenario.SetInt(schema.CountKey, count + 1);
            return true;
        }

        public static void SetCount(UiSandboxScenarioSO scenario, EconomyListSchema schema, int value)
        {
            int target = Mathf.Clamp(value, 0, schema.MaxCount);
            int count  = Count(scenario, schema);
            for (int i = count; i < target; ++i)
            {
                ResetRow(scenario, schema, i);
            }

            scenario.SetInt(schema.CountKey, target);
        }

        /// <summary> 뒤 줄을 한 칸씩 당기고 개수를 줄인다 </summary>
        public static bool RemoveAt(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index)
        {
            int count = Count(scenario, schema);
            if ((index < 0) || (index >= count))
            {
                return false;
            }

            for (int i = index; i < (count - 1); ++i)
            {
                for (int f = 0; f < schema.Fields.Length; ++f)
                {
                    CopyField(scenario, schema, schema.Fields[f], i + 1, i);
                }
            }

            scenario.SetInt(schema.CountKey, count - 1);
            return true;
        }

        /// <summary> 한 줄의 모든 값을 기본값으로 되돌린다 </summary>
        public static void ResetRow(UiSandboxScenarioSO scenario, EconomyListSchema schema, int index)
        {
            for (int f = 0; f < schema.Fields.Length; ++f)
            {
                EconomyField field = schema.Fields[f];
                WriteText(scenario, field, schema.KeyOf(index, field.Key), DefaultOf(schema, index, f));
            }
        }

        /// <summary> 접이식 머리글, 개수 입력, 줄별 필드와 삭제 버튼, 추가 버튼을 그린다 </summary>
        public static void Draw(UiSandboxScenarioSO scenario, EconomyListSchema schema)
        {
            Seed(scenario, schema);
            int count = Count(scenario, schema);
            bool open = _expanded.TryGetValue(schema.Prefix, out bool stored) && stored;
            open = EditorGUILayout.Foldout(open, schema.Title + " (" + count + ")", true);
            _expanded[schema.Prefix] = open;
            if (!open)
            {
                return;
            }

            EditorGUI.indentLevel++;
            int edited = EditorGUILayout.IntSlider("개수", count, 0, schema.MaxCount);
            if (edited != count)
            {
                SetCount(scenario, schema, edited);
                count = edited;
            }

            int removeIndex = -1;
            for (int i = 0; i < count; ++i)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("#" + i, EditorStyles.boldLabel);
                for (int f = 0; f < schema.Fields.Length; ++f)
                {
                    DrawField(scenario, schema.Fields[f], schema.KeyOf(i, schema.Fields[f].Key));
                }

                if (GUILayout.Button("이 줄 삭제"))
                {
                    removeIndex = i;
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("줄 추가"))
            {
                Add(scenario, schema);
            }

            if (removeIndex >= 0)
            {
                RemoveAt(scenario, schema, removeIndex);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawField(UiSandboxScenarioSO scenario, EconomyField field, string key)
        {
            switch (field.Kind)
            {
                case EEconomyFieldKind.Int:
                    EconomyScenarioValues.DrawInt(scenario, key, field.Label, (int)Math.Max(field.Min, int.MinValue), (int)Math.Min(field.Max, int.MaxValue));
                    break;
                case EEconomyFieldKind.Long:
                    EconomyScenarioValues.DrawLong(scenario, key, field.Label, field.Min, field.Max);
                    break;
                case EEconomyFieldKind.Bool:
                    EconomyScenarioValues.DrawBool(scenario, key, field.Label);
                    break;
                default:
                    DrawTextField(scenario, field, key);
                    break;
            }
        }

        private static void DrawTextField(UiSandboxScenarioSO scenario, EconomyField field, string key)
        {
            if ((field.Options == null) || (field.Options.Length == 0))
            {
                EconomyScenarioValues.DrawString(scenario, key, field.Label);
                return;
            }

            string current = scenario.GetString(key);
            int index = Mathf.Max(0, Array.IndexOf(field.Options, current));
            int edited = EditorGUILayout.Popup(field.Label, index, field.Options);
            if ((edited != index) || (current != field.Options[edited]))
            {
                scenario.SetString(key, field.Options[edited]);
            }
        }

        private static string DefaultOf(EconomyListSchema schema, int row, int fieldIndex)
        {
            string raw = ((row < schema.SampleRows.Length) && (fieldIndex < schema.SampleRows[row].Length))
                ? schema.SampleRows[row][fieldIndex]
                : schema.Fields[fieldIndex].DefaultValue;
            return raw.Replace(INDEX_TOKEN, (row + 1).ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteText(UiSandboxScenarioSO scenario, EconomyField field, string key, string raw)
        {
            switch (field.Kind)
            {
                case EEconomyFieldKind.Int:
                    scenario.SetInt(key, int.Parse(raw, CultureInfo.InvariantCulture));
                    break;
                case EEconomyFieldKind.Long:
                    scenario.SetLong(key, long.Parse(raw, CultureInfo.InvariantCulture));
                    break;
                case EEconomyFieldKind.Bool:
                    scenario.SetBool(key, bool.Parse(raw));
                    break;
                default:
                    scenario.SetString(key, raw);
                    break;
            }
        }

        private static void CopyField(UiSandboxScenarioSO scenario, EconomyListSchema schema, EconomyField field, int from, int to)
        {
            string source = schema.KeyOf(from, field.Key);
            string target = schema.KeyOf(to, field.Key);
            switch (field.Kind)
            {
                case EEconomyFieldKind.Int:
                    scenario.SetInt(target, scenario.GetInt(source));
                    break;
                case EEconomyFieldKind.Long:
                    scenario.SetLong(target, scenario.GetLong(source));
                    break;
                case EEconomyFieldKind.Bool:
                    scenario.SetBool(target, scenario.GetBool(source));
                    break;
                default:
                    scenario.SetString(target, scenario.GetString(source));
                    break;
            }
        }
    }
}
