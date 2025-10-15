using BusinessLogic.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.AppealEvidenceDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Appeals
{
    [Route("api/appeal-evidences")]
    [ApiController]
    public class AppealEvidencesController : ControllerBase
    {
        private readonly IAppealEvidenceService _appealEvidenceService;

        // Constructor
        public AppealEvidencesController(IAppealEvidenceService appealEvidenceService)
        {
            _appealEvidenceService = appealEvidenceService;
        }

        /// <summary>
        /// Create a new Appeal Evidence
        /// </summary>
        /// <param name="appealEvidenceDTO"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateAppealEvidence(CreateAppealEvidenceDTO appealEvidenceDTO)
        {
            await _appealEvidenceService.CreateAppealEvidenceAsync(appealEvidenceDTO);
            return Ok(new BaseResponseModel(
                         statusCode: StatusCodes.Status201Created,
                         code: ResponseCodeConstants.SUCCESS,
                         message: "Create Appeal Evidence successfully."
                     ));
        }

        /// <summary>
        /// Delete an Appeal Evidence by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAppealEvidence(Guid id)
        {
            await _appealEvidenceService.DeleteAppealEvidenceAsync(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Appeal Evidence successfully."
                    ));
        }
    }
}
