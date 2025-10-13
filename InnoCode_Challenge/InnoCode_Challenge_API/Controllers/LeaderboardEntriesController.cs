using BusinessLogic.IServices;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.ResponseModel;
using Utility.Constant;
using Utility.PaginatedList;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/leaderboard-entries")]
    [ApiController]
    public class LeaderboardEntriesController : ControllerBase
    {
        private readonly ILeaderboardEntryService _leaderboardEntryService;

        public LeaderboardEntriesController(ILeaderboardEntryService leaderboardEntryService)
        {
            _leaderboardEntryService = leaderboardEntryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetPaginatedLeaderboardAsync(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? contestIdSearch = null,
            string? contestNameSearch = null)
        {
            PaginatedList<GetLeaderboardEntryDTO> result = await _leaderboardEntryService.GetPaginatedLeaderboardAsync(pageNumber, pageSize, idSearch, contestIdSearch, contestNameSearch);
            return Ok(new BaseResponseModel<PaginatedList<GetLeaderboardEntryDTO>>(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        data: result,
                        message: "Leaderboards retrieved successfully."
                    ));
        }

        [HttpPost]
        public async Task<IActionResult> CreateLeaderboard(CreateLeaderboardEntryDTO leaderboardDTO)
        {
            await _leaderboardEntryService.CreateLeaderboardAsync(leaderboardDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status201Created,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Create leaderboard successfully."
                    ));
        }

        [HttpPut]
        public async Task<IActionResult> UpdateLeaderboard(UpdateLeaderboardEntryDTO leaderboardDTO)
        {
            await _leaderboardEntryService.UpdateLeaderboardAsync(leaderboardDTO);
            return Ok(new BaseResponseModel(
                        statusCode: StatusCodes.Status200OK,
                        code: ResponseCodeConstants.SUCCESS,
                        message: "Update leaderboard successfully."
                    ));
        }
    }
}
