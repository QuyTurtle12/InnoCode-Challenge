using BusinessLogic.IServices.Submissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.DTOs.PlagiarismDTOs;

namespace InnoCode_Challenge_API.Controllers.Staffs
{
    [ApiController]
    [Route("api/staff/plagiarism")]
    [Authorize(Roles = "Staff,Admin")]
    public class StaffPlagiarismController : ControllerBase
    {
        private readonly ISubmissionService _submissionService;

        public StaffPlagiarismController(ISubmissionService submissionService)
        {
            _submissionService = submissionService;
        }

        [HttpGet("queue")]
        public async Task<IActionResult> GetQueue(
            int pageNumber = 1,
            int pageSize = 20,
            Guid? contestId = null,
            Guid? roundId = null,
            string? studentName = null,
            string? teamName = null)
        {
            var result = await _submissionService.GetPlagiarismQueueAsync(
                pageNumber, pageSize, contestId, roundId, studentName, teamName);

            return Ok(result);
        }

        [HttpGet("{submissionId:guid}")]
        public async Task<IActionResult> GetDetail(Guid submissionId)
        {
            var result = await _submissionService.GetPlagiarismSubmissionDetailAsync(submissionId);
            return Ok(result);
        }

        [HttpPost("{submissionId:guid}/resolve")]
        public async Task<IActionResult> Resolve(Guid submissionId, [FromBody] ResolvePlagiarismDTO dto)
        {
            await _submissionService.ResolvePlagiarismSubmissionAsync(submissionId, dto);
            return Ok(new { message = "Resolved successfully" });
        }
    }
}
