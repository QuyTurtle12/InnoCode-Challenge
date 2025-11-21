using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Mcqs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.McqTestQuestionDTOs;
using Repository.ResponseModel;
using Utility.Constant;

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

        /// <summary>
        /// Add questions from given list of question to Mcq Test
        /// </summary>
        /// <param name="testId"></param>
        /// <param name="questionIds"></param>
        /// <returns></returns>
        [HttpPost("{testId}")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> AddQuestionToTest(Guid testId,[Required] List<Guid> questionIds)
        {
            await _mcqTestService.AddQuestionsToTestAsync(testId, questionIds);
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
    }
}
