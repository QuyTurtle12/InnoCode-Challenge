using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.SchoolDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Schools
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchoolsController : ControllerBase
    {
        private readonly ISchoolService _schoolService;

        public SchoolsController(ISchoolService schoolService)
        {
            _schoolService = schoolService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] SchoolQueryParams queryParams)
        {
            var paged = await _schoolService.GetAsync(queryParams);

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
                message: "Schools retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var dto = await _schoolService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dto,
                message: "School retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateSchoolDTO dto)
        {
            var created = await _schoolService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.SchoolId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "School created successfully."
                ));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSchoolDTO dto)
        {
            var updated = await _schoolService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "School updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _schoolService.DeleteAsync(id);
            return NoContent();
        }
    }
}
