using BusinessLogic.IServices.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.TeamDTOs;
using Repository.ResponseModel;
using Utility.Constant;

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

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] TeamQueryParams queryParams)
        {
            var paged = await _teamService.GetAsync(queryParams);

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
        [Authorize(Roles = RoleConstants.Admin)]
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
        [Authorize(Roles = RoleConstants.Admin)]
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
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _teamService.DeleteAsync(id);
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
