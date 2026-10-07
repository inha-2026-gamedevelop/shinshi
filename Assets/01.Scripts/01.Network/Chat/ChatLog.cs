// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 서버를 옮겨도(마을에서 던전으로) 로그를 유지한다. 접속을 완전히 끝냈을 때만 비운다
    /// <summary> 이 클라이언트가 받은 최근 채팅. 접두사와 색은 UI가 정한다 </summary>
    public static class ChatLog
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<ChatEntry>          _entries = new List<ChatEntry>(Constants.Chat.LOG_CAPACITY);
        private static readonly List<double>             _times   = new List<double>(Constants.Chat.LOG_CAPACITY);
        private static readonly IReadOnlyList<ChatEntry> _view    = _entries.AsReadOnly();

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<ChatEntry> Entries => _view;

        // Clear를 부른 횟수. 공지 피드가 로그가 비워진 것을 알아 이미 보인 공지를 다시 넣는 기준으로 쓴다
        public static int ClearGeneration { get; private set; }

        public static event Action Changed;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _entries.Clear();
            _times.Clear();
            Changed = null;
            ++ClearGeneration;
        }

        /// <summary> 본인에게만 보이는 안내. 같은 문구가 짧은 시간 안에 다시 오면 넣지 않는다 </summary>
        public static void AddSystem(string text)
        {
            AddSystem(text, Time.realtimeSinceStartupAsDouble);
        }

        public static void AddSystem(string text, double now)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                if ((now - _times[i]) >= Constants.Chat.SYSTEM_REPEAT_SECONDS)
                {
                    break;
                }

                if ((_entries[i].Channel == EChatChannel.System) && (_entries[i].Text == text))
                {
                    return;
                }
            }

            Append(new ChatEntry(EChatChannel.System, string.Empty, text), now);
        }

        /// <summary> 서버가 확인한 채팅만 수신부에서 넣는다 </summary>
        public static void AddMessage(EChatChannel channel, string sender, string text)
        {
            if ((string.IsNullOrWhiteSpace(text)) || (!ChatRules.IsSendable(channel)))
            {
                return;
            }

            Append(new ChatEntry(channel, sender, text), Time.realtimeSinceStartupAsDouble);
        }

        public static void Clear()
        {
            ++ClearGeneration;
            if (_entries.Count == 0)
            {
                return;
            }

            _entries.Clear();
            _times.Clear();
            Changed?.Invoke();
        }

        private static void Append(ChatEntry entry, double time)
        {
            if (_entries.Count >= Constants.Chat.LOG_CAPACITY)
            {
                _entries.RemoveAt(0);
                _times.RemoveAt(0);
            }

            _entries.Add(entry);
            _times.Add(time);
            Changed?.Invoke();
        }
    }
}
