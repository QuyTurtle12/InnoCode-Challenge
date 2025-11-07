using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.McqTestQuestionDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers.Mcqs
{
    [Route("api/mcq-tests")]
    [ApiController]
    public class McqTestsController : ControllerBase
    {
        private readonly IMcqTestService _mcqTestService;

        // Constructor
        public McqTestsController(IMcqTestService mcqTestService)
        {
            _mcqTestService = mcqTestService;
        }

        ///// <summary>
        ///// Get paginated list of Mcq Tests with optional filtering by id and roundId
        ///// </summary>
        ///// <param name="pageNumber"></param>
        ///// <param name="pageSize"></param>
        ///// <param name="idSearch"></param>
        ///// <param name="roundIdSearch"></param>
        ///// <returns></returns>
        //[HttpGet]
        //public async Task<IActionResult> GetMcqTests(int pageNumber = 1, int pageSize = 10, 
        //    Guid? idSearch = null, Guid? roundIdSearch = null)
        //{
        //    PaginatedList<GetMcqTestDTO> result = await _mcqTestService.GetPaginatedMcqTestAsync(pageNumber, pageSize, idSearch, roundIdSearch);

        //    var paging = new
        //    {
        //        result.PageNumber,
        //        result.PageSize,
        //        result.TotalPages,
        //        result.TotalCount,
        //        result.HasPreviousPage,
        //        result.HasNextPage
        //    };

        //    return Ok(new BaseResponseModel<object>(
        //                statusCode: StatusCodes.Status200OK,
        //                code: ResponseCodeConstants.SUCCESS,
        //                data: result.Items,
        //                additionalData: paging,
        //                message: "Mcq Test retrieved successfully."
        //            ));
        //}

        ///// <summary>
        ///// Create a new Mcq Test for a specific round
        ///// </summary>
        ///// <param name="roundId"></param>
        ///// <param name="mcqTestDTO"></param>
        ///// <returns></returns>
        //[HttpPost("{roundId}")]
        //public async Task<IActionResult> CreateMcqTest(Guid roundId, CreateMcqTestDTO mcqTestDTO)
        //{
        //    await _mcqTestService.CreateMcqTestAsync(roundId, mcqTestDTO);
        //    return Ok(new BaseResponseModel(
        //                statusCode: StatusCodes.Status201Created,
        //                code: ResponseCodeConstants.SUCCESS,
        //                message: "Create Mcq Test successfully."
        //            ));
        //}

        /// <summary>
        /// Add questions from question bank to Mcq Test
        /// </summary>
        /// <param name="testId"></param>
        /// <param name="bankId"></param>
        /// <returns></returns>
        [HttpPost("{testId}")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> AddQuestionToTest(Guid testId,[Required] Guid bankId)
        {
            await _mcqTestService.AddQuestionsToTest(testId, bankId);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Add questions to test successfully."
                    ));
        }

        /// <summary>
        /// Bulk update the weights of multiple questions in a test
        /// </summary>
        /// <param name="testId">The ID of the test</param>
        /// <param name="dto">List of question IDs and their new weights</param>
        [HttpPut("{testId}/questions/weights")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> BulkUpdateQuestionWeights(
            Guid testId,
            BulkUpdateQuestionWeightsDTO dto)
        {
            await _mcqTestService.BulkUpdateQuestionWeightsAsync(testId, dto);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: $"{dto.Questions.Count} question weight(s) updated successfully."
                    ));
        }

        /// <summary>
        /// Update an existing Mcq Test by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="mcqTestDTO"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMcqTest(Guid id, [FromBody] UpdateMcqTestDTO mcqTestDTO)
        {
            await _mcqTestService.UpdateMcqTestAsync(id, mcqTestDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Mcq Test successfully."
                    ));
        }

        // DELETE: api/mcq-tests/{id}
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteMcqTest(Guid id)
        {
            await _mcqTestService.DeleteMcqTestAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Mcq Test successfully."
                    ));
        }
    }
}
