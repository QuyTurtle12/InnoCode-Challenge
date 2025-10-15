using BusinessLogic.IServices.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.StudentDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Students
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentsController : ControllerBase
    {
        private readonly IStudentService _studentService;

        public StudentsController(IStudentService studentService)
        {
            _studentService = studentService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] StudentQueryParams queryParams)
        {
            var paged = await _studentService.GetAsync(queryParams);

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
                message: "Students retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var dto = await _studentService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: dto,
                message: "Student retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateStudentDTO dto)
        {
            var created = await _studentService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.StudentId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "Student created successfully."
                ));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudentDTO dto)
        {
            var updated = await _studentService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updated,
                message: "Student updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _studentService.DeleteAsync(id);
            return NoContent();
        }
    }
}
