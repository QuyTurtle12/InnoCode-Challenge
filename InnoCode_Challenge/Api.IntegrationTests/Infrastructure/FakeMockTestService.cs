using BusinessLogic.IServices.Contests;
using Repository.DTOs.JudgeDTOs;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class FakeMockTestService : IMockTestService
    {
        public Task<JudgeSubmissionResultDTO> ExecuteMockTestAsync(
            string studentCode,
            string mockTestUrl,
            int timeLimitSec = 20,
            int memoryLimitMb = 512)
        {
            var cases = new List<JudgeCaseResultDTO>
            {
                new JudgeCaseResultDTO
                {
                    Id = "mock-test-1",
                    Status = "success",
                    Expected = "1",
                    Actual = "1",
                    Stdout = "1",
                    Stderr = string.Empty,
                    Time = "0.01",
                    MemoryKb = 1024
                }
            };

            return Task.FromResult(new JudgeSubmissionResultDTO
            {
                Summary = new JudgeSummaryDTO
                {
                    Total = cases.Count,
                    Passed = cases.Count,
                    Failed = 0
                },
                Cases = cases
            });
        }
    }
}
