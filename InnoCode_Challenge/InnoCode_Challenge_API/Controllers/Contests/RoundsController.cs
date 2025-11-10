using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.RoundDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoundsController : ControllerBase
    {
        private readonly IRoundService _roundService;

        // Constructor
        public RoundsController(IRoundService roundService)
        {
            _roundService = roundService;
        }
        
        /// <summary>
        /// Get paginated rounds with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetRoundDTO>>> GetRounds(
             int pageNumber = 1,
             int pageSize = 10,
             Guid? idSearch = null,
             Guid? contestIdSearch = null,
             string? roundNameSearch = null,
             string? contestNameSearch = null,
             DateTime? startDate = null,
             DateTime? endDate = null)
        {
            PaginatedList<GetRoundDTO> result = await _roundService.GetPaginatedRoundAsync(pageNumber, pageSize, idSearch, contestIdSearch, roundNameSearch, contestNameSearch, startDate, endDate);

            var paging = new
            {
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.TotalCount,
                result.HasPreviousPage,
                result.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result.Items,
                        additionalData: paging,
                        message: "Round retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create a new round
        /// </summary>
        [HttpPost("{contestId}")]
        public async Task<IActionResult> CreateRound(Guid contestId, CreateRoundDTO roundDTO)
        {
            await _roundService.CreateRoundAsync(contestId, roundDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create round successfully."
                    ));
        }

        /// <summary>
        /// Update an existing round
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRound(Guid id, UpdateRoundDTO roundDTO)
        {
            await _roundService.UpdateRoundAsync(id, roundDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update round successfully."
                    ));
        }

        /// <summary>
        /// Delete a round by id
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRound(Guid id)
        {
            await _roundService.DeleteRoundAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete round successfully."
                    ));
        }

        /// <summary>
        /// Get manual type submissions by round id with pagination and optional status filter
        /// </summary>
        /// <param name="roundId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="statusFilter">Pending, Finished, Cancelled</param>
        /// <returns></returns>
        [HttpGet("{roundId}/submissions/manual-type")]
        public async Task<IActionResult> GetManualTypeSubmissionsByRoundId(
            Guid roundId,
            int pageNumber = 1,
            int pageSize = 10,
            SubmissionStatusEnum? statusFilter = SubmissionStatusEnum.Pending
            )
        {
            var submissions = await _roundService.GetManualTypeSubmissionsByRoundId(pageNumber, pageSize, roundId, statusFilter);

            var paging = new
            {
                submissions.PageNumber,
                submissions.PageSize,
                submissions.TotalPages,
                submissions.TotalCount,
                submissions.HasPreviousPage,
                submissions.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: submissions.Items,
                        additionalData: paging,
                        message: "Manual type submissions retrieved successfully."
                    ));
        }
    }
}
