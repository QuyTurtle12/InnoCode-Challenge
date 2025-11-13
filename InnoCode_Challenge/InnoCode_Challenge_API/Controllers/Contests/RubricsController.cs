using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.Services.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.RubricDTOs.Repository.DTOs.RubricDTOs;
using Repository.DTOs.TestCaseDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.Enums;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/")]
    [ApiController]
    public class RubricsController : ControllerBase
    {
        private readonly IProblemService _problemService;
        private readonly IConfigService _configService;

        public RubricsController(IProblemService problemService, IConfigService configService)
        {
            _problemService = problemService;
            _configService = configService;
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

        /// <summary>
        /// Download rubric import template
        /// </summary>
        /// <returns></returns>
        [HttpGet("rubrics/template")]
        public async Task<IActionResult> DownloadRubricImportTemplate()
        {
            string url = await _configService.DownloadImportTemplate(ImportTemplateEnum.RubricTemplate);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: url,
                message: "Template downloaded successfully."
            ));
        }

        /// <summary>
        /// Import rubric criteria from CSV file
        /// </summary>
        /// <param name="csvFile"></param>
        /// <param name="roundId"></param>
        /// <returns></returns>
        [HttpPost("rounds/{roundId}/rubric/import-csv")]
        [Authorize(Policy = "RequireOrganizerRole")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportTestCasesFromCsv(
            IFormFile csvFile,
            [FromRoute] Guid roundId)
        {
            RubricCsvImportResultDTO result = await _problemService.ImportRubricFromCsvAsync(csvFile, roundId);

            return Ok(new BaseResponseModel<RubricCsvImportResultDTO>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                message: $"Import completed. {result.SuccessCount} rubric criteria imported to round '{result.RoundName}'"
            ));
        }
    }
}
