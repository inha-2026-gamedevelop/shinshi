// System
using System.Collections.Generic;

// Project
using Shinshi.Contracts;

namespace Shinshi.AdminCli
{
    /// <summary> 여러 명령이 같은 모양으로 찍는 출력 </summary>
    internal static class CommandOutput
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> OperationResult만 돌려주는 쓰기의 결과. --json이면 원본을 쓴다 </summary>
        public static void WriteOperation(CliContext context, AdminApiResponse<OperationResult> response, string message)
        {
            if (context.Json)
            {
                context.WriteJson(response.RawBody);
                return;
            }

            context.Line(message);
            context.WriteFields(OperationFields(response.Value));
        }

        public static List<(string Label, string Value)> OperationFields(OperationResult operation)
        {
            return new List<(string, string)>
            {
                ("operationId", CliFormat.Text(operation.OperationId)),
                ("상태", ContractCodeNames.OperationStatus(operation.Status)),
                ("영수증 ID", CliFormat.Text(operation.ReceiptId)),
                ("서버 시각", CliFormat.Time(operation.ServerTimeUnixMs))
            };
        }
    }
}
