// System
using System;

namespace Shinshi.Patch.Client
{
    /// <summary> 패치 원본이 키를 열지 못했다. 없는 키, 서버 오류 응답처럼 다시 시도해 볼 수 있는 실패 </summary>
    public sealed class PatchSourceException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public string Key        { get; }
        public bool   IsNotFound { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PatchSourceException(string key, bool isNotFound, string message)
            : base(message)
        {
            Key        = key;
            IsNotFound = isNotFound;
        }

        public PatchSourceException(string key, bool isNotFound, string message, Exception innerException)
            : base(message, innerException)
        {
            Key        = key;
            IsNotFound = isNotFound;
        }
    }
}
