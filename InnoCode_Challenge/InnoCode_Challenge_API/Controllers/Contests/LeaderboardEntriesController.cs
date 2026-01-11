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
            GetLeaderboardEntryDTO? result = await _leaderboardService.GetLeaderboardAsync(
                pageNumber, pageSize, contestId);

            if (result == null)
            {
                return Ok(new BaseResponseModel<object>(
                    statusCode: StatusCodes.Status200OK,
                    code: ResponseCodeConstants.SUCCESS,
                    data: result,
                    message: "Leaderboard retrieved successfully."
                ));
            }

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


        [HttpGet("contests/{contestId}/teams")]
        public async Task<IActionResult> GetAllTeamsInContest(
            Guid contestId,
            int pageNumber = 1,
            int pageSize = 10
            )
        {
            var teams = await _leaderboardService.GetAllTeamsInContestAsync(pageNumber, pageSize, contestId);

            var paging = new
            {
                PageNumber = teams.PageNumber,
                PageSize = teams.PageSize,
                TotalPages = teams.TotalPages,
                TotalCount = teams.TotalCount,
                HasPreviousPage = teams.HasPreviousPage,
                HasNextPage = teams.HasNextPage
            };

            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: teams.Items,
                additionalData: paging,
                message: "Teams retrieved successfully."
            ));
        }

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
        public async Task<IActionResult> SetTeamScore(
            Guid contestId,
            Guid teamId,
            double newScore)
        {
            await _leaderboardService.SetTeamScoreAsync(contestId, teamId, newScore);
            return Ok(new BaseResponseModel(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                message: "Team score updated"
            ));
        }
    }
}
