// System
using System;
using System.Globalization;

namespace Shinshi.Dev
{
    /// <summary> 개발 빌드 시험 인자 읽기, 출시 빌드에서는 항상 없음으로 답한다 </summary>
    public static class DevArgs
    {
        /****************************************
        *                Methods
        ****************************************/

        public static bool Has(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
#endif
            return false;
        }

        public static bool TryReadString(string name, out string value)
        {
            value = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    return true;
                }
            }
#endif
            return false;
        }

        public static bool TryReadFloat(string name, out float value)
        {
            value = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                }
            }
#endif
            return false;
        }
    }
}
