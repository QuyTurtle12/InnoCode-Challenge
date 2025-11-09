using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProblemsController : ControllerBase
    {
        private readonly IProblemService _problemService;

        // Constructor
        public ProblemsController(IProblemService problemService)
        {
            _problemService = problemService;
        }

        ///// <summary>
        ///// Get paginated problems with optional filters
        ///// </summary>
        ///// <param name="pageNumber">Current page number</param>
        ///// <param name="pageSize">Number of items per page</param>
        ///// <param name="idSearch">Optional problem ID filter</param>
        ///// <param name="roundIdSearch">Optional round ID filter</param>
        ///// <param name="roundNameSearch">Optional round name filter</param>
        ///// <returns>Paginated list of problems</returns>
        //[HttpGet]
        //public async Task<ActionResult<PaginatedList<GetProblemDTO>>> GetProblems(
        //    int pageNumber = 1,
        //    int pageSize = 10,
        //    Guid? idSearch = null,
        //    Guid? roundIdSearch = null,
        //    string? roundNameSearch = null)
        //{
        //    PaginatedList<GetProblemDTO> result = await _problemService.GetPaginatedProblemAsync(
        //        pageNumber, pageSize, idSearch, roundIdSearch, roundNameSearch);

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
        //                message: "Problem retrieved successfully."
        //            ));
        //}

        /// <summary>
        /// Update an existing problem
        /// </summary>
        /// <param name="id">Problem ID</param>
        /// <param name="problemDTO">Updated problem data</param>
        /// <returns>No content if successful</returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProblem(Guid id, UpdateProblemDTO problemDTO)
        {
            await _problemService.UpdateProblemAsync(id, problemDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update Problem successfully."
                    )); ;
        }

        ///// <summary>
        ///// Delete a problem
        ///// </summary>
        ///// <param name="id">Problem ID to delete</param>
        ///// <returns>No content if successful</returns>
        //[HttpDelete("{id}")]
        //public async Task<IActionResult> DeleteProblem(Guid id)
        //{
        //    await _problemService.DeleteProblemAsync(id);
        //    return Ok(new BaseResponseModel(
        //                statusCode: StatusCodes.Status200OK,
        //                code: ResponseCodeConstants.SUCCESS,
        //                message: "Delete Problem successfully."
        //            )); ;
        //}

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
        public async Task<IActionResult> CreateRubric(Guid roundId , CreateRubricDTO createRubricDTO)
        {
            RubricTemplateDTO template = await _problemService.CreateRubricAsync(roundId, createRubricDTO);

            return Ok(new BaseResponseModel<RubricTemplateDTO>(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                data: template,
                message: "Rubric created successfully."
            ));
        }

        /// <summary>
        /// Update rubric (scoring criteria) for a manual problem in a specific round
        /// </summary>
        /// <param name="roundId">Round ID</param>
        /// <param name="updateRubricDTO">Updated rubric criteria</param>
        /// <returns>Updated rubric template</returns>
        [HttpPut("rounds/{roundId}/rubric")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> UpdateRubric(Guid roundId,UpdateRubricDTO updateRubricDTO)
        {
            RubricTemplateDTO template = await _problemService.UpdateRubricAsync(roundId, updateRubricDTO);

            return Ok(new BaseResponseModel<RubricTemplateDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: template,
                message: "Rubric updated successfully."
            ));
        }
    }
}
