using BusinessLogic.IServices.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TeamDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Students
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamService _teamService;

        public TeamsController(ITeamService teamService)
        {
            _teamService = teamService;
        }

        [HttpGet("/api/contests/{contestId}/teams")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> GetAll(
            Guid contestId,
            int pageNumber = 1,
            int pageSize = 10,
            Guid? mentorIdSearch = null,
            Guid? schoolIdSearch = null,
            string? nameSearch = null)
        {
            PaginatedList<TeamWithMembersDTO> paged = await _teamService.GetAsync(pageNumber, pageSize, contestId, mentorIdSearch, schoolIdSearch, nameSearch, false);

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
                message: "Teams retrieved successfully."
            ));
        }

        [HttpGet("my-team")]
        [Authorize(Policy = "RequireMentorOrStudent")]
        public async Task<IActionResult> GetMyTeam(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? contestIdSearch = null,
            Guid? mentorIdSearch = null,
            Guid? schoolIdSearch = null,
            string? nameSearch = null)
        {
            PaginatedList<TeamWithMembersDTO> paged = await _teamService.GetAsync(pageNumber, pageSize, contestIdSearch, mentorIdSearch, schoolIdSearch, nameSearch, true);

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
                message: "Teams retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var dto = await _teamService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dto,
                message: "Team retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Mentor)]
        public async Task<IActionResult> Create([FromBody] CreateTeamDTO dto)
        {
            var created = await _teamService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.TeamId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "Team created successfully."
                ));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = $"{RoleConstants.Mentor},{RoleConstants.Admin}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamDTO dto)
        {
            var updated = await _teamService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "Team updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = $"{RoleConstants.Mentor},{RoleConstants.Admin}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _teamService.DeleteAsync(id);
            return NoContent();
        }

        [HttpDelete("{teamId:guid}/members/{studentId:guid}")]
        [Authorize(Roles = RoleConstants.Mentor)]
        public async Task<IActionResult> RemoveMember(Guid teamId, Guid studentId)
        {
            await _teamService.RemoveMemberAsync(teamId, studentId);
            return NoContent();
        }

        [HttpGet("me")]
        [Authorize] 
        public async Task<IActionResult> GetMine()
        {
            var teams = await _teamService.GetMyTeamsAsync();

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: teams,
                message: "Teams of current user retrieved successfully."
            ));
        }

    }
}
