using BusinessLogic.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ProvinceDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProvincesController : ControllerBase
    {
        private readonly IProvinceService _provinceService;

        public ProvincesController(IProvinceService provinceService)
        {
            _provinceService = provinceService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ProvinceQueryParams queryParams)
        {
            var pagedResult = await _provinceService.GetAsync(queryParams);

            var pagingMetadata = new
            {
                pagedResult.PageNumber,
                pagedResult.PageSize,
                pagedResult.TotalPages,
                pagedResult.TotalCount,
                pagedResult.HasPreviousPage,
                pagedResult.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: pagedResult.Items,
                additionalData: pagingMetadata,
                message: "Provinces retrieved successfully."
            ));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var province = await _provinceService.GetByIdAsync(id);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: province,
                message: "Province retrieved successfully."
            ));
        }

        [HttpPost]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Create([FromBody] CreateProvinceDTO dto)
        {
            var createdProvince = await _provinceService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = createdProvince.ProvinceId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: createdProvince,
                    message: "Province created successfully."
                ));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProvinceDTO dto)
        {
            var updatedProvince = await _provinceService.UpdateAsync(id, dto);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: updatedProvince,
                message: "Province updated successfully."
            ));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _provinceService.DeleteAsync(id);
            return NoContent();
        }
    }
}
