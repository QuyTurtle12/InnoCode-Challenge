using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TeamMemberDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Students
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeamMembersController : ControllerBase
    {
        private readonly ITeamMemberService _teamMemberService;

        public TeamMembersController(ITeamMemberService teamMemberService)
        {
            _teamMemberService = teamMemberService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] TeamMemberQueryParams queryParams)
        {
            var paged = await _teamMemberService.GetAsync(queryParams);

            var paging = new
            {
                paged.PageNumber,
                paged.PageSize,
                paged.TotalPages,
                paged.TotalCount,
                paged.HasPreviousPage,
                paged.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: paged.Items,
                additionalData: paging,
                message: "Team members retrieved successfully."
            ));
        }

        [HttpGet("{teamId:guid}/{studentId:guid}")]
        public async Task<IActionResult> GetById(Guid teamId, Guid studentId)
        {
            var dto = await _teamMemberService.GetByIdAsync(teamId, studentId);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dto,
                message: "Team member retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateTeamMemberDTO dto)
        {
            var created = await _teamMemberService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { teamId = created.TeamId, studentId = created.StudentId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "Team member created successfully."
                ));
        }

        [HttpPut("{teamId:guid}/{studentId:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid teamId, Guid studentId, [FromBody] UpdateTeamMemberDTO dto)
        {
            var updated = await _teamMemberService.UpdateAsync(teamId, studentId, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "Team member updated successfully."
            ));
        }

        [HttpDelete("{teamId:guid}/{studentId:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid teamId, Guid studentId)
        {
            await _teamMemberService.DeleteAsync(teamId, studentId);
            return NoContent();
        }
    }
}
