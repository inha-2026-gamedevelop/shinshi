// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> Viewer 이상이 감사 사유를 남기고 조회하는 캐릭터 프로필 </summary>
    public sealed class AdminCharactersController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAdminCharacterRepository _characters;
        private readonly IAdminAuditWriter _auditWriter;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharactersController(IAdminCharacterRepository characters, IAdminAuditWriter auditWriter)
        {
            ArgumentNullException.ThrowIfNull(characters);
            ArgumentNullException.ThrowIfNull(auditWriter);
            _characters  = characters;
            _auditWriter = auditWriter;
        }

        [HttpGet(AdminApiRoutes.CHARACTER)]
        public async Task<ActionResult<AdminCharacterDetailResponse>> Detail(Guid characterId,
            [FromQuery(Name = AdminAccountQuery.REASON_PARAMETER)] string? reason, CancellationToken cancellationToken)
        {
            if ((characterId == Guid.Empty) || string.IsNullOrWhiteSpace(reason)
                || (reason.Length > AdminContractCodes.MAX_REASON_LENGTH))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            await _auditWriter.AppendStandaloneAsync(AdminAuditEntry.ForAdmin(CurrentAdmin,
                AdminAuditActions.CHARACTER_VIEW, AdminAuditActions.TARGET_CHARACTER,
                characterId.ToString("D"), reason, requestIpHash: RequestIpHash), cancellationToken);
            AdminCharacterDetailResponse? detail = await _characters.ReadDetailAsync(characterId, cancellationToken);
            if (detail == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            return Ok(detail);
        }
    }
}

