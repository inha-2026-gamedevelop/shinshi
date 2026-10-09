using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> UI 샌드박스 시나리오 값 묶음. 문자열 키마다 값을 InvariantCulture 문자열로 저장해 프리셋 에셋으로 보관한다 </summary>
    [CreateAssetMenu(fileName = "UiSandboxScenario", menuName = "Shinshi/UI Sandbox/Scenario")]
    public sealed class UiSandboxScenarioSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Serializable]
        private struct Entry
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public List<Entry> Entries = new List<Entry>();
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private int _version = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary> 값이 바뀔 때마다 늘어나는 번호. 저장되지 않으며 변경 감지에 쓴다 </summary>
        public int Version
        {
            get { return _version; }
        }

        /// <summary> 저장된 키 목록의 복사본 </summary>
        public IReadOnlyList<string> Keys
        {
            get
            {
                List<string> keys = new List<string>(_entries.Count);
                for (int i = 0; i < _entries.Count; ++i)
                {
                    keys.Add(_entries[i].Key);
                }

                return keys;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool Contains(string key)
        {
            return FindIndex(key) >= 0;
        }

        public int GetInt(string key, int fallback = 0)
        {
            if ((TryGetRaw(key, out string raw))
                && (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)))
            {
                return value;
            }

            return fallback;
        }

        public long GetLong(string key, long fallback = 0)
        {
            if ((TryGetRaw(key, out string raw))
                && (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)))
            {
                return value;
            }

            return fallback;
        }

        public float GetFloat(string key, float fallback = 0.0f)
        {
            if ((TryGetRaw(key, out string raw))
                && (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)))
            {
                return value;
            }

            return fallback;
        }

        public bool GetBool(string key, bool fallback = false)
        {
            if (!TryGetRaw(key, out string raw))
            {
                return fallback;
            }

            if (bool.TryParse(raw, out bool value))
            {
                return value;
            }

            if (raw == "1")
            {
                return true;
            }

            if (raw == "0")
            {
                return false;
            }

            return fallback;
        }

        public string GetString(string key, string fallback = "")
        {
            return TryGetRaw(key, out string raw) ? raw : fallback;
        }

        public void SetInt(string key, int value)
        {
            SetRaw(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public void SetLong(string key, long value)
        {
            SetRaw(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public void SetFloat(string key, float value)
        {
            SetRaw(key, value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void SetBool(string key, bool value)
        {
            SetRaw(key, value ? "true" : "false");
        }

        public void SetString(string key, string value)
        {
            SetRaw(key, value ?? string.Empty);
        }

        /// <summary> 키가 없을 때만 값을 채운다. 사용자가 고친 값은 덮어쓰지 않는다 </summary>
        public void SeedInt(string key, int value)
        {
            if (!Contains(key))
            {
                SetInt(key, value);
            }
        }

        public void SeedLong(string key, long value)
        {
            if (!Contains(key))
            {
                SetLong(key, value);
            }
        }

        public void SeedFloat(string key, float value)
        {
            if (!Contains(key))
            {
                SetFloat(key, value);
            }
        }

        public void SeedBool(string key, bool value)
        {
            if (!Contains(key))
            {
                SetBool(key, value);
            }
        }

        public void SeedString(string key, string value)
        {
            if (!Contains(key))
            {
                SetString(key, value);
            }
        }

        public bool Remove(string key)
        {
            int index = FindIndex(key);
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            ++_version;
            return true;
        }

        public void Clear()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _entries.Clear();
            ++_version;
        }

        /// <summary> 다른 시나리오의 값으로 전부 바꾼다. 같은 객체나 null이면 아무것도 하지 않는다 </summary>
        public void CopyFrom(UiSandboxScenarioSO source)
        {
            if ((source == null) || (ReferenceEquals(source, this)))
            {
                return;
            }

            _entries.Clear();
            _entries.AddRange(source._entries);
            ++_version;
        }

        /// <summary> 도메인 리로드와 Play 진입에서 살아남도록 값 전체를 JSON 문자열로 만든다 </summary>
        public string ToJson()
        {
            Snapshot snapshot = new Snapshot();
            snapshot.Entries.AddRange(_entries);
            return JsonUtility.ToJson(snapshot);
        }

        /// <summary> ToJson 결과로 값 전체를 바꾼다. 비었거나 깨진 JSON이면 현재 값을 그대로 두고 false를 돌려준다 </summary>
        public bool TryLoadJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            Snapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<Snapshot>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if ((snapshot == null) || (snapshot.Entries == null))
            {
                return false;
            }

            _entries.Clear();
            for (int i = 0; i < snapshot.Entries.Count; ++i)
            {
                Entry entry = snapshot.Entries[i];
                if ((string.IsNullOrEmpty(entry.Key)) || (FindIndex(entry.Key) >= 0))
                {
                    continue;
                }

                _entries.Add(new Entry { Key = entry.Key, Value = entry.Value ?? string.Empty });
            }

            ++_version;
            return true;
        }

        /// <summary> 저장되지 않는 메모리 전용 복사본을 만든다. 복사본을 바꿔도 원본에는 영향이 없다 </summary>
        public UiSandboxScenarioSO Clone()
        {
            UiSandboxScenarioSO copy = CreateInstance<UiSandboxScenarioSO>();
            copy.name      = name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary> 키와 값이 순서에 상관없이 모두 같은지 비교한다 </summary>
        public bool ContentEquals(UiSandboxScenarioSO other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            if (_entries.Count != other._entries.Count)
            {
                return false;
            }

            for (int i = 0; i < _entries.Count; ++i)
            {
                if ((!other.TryGetRaw(_entries[i].Key, out string value))
                    || (!string.Equals(value, _entries[i].Value ?? string.Empty, StringComparison.Ordinal)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetRaw(string key, out string raw)
        {
            int index = FindIndex(key);
            if (index < 0)
            {
                raw = null;
                return false;
            }

            raw = _entries[index].Value ?? string.Empty;
            return true;
        }

        private void SetRaw(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("시나리오 키는 비어 있을 수 없다", nameof(key));
            }

            int index = FindIndex(key);
            if (index < 0)
            {
                _entries.Add(new Entry { Key = key, Value = value });
                ++_version;
                return;
            }

            if (string.Equals(_entries[index].Value, value, StringComparison.Ordinal))
            {
                return;
            }

            _entries[index] = new Entry { Key = key, Value = value };
            ++_version;
        }

        private int FindIndex(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -1;
            }

            for (int i = 0; i < _entries.Count; ++i)
            {
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
