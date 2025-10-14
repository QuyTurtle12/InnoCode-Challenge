using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AppealDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppealsController : ControllerBase
    {
        private readonly IAppealService _appealService;

        // Constructor
        public AppealsController(IAppealService appealService)
        {
            _appealService = appealService;
        }

        /// <summary>
        /// Gets paginated appeals with optional filters
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAppeals(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? teamIdSearch = null,
            Guid? ownerIdSearch = null,
            string? teamNameSearch = null,
            string? ownerNameSearch = null)
        {
            PaginatedList<GetAppealDTO> result = await _appealService.GetPaginatedAppealAsync(
                pageNumber,
                pageSize,
                idSearch,
                teamIdSearch,
                ownerIdSearch,
                teamNameSearch,
                ownerNameSearch);

            return Ok(new BaseResponseModel<PaginatedList<GetAppealDTO>>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result,
                        message: "Appeal retrieved successfully."
                    ));
        }

        /// <summary>
        /// Creates a new appeal
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateAppeal(CreateAppealDTO appealDto)
        {
            await _appealService.CreateAppealAsync(appealDto);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Appeal successfully."
                    ));
        }

        /// <summary>
        /// Updates an existing appeal
        /// </summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateAppeal(Guid id, [FromBody] UpdateAppealDTO appealDto)
        {
            await _appealService.UpdateAppealAsync(id, appealDto);
            return Ok(new BaseResponseModel(
                    statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Appeal successfully."
                    ));
        }

        /// <summary>
        /// Deletes an appeal
        /// </summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteAppeal(Guid id)
        {
            await _appealService.DeleteAppealAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Appeal successfully."
                    ));
        }
    }
}
