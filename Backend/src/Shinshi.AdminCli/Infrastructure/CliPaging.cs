// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> nextCursor가 있는 조회의 한 페이지 또는 --all로 끝까지 읽기. cursor는 서버가 준 불투명 문자열을 그대로 넘긴다 </summary>
    internal static class CliPaging
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string NEXT_CURSOR_PROPERTY = "nextCursor";

        // cursor가 되풀이되는 서버 결함에서 멈추지 않는 일을 막는 상한. 최대 한도 500건이면 백만 건이다
        private const int MAX_PAGES = 2000;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> all이 아니면 첫 페이지만 읽는다. 각 페이지의 원본 JSON과 역직렬화 값을 함께 돌려준다 </summary>
        public static async Task<List<AdminApiResponse<TPage>>> ReadAsync<TPage>(AdminApiClient client, string path,
            CliQuery query, bool all, Func<TPage, string?> nextCursor, CancellationToken cancellationToken)
        {
            List<AdminApiResponse<TPage>> pages = new List<AdminApiResponse<TPage>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            CliQuery current = query;
            while (true)
            {
                AdminApiResponse<TPage> page = await client.GetAsync<TPage>(current.Append(path), cancellationToken);
                pages.Add(page);
                string? cursor = nextCursor(page.Value);
                if ((!all) || string.IsNullOrEmpty(cursor))
                {
                    return pages;
                }

                if ((!seen.Add(cursor)) || (pages.Count >= MAX_PAGES))
                {
                    throw new CliUnavailableException("페이지 cursor가 끝나지 않아 " + pages.Count
                        + "쪽에서 멈췄습니다. --cursor로 나눠 조회하세요.", false);
                }

                current = query.WithCursor(AdminLogQueryParameters.CURSOR, cursor);
            }
        }

        /// <summary> --all의 --json 출력. 첫 페이지 객체에 뒤 페이지의 배열을 이어 붙이고 nextCursor를 null로 둔다 </summary>
        public static string MergeJson<TPage>(IReadOnlyList<AdminApiResponse<TPage>> pages, string arrayProperty)
        {
            if (pages.Count == 1)
            {
                return pages[0].RawBody;
            }

            JObject merged = ParseObject(pages[0].RawBody);
            JArray items = merged[arrayProperty] as JArray ?? new JArray();
            for (int i = 1; i < pages.Count; ++i)
            {
                JObject page = ParseObject(pages[i].RawBody);
                if (page[arrayProperty] is JArray more)
                {
                    foreach (JToken item in more)
                    {
                        items.Add(item);
                    }
                }
            }

            merged[arrayProperty]        = items;
            merged[NEXT_CURSOR_PROPERTY] = JValue.CreateNull();
            return merged.ToString(Formatting.None);
        }

        // 감사 행의 jsonb에 든 날짜 문자열을 DateTime으로 바꿔 다시 쓰면 원본과 달라지므로 문자열 그대로 읽는다
        private static JObject ParseObject(string raw)
        {
            using JsonTextReader reader = new JsonTextReader(new StringReader(raw))
            {
                DateParseHandling = DateParseHandling.None
            };
            return JObject.Load(reader);
        }
    }
}
