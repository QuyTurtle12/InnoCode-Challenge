using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/leaderboard-entries")]
    [ApiController]
    public class LeaderboardEntriesController : ControllerBase
    {
        private readonly ILeaderboardEntryService _leaderboardService;

        public LeaderboardEntriesController(ILeaderboardEntryService leaderboardService)
        {
            _leaderboardService = leaderboardService;
        }

        /// <summary>
        /// Get paginated leaderboard
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLeaderboard(
            int pageNumber = 1,
            int pageSize = 10,
            Guid? idSearch = null,
            Guid? contestIdSearch = null,
            string? contestNameSearch = null)
        {
            var result = await _leaderboardService.GetPaginatedLeaderboardAsync(
                pageNumber, pageSize, idSearch, contestIdSearch, contestNameSearch);

            var paging = new
            {
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.TotalCount,
                result.HasPreviousPage,
                result.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result.Items,
                additionalData: paging,
                message: "Leaderboard retrieved successfully."
            ));
        }

        /// <summary>
        /// Create initial leaderboard
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> CreateLeaderboard(CreateLeaderboardEntryDTO dto)
        {
            await _leaderboardService.CreateLeaderboardAsync(dto);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status201Created,
                code: ResponseCodeConstants.SUCCESS,
                message: "Leaderboard created successfully."
            ));
        }

        /// <summary>
        /// Update leaderboard rankings
        /// </summary>
        [HttpPut]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> UpdateLeaderboard([FromBody] UpdateLeaderboardEntryDTO dto)
        {
            await _leaderboardService.UpdateLeaderboardAsync(dto);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Leaderboard updated successfully."
            ));
        }

        /// <summary>
        /// Update team score in real-time
        /// </summary>
        //[HttpPut("score")]
        //[Authorize(Policy = "RequireStaffOrAdmin")]
        //public async Task<IActionResult> UpdateTeamScore(
        //    Guid contestId,
        //    Guid teamId,
        //    double newScore)
        //{
        //    await _leaderboardService.UpdateTeamScoreAsync(contestId, teamId, newScore);
        //    return Ok(new BaseResponseModel(
        //        statusCode: StatusCodes.Status200OK,
        //        code: ResponseCodeConstants.SUCCESS,
        //        message: "Team score updated and broadcasted successfully."
        //    ));
        //}
    }
}
