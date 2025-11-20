//using BusinessLogic.IServices.Contests;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Repository.DTOs.ProblemDTOs;
//using Repository.DTOs.RubricDTOs;
//using Repository.ResponseModel;
//using Utility.Constant;

//namespace InnoCode_Challenge_API.Controllers.Contests
//{
//    [Route("api/[controller]")]
//    [ApiController]
//    public class ProblemsController : ControllerBase
//    {
//        private readonly IProblemService _problemService;

//        // Constructor
//        public ProblemsController(IProblemService problemService)
//        {
//            _problemService = problemService;
//        }

//        /// <summary>
//        /// Update an existing problem
//        /// </summary>
//        /// <param name="id">Problem ID</param>
//        /// <param name="problemDTO">Updated problem data</param>
//        /// <returns>No content if successful</returns>
//        [HttpPut("{id}")]
//        public async Task<IActionResult> UpdateProblem(Guid id, UpdateProblemDTO problemDTO)
//        {
//            await _problemService.UpdateProblemAsync(id, problemDTO);
//            return Ok(new BaseResponseModel(
//                        statusCode: StatusCodes.Status200OK,
//                        code: ResponseCodeConstants.SUCCESS,
//                        message: "Update Problem successfully."
//                    )); ;
//        }

//        /// <summary>
//        /// Get rubric template (scoring criteria) for a problem
//        /// </summary>
//        /// <param name="roundId">Round ID</param>
//        /// <returns>Rubric template with all criteria</returns>
//        [HttpGet("rounds/{roundId}/rubric")]
//        public async Task<IActionResult> GetRubricTemplate(Guid roundId)
//        {
//            RubricTemplateDTO template = await _problemService.GetRubricTemplateAsync(roundId);

//            return Ok(new BaseResponseModel<RubricTemplateDTO>(
//                statusCode: StatusCodes.Status200OK,
//                code: ResponseCodeConstants.SUCCESS,
//                data: template,
//                message: "Rubric template retrieved successfully."
//            ));
//        }

//        /// <summary>
//        /// Create rubric (scoring criteria) for a manual problem in a specific round
//        /// </summary>
//        /// <param name="roundId">Round ID</param>
//        /// <param name="createRubricDTO">Rubric criteria to create</param>
//        /// <returns>Created rubric template</returns>
//        [HttpPost("rounds/{roundId}/rubric")]
//        [Authorize(Policy = "RequireOrganizerRole")]
//        public async Task<IActionResult> CreateRubric(Guid roundId, CreateRubricDTO createRubricDTO)
//        {
//            RubricTemplateDTO template = await _problemService.CreateRubricCriterionAsync(roundId, createRubricDTO);

//            return Ok(new BaseResponseModel<RubricTemplateDTO>(
//                statusCode: StatusCodes.Status201Created,
//                code: ResponseCodeConstants.SUCCESS,
//                data: template,
//                message: "Rubric created successfully."
//            ));
//        }

//        /// <summary>
//        /// Update rubric (scoring criteria) for a manual problem in a specific round
//        /// </summary>
//        /// <param name="roundId">Round ID</param>
//        /// <param name="updateRubricDTO">Updated rubric criteria</param>
//        /// <returns>Updated rubric template</returns>
//        [HttpPut("rounds/{roundId}/rubric")]
//        [Authorize(Policy = "RequireOrganizerRole")]
//        public async Task<IActionResult> UpdateRubric(Guid roundId, UpdateRubricDTO updateRubricDTO)
//        {
//            RubricTemplateDTO template = await _problemService.UpdateRubricCriterionAsync(roundId, updateRubricDTO);

//            return Ok(new BaseResponseModel<RubricTemplateDTO>(
//                statusCode: StatusCodes.Status200OK,
//                code: ResponseCodeConstants.SUCCESS,
//                data: template,
//                message: "Rubric updated successfully."
//            ));
//        }
//    }
//}
