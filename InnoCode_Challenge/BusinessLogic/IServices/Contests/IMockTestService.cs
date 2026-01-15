using Repository.DTOs.JudgeDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface IMockTestService
    {
        Task<JudgeSubmissionResultDTO> ExecuteMockTestAsync(
            string studentCode,
            string mockTestUrl,
            int timeLimitSec = 20,
            int memoryLimitMb = 512);
    }
}
