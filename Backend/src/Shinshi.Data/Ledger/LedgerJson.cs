// System
using System;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shinshi.Data.Ledger
{
    internal static class LedgerJson
    {
        /****************************************
        *                Methods
        ****************************************/

        public static void RequireObject(string json, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(json, parameterName);
            try
            {
                if (JToken.Parse(json) is not JObject)
                {
                    throw new ArgumentException("A JSON object is required.", parameterName);
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("A valid JSON object is required.", parameterName, exception);
            }
        }
    }
}
