using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ProblemDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
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

        /// <summary>
        /// Create a new problem
        /// </summary>
        /// <param name="problemDTO"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateProblem(CreateProblemDTO problemDTO)
        {
            await _problemService.CreateProblemAsync(problemDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Problem successfully."
                    ));
        }

        /// <summary>
        /// Get paginated problems with optional filters
        /// </summary>
        /// <param name="pageNumber">Current page number</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="idSearch">Optional problem ID filter</param>
        /// <param name="roundIdSearch">Optional round ID filter</param>
        /// <param name="roundNameSearch">Optional round name filter</param>
        /// <returns>Paginated list of problems</returns>
        [HttpGet]
        public async Task<ActionResult<PaginatedList<GetProblemDTO>>> GetProblems(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? roundIdSearch = null,
            string? roundNameSearch = null)
        {
            PaginatedList<GetProblemDTO> result = await _problemService.GetPaginatedProblemAsync(
                pageNumber, pageSize, idSearch, roundIdSearch, roundNameSearch);

            return Ok(new BaseResponseModel<PaginatedList<GetProblemDTO>>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result,
                        message: "Problem retrieved successfully."
                    ));
        }

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

        /// <summary>
        /// Delete a problem
        /// </summary>
        /// <param name="id">Problem ID to delete</param>
        /// <returns>No content if successful</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProblem(Guid id)
        {
            await _problemService.DeleteProblemAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Problem successfully."
                    )); ;
        }
    }
}
