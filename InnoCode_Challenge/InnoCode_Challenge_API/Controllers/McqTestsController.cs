using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.McqTestDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
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
        /// Get paginated list of Mcq Tests with optional filtering by id and roundId
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="idSearch"></param>
        /// <param name="roundIdSearch"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetMcqTests(int pageNumber = 1, int pageSize = 10, 
            Guid? idSearch = null, Guid? roundIdSearch = null)
        {
            PaginatedList<GetMcqTestDTO> result = await _mcqTestService.GetPaginatedMcqTestAsync(pageNumber, pageSize, idSearch, roundIdSearch);
            return Ok(new BaseResponseModel<PaginatedList<GetMcqTestDTO>>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result,
                        message: "Mcq Test retrieved successfully."
                    ));
        }

        /// <summary>
        /// Create a new Mcq Test
        /// </summary>
        /// <param name="mcqTestDTO"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateMcqTest([FromBody] CreateMcqTestDTO mcqTestDTO)
        {
            await _mcqTestService.CreateMcqTest(mcqTestDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create Mcq Test successfully."
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
            await _mcqTestService.UpdateMcqTest(id, mcqTestDTO);
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
            await _mcqTestService.DeleteMcqTest(id);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Delete Mcq Test successfully."
                    ));
        }
    }
}
