using System.ComponentModel.DataAnnotations;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
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
        [HttpGet("{contestId}")]
        public async Task<IActionResult> GetLeaderboard(
            [Required] Guid contestId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            GetLeaderboardEntryDTO result = await _leaderboardService.GetLeaderboardAsync(
                pageNumber, pageSize, contestId);

            // Calculate pagination info based on the team list
            int totalTeams = result.TotalTeamCount;
            int totalPages = (int)Math.Ceiling(totalTeams / (double)pageSize);
            bool hasPreviousPage = pageNumber > 1;
            bool hasNextPage = pageNumber < totalPages;

            var paging = new
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalCount = totalTeams,
                HasPreviousPage = hasPreviousPage,
                HasNextPage = hasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: result,
                additionalData: paging,
                message: "Leaderboard retrieved successfully."
            ));
        }

        ///// <summary>
        ///// Create initial leaderboard
        ///// </summary>
        //[HttpPost]
        //[Authorize(Policy = "RequireStaffOrAdmin")]
        //public async Task<IActionResult> CreateLeaderboard(CreateLeaderboardEntryDTO dto)
        //{
        //    await _leaderboardService.CreateLeaderboardAsync(dto);
        //    return Ok(new BaseResponseModel(
        //        statusCode: StatusCodes.Status201Created,
        //        code: ResponseCodeConstants.SUCCESS,
        //        message: "Leaderboard created successfully."
        //    ));
        //}

        ///// <summary>
        ///// Update leaderboard rankings
        ///// </summary>
        //[HttpPost("{contestId}/recalculate")]
        //[Authorize(Policy = "RequireStaffOrAdmin")]
        //public async Task<IActionResult> UpdateLeaderboard(Guid contestId)
        //{
        //    await _leaderboardService.UpdateLeaderboardAsync(contestId);
        //    return Ok(new BaseResponseModel(
        //        statusCode: StatusCodes.Status200OK,
        //        code: ResponseCodeConstants.SUCCESS,
        //        message: "Leaderboard updated successfully."
        //    ));
        //}

        /// <summary>
        /// Toggle leaderboard freeze status (Ongoing -> Paused or Paused -> Ongoing)
        /// </summary>
        /// <param name="contestId">Contest ID</param>
        /// <returns></returns>
        [HttpPut("contests/{contestId}/toggle-freeze")]
        [Authorize(Policy = "RequireOrganizerRole")]
        public async Task<IActionResult> ToggleLeaderboardFreeze(
            Guid contestId)
        {
            string newStatus = await _leaderboardService.ToggleLeaderboardFreezeAsync(contestId);

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: newStatus,
                message: $"Contest status changed to {newStatus} successfully."
            ));
        }

        /// <summary>
        /// Update team score (staff/admin only)
        /// </summary>
        [HttpPut("contests/{contestId}/teams/{teamId}/score")]
        [Authorize(Policy = "RequireStaffOrAdmin")]
        public async Task<IActionResult> UpdateTeamScore(
            Guid contestId,
            Guid teamId,
            double newScore)
        {
            await _leaderboardService.UpdateTeamScoreAsync(contestId, teamId, newScore);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Team score updated and broadcasted successfully."
            ));
        }
    }
}
