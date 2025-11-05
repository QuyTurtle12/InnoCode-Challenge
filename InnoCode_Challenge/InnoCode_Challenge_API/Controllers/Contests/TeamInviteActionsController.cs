using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/team-invites")]
    [ApiController]
    public class TeamInviteActionsController : ControllerBase
    {
        private readonly ITeamInviteService _service;

        public TeamInviteActionsController(ITeamInviteService service) => _service = service;

        [HttpPost("accept")]
        public async Task<IActionResult> Accept([FromQuery] string token)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.AcceptByTokenAsync(token, userId);
            return Ok(BaseResponseModel<object>.OkResponseModel(new { token }, "Invite accepted."));
        }

        [HttpPost("decline")]
        public async Task<IActionResult> Decline([FromQuery] string token)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.DeclineByTokenAsync(token, userId);
            return Ok(BaseResponseModel<object>.OkResponseModel(new { token }, "Invite declined."));
        }
    }
}
