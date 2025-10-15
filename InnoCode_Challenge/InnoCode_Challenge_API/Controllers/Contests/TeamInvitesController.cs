using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TeamInviteDTOs;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/teams/{teamId:guid}/invites")]
    [ApiController]
    public class TeamInvitesController : ControllerBase
    {
        private readonly ITeamInviteService _service;

        public TeamInvitesController(ITeamInviteService service) => _service = service;

        [HttpGet]
        [Authorize] // mentors, staff, admin; service enforces ownership/role
        public async Task<IActionResult> List(Guid teamId, [FromQuery] TeamInviteQueryParams query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            var page = await _service.GetForTeamAsync(teamId, query, userId, role);
            var meta = new
            {
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.TotalCount,
                page.HasPreviousPage,
                page.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                StatusCodes.Status200OK,
                ResponseCodeConstants.SUCCESS,
                page.Items,
                meta,
                "Invites retrieved."));
        }

        [HttpPost]
        [Authorize] // mentor owner, or staff/admin
        public async Task<IActionResult> Create(Guid teamId, [FromBody] CreateTeamInviteDTO dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            var created = await _service.CreateAsync(teamId, dto, userId, role);
            return CreatedAtAction(nameof(List), new { teamId }, new BaseResponseModel<object>(
                StatusCodes.Status201Created,
                ResponseCodeConstants.SUCCESS,
                created,
                message: "Invite created."));
        }

        [HttpPost("{inviteId:guid}/resend")]
        [Authorize]
        public async Task<IActionResult> Resend(Guid teamId, Guid inviteId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            var updated = await _service.ResendAsync(teamId, inviteId, userId, role);
            return Ok(BaseResponseModel<object>.OkResponseModel(updated, "Invite resent."));
        }

        [HttpDelete("{inviteId:guid}")]
        [Authorize]
        public async Task<IActionResult> Revoke(Guid teamId, Guid inviteId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            await _service.RevokeAsync(teamId, inviteId, userId, role);
            return NoContent();
        }
    }
}
