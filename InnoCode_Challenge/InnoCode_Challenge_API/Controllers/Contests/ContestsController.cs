using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ContestDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContestsController : ControllerBase
    {
        private readonly IContestService _contestService;

        // Constructor
        public ContestsController(IContestService contestService)
        {
            _contestService = contestService;
        }

        /// <summary>
        /// Get All Contests with Pagination and Optional Filters
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="nameSearch"></param>
        /// <param name="yearSearch"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetContestDTO>>> GetContests(int pageNumber = 1,
                                                                                 int pageSize = 10,
                                                                                 Guid? idSearch = null,
                                                                                 string? nameSearch = null,
                                                                                 int? yearSearch = null,
                                                                                 DateTime? startDate = null,
                                                                                 DateTime? endDate = null)
        {
            var result = await _contestService.GetPaginatedContestAsync(pageNumber, pageSize, idSearch,
                                                                  nameSearch, yearSearch, startDate, endDate);

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
                        message: "Contests retrieved successfully."
                    ));
        }

        ///// <summary>
        ///// Create a New Contest
        ///// </summary>
        ///// <param name="contestDTO"></param>
        ///// <returns></returns>
        //[HttpPost]
        //public async Task<IActionResult> CreateContest(CreateContestDTO contestDTO)
        //{
        //    await _contestService.CreateContestAsync(contestDTO);
        //    return Ok(new BaseResponseModel(
        //                statusCode: StatusCodes.Status201Created,
        //                code: ResponseCodeConstants.SUCCESS,
        //                message: "Create contest successfully."
        //            ));
        //}

        /// <summary>
        /// Update an Existing Contest
        /// </summary>
        /// <param name="id"></param>
        /// <param name="contestDTO"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateContest(Guid id, UpdateContestDTO contestDTO)
        {
            GetContestDTO result = await _contestService.UpdateContestAsync(id, contestDTO);
            return Ok(new BaseResponseModel<object>(
                        data: result,
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update contest successfully."
                    ));
        }

        ///// <summary>
        ///// Publish a Contest
        ///// </summary>
        ///// <param name="id"></param>
        ///// <returns></returns>
        //[HttpPut("{id}/publish")]
        //public async Task<IActionResult> PublishedContest(Guid id)
        //{
        //    await _contestService.PublishContestAsync(id);
        //    return Ok(new BaseResponseModel(
        //                statusCode: StatusCodes.Status200OK,
        //                code: ResponseCodeConstants.SUCCESS,
        //                message: "Publish contest successfully."
        //            ));
        //}

        /// <summary>
        /// Delete a Contest
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteContest(Guid id)
        {
            await _contestService.DeleteContestAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete contest successfully."
                    ));
        }

        [HttpPost("advanced")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> CreateAdvanced(CreateContestAdvancedDTO dto)
        {
            var created = await _contestService.CreateContestWithPolicyAsync(dto);
            return CreatedAtAction(nameof(CheckPublishReadiness), new { id = created.ContestId },
                new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status201Created,
                    code: ResponseCodeConstants.SUCCESS,
                    data: created,
                    message: "Contest created (draft) and policies bootstrapped."
                    ));
        }

        [HttpGet("{id}/check")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> CheckPublishReadiness(Guid id)
        {
            var check = await _contestService.CheckPublishReadinessAsync(id);
            return Ok(new BaseResponseModel<object>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: check,
                        message: check.IsReady ? "Contest is ready to publish." : "Contest is NOT ready to publish."
                    ));
        }
         
        [HttpPut("{id}/publish")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> PublishIfReady(Guid id)
        {
            await _contestService.PublishIfReadyAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Contest published."
                    ));
        }

        [HttpPut("{id}/cancel")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> CancelContest(Guid id)
        {
            await _contestService.CancelledContest(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Cancel contest successfully."
                    ));
        }

    }
}
