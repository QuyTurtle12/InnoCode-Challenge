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
        public async Task<IActionResult> Participate([FromBody] JudgeParticipateContestDTO dto)
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
        public async Task<IActionResult> Leave([FromBody] JudgeParticipateContestDTO dto)
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

    }
}
