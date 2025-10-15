using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.MentorDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Mentors
{
    [Route("api/[controller]")]
    [ApiController]
    public class MentorsController : ControllerBase
    {
        private readonly IMentorService _mentorService;

        public MentorsController(IMentorService mentorService)
        {
            _mentorService = mentorService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] MentorQueryParams queryParams)
        {
            var paged = await _mentorService.GetAsync(queryParams);

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
                message: "Mentors retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var dto = await _mentorService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dto,
                message: "Mentor retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateMentorDTO dto)
        {
            var created = await _mentorService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.MentorId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "Mentor created successfully."
                ));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMentorDTO dto)
        {
            var updated = await _mentorService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "Mentor updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _mentorService.DeleteAsync(id);
            return NoContent();
        }
    }
}
