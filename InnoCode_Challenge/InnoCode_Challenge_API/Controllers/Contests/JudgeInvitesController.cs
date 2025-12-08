using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.JudgeInviteDTOs;
using Repository.ResponseModel;
using System.ComponentModel.DataAnnotations;
using System.Net.NetworkInformation;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/")]
    [ApiController]
    public class JudgeInvitesController : ControllerBase
    {
        private readonly IJudgeInviteService _judgeInviteService;

        public JudgeInvitesController(IJudgeInviteService judgeInviteService)
        {
            _judgeInviteService = judgeInviteService;
        }

        /// <summary>
        /// Get paginated list of judge invites for a contest
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <param name="page">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 20, max: 100)</param>
        /// <param name="status">Filter by invite status</param>
        /// <param name="requesterUserId">Filter by requester user ID</param>
        /// <param name="judgeNameSearch">Search by judge name</param>
        /// <param name="judgeEmailSearch">Search by judge email</param>
        /// <param name="createdAtStart">Filter by created date start</param>
        /// <param name="createdAtEnd">Filter by created date end</param>
        /// <param name="ExpiresAtStart">Filter by expiration date start</param>
        /// <param name="ExpiresAtEnd">Filter by expiration date end</param>
        /// <param name="AcceptedAtStart">Filter by accepted date start</param>
        /// <param name="AcceptedAtEnd">Filter by accepted date end</param>
        /// <param name="desc">Sort descending (default: true)</param>
        /// <returns>Paginated list of judge invites</returns>
        [HttpGet("contests/{contestId}/judge-invites")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> GetForContest(
            [FromRoute] Guid contestId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] JudgeInviteStatusEnum? status = null,
            [FromQuery] Guid? requesterUserId = null,
            [FromQuery] string? judgeNameSearch = null,
            [FromQuery] string? judgeEmailSearch = null,
            [FromQuery] DateTime? createdAtStart = null,
            [FromQuery] DateTime? createdAtEnd = null,
            [FromQuery] DateTime? ExpiresAtStart = null,
            [FromQuery] DateTime? ExpiresAtEnd = null,
            [FromQuery] DateTime? AcceptedAtStart = null,
            [FromQuery] DateTime? AcceptedAtEnd = null,
            [FromQuery] bool desc = true)
        {
            PaginatedList<JudgeInviteDTO> result = await _judgeInviteService.GetForContestAsync(
                contestId,
                page,
                pageSize,
                status,
                requesterUserId,
                judgeNameSearch,
                judgeEmailSearch,
                createdAtStart,
                createdAtEnd,
                ExpiresAtStart,
                ExpiresAtEnd,
                AcceptedAtStart,
                AcceptedAtEnd,
                desc);

            var paging = new
            {
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.TotalCount,
                result.HasPreviousPage,
                result.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result.Items,
                additionalData: paging,
                message: "Judge invites retrieved successfully."
            ));
        }

        /// <summary>
        /// Create a new judge invite for a contest
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <param name="dto">Judge invite creation data</param>
        /// <returns>Created judge invite with invite code</returns>
        [HttpPost("contests/{contestId}/judge-invites")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> Create(
            Guid contestId,
            CreateJudgeInviteDTO dto)
        {
            var result = await _judgeInviteService.CreateAsync(contestId, dto);

            return Ok(new BaseResponseModel<JudgeInviteDTO>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Judge invite created successfully."
            ));
        }

        /// <summary>
        /// Resend a judge invite (generates new invite code)
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <param name="inviteId">Invite ID</param>
        /// <returns>Updated judge invite with new invite code</returns>
        [HttpPost("contests/{contestId}/judge-invites/{inviteId}/resend")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> Resend(
            Guid contestId,
            Guid inviteId)
        {
            var result = await _judgeInviteService.ResendAsync(contestId, inviteId);

            return Ok(new BaseResponseModel<JudgeInviteDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: "Judge invite resent successfully."
            ));
        }

        /// <summary>
        /// Revoke a pending judge invite
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <param name="inviteId">Invite ID</param>
        /// <returns>Success message</returns>
        [HttpDelete("contests/{contestId}/judge-invites/{inviteId}")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> Revoke(
            [FromRoute] Guid contestId,
            [FromRoute] Guid inviteId)
        {
            await _judgeInviteService.RevokeAsync(contestId, inviteId);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Judge invite revoked successfully."
            ));
        }

        /// <summary>
        /// Accept a judge invite by invite code
        /// </summary>
        /// <param name="inviteCode">8-character invite code</param>
        /// <returns>Success message</returns>
        [HttpPost("judge-invites/accept")]
        [Authorize(Policy = "RequireJudgeRole")]
        public async Task<IActionResult> Accept([Required] string inviteCode)
        {
            await _judgeInviteService.AcceptByCodeAsync(inviteCode);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Judge invite accepted successfully. You are now assigned to the contest."
            ));
        }

        /// <summary>
        /// Decline a judge invite by invite code
        /// </summary>
        /// <param name="inviteCode">8-character invite code</param>
        /// <returns>Success message</returns>
        [HttpPost("judge-invites/decline")]
        [Authorize(Policy = "RequireJudgeRole")]
        public async Task<IActionResult> Decline([Required] string inviteCode)
        {
            await _judgeInviteService.DeclineByCodeAsync(inviteCode);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Judge invite declined successfully."
            ));
        }
    }
}
