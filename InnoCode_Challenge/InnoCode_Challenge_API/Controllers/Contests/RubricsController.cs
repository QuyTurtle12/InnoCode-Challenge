using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.RubricDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/")]
    [ApiController]
    public class RubricsController : ControllerBase
    {
        private readonly IProblemService _problemService;

        public RubricsController(IProblemService problemService)
        {
            _problemService = problemService;
        }

        /// <summary>
        /// Get rubric template (scoring criteria) for a problem
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <returns>Rubric template with all criteria</returns>
        [HttpGet("rounds/{roundId}/rubric")]
        public async Task<IActionResult> GetRubricTemplate(Guid roundId)
        {
            RubricTemplateDTO template = await _problemService.GetRubricTemplateAsync(roundId);

            return Ok(new BaseResponseModel<RubricTemplateDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: template,
                message: "Rubric template retrieved successfully."
            ));
        }

        /// <summary>
        /// Create rubric (scoring criteria) for a manual problem in a specific round
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <param name="createRubricDTO">Rubric criteria to create</param>
        /// <returns>Created rubric template</returns>
        [HttpPost("rounds/{roundId}/rubric")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> CreateRubric(Guid roundId, CreateRubricDTO createRubricDTO)
        {
            RubricTemplateDTO template = await _problemService.CreateRubricCriterionAsync(roundId, createRubricDTO);

            return Ok(new BaseResponseModel<RubricTemplateDTO>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: template,
                message: "Rubric created successfully."
            ));
        }

        /// <summary>
        /// Update rubrics (scoring criteria) for a manual problem in a specific round
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <param name="updateRubricDTO">Updated rubric criteria</param>
        /// <returns>Updated rubric template</returns>
        [HttpPut("rounds/{roundId}/rubric")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> UpdateRubric(Guid roundId, UpdateRubricDTO updateRubricDTO)
        {
            RubricTemplateDTO template = await _problemService.UpdateRubricCriterionAsync(roundId, updateRubricDTO);

            return Ok(new BaseResponseModel<RubricTemplateDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: template,
                message: "Rubric updated successfully."
            ));
        }

        /// <summary>
        /// Delete a rubric criterion by id
        /// </summary>
        /// <param name="id">Rubric criterion ID to delete</param>
        [HttpDelete("rounds/{roundId}/rubric/{id}")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> DeleteRubricCriterion(Guid id)
        {
            await _problemService.DeleteRubricCriterionAsync(id);

            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Rubric criterion deleted successfully."
            ));
        }
    }
}
