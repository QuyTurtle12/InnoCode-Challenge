using BusinessLogic.IServices.Mentors;
using DataAccess.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.MentorManagementDTOs;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Mentors
{
    [Route("api/schools/{schoolId:guid}/mentors")]
    [ApiController]
    public class SchoolMentorsController : ControllerBase
    {
        private readonly IMentorManagementService _service;

        public SchoolMentorsController(IMentorManagementService service) => _service = service;

        [HttpPost]
        [Authorize(Roles = RoleConstants.SchoolManager)]
        public async Task<IActionResult> Create(Guid schoolId, [FromBody] MentorManagerRequestDTO dto)
        {
            var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdRaw) || !Guid.TryParse(userIdRaw, out var userId))
                return Unauthorized(new BaseResponseModel(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "Invalid user token."));

            var created = await _service.CreateMentorAsync(schoolId, dto, userId);

            return StatusCode(StatusCodes.Status201Created,
                new BaseResponseModel<object>(StatusCodes.Status201Created, ResponseCodeConstants.SUCCESS, created, message: "Mentor created."));
        }
    }

}

