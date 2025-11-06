using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.ContestDTOs;
using Repository.ResponseModel;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Contests
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContestJudgeController : ControllerBase
    {
        private readonly IContestJudgeService _contestJudgeService;

        public ContestJudgeController(IContestJudgeService contestJudgeService)
        {
            _contestJudgeService = contestJudgeService;
        }

        [Authorize(Roles = RoleConstants.Judge)]
        [HttpPost("participate")]
        public async Task<IActionResult> Participate([FromBody] JudgeContestDTO dto)
        {
            await _contestJudgeService.ParticipateAsync(dto);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Judge participation recorded."
            );

            return Ok(response);
        }

        [Authorize(Roles = RoleConstants.Judge)]
        [HttpPost("leave")]
        public async Task<IActionResult> Leave([FromBody] JudgeContestDTO dto)
        {
            await _contestJudgeService.LeaveAsync(dto);

            var response = new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: null,
                message: "Judge removed from contest."
            );

            return Ok(response);
        }
        [Authorize(Roles = RoleConstants.Admin + "," + RoleConstants.ContestOrganizer)]
        [HttpGet("{contestId:guid}/judges")]
        public async Task<IActionResult> GetJudges(Guid contestId)
        {
            var data = await _contestJudgeService.GetJudgesByContestAsync(contestId);

            var response = new BaseResponseModel<IList<JudgeInContestDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "Judges retrieved."
            );

            return Ok(response);
        }

        [Authorize(Roles = RoleConstants.Judge)]
        [HttpGet("my-contests")]
        public async Task<IActionResult> GetMyContests()
        {
            var data = await _contestJudgeService.GetContestsOfCurrentJudgeAsync();

            var response = new BaseResponseModel<IList<JudgeContestDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "Contests retrieved."
            );

            return Ok(response);
        }

        [Authorize(Roles = RoleConstants.Admin)]
        [HttpGet("judge/{judgeId:guid}/contests")]
        public async Task<IActionResult> GetContestsOfJudge(Guid judgeId)
        {
            var data = await _contestJudgeService.GetContestsOfJudgeAsync(judgeId);

            var response = new BaseResponseModel<IList<JudgeContestDTO>>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "Contests retrieved."
            );

            return Ok(response);
        }

    }
}
