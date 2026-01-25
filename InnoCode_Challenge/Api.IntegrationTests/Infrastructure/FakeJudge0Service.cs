using BusinessLogic.IServices.Contests;
using Repository.DTOs.JudgeDTOs;
using Utility.Enums;
using Utility.Helpers;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class FakeJudge0Service : IJudge0Service
    {
        public Task<JudgeSubmissionResultDTO> AutoEvaluateSubmissionAsync(JudgeSubmissionRequestDTO request)
        {
            var cases = request.TestCases.Select(tc => new JudgeCaseResultDTO
            {
                Id = tc.Id,
                Status = "success",
                Judge0StatusId = 3,
                Judge0Status = Judge0StatusEnum.Accepted.ToString(),
                Expected = tc.ExpectedOutput?.Trim() ?? string.Empty,
                Actual = tc.ExpectedOutput?.Trim() ?? string.Empty,
                Stdout = tc.ExpectedOutput?.Trim() ?? string.Empty,
                Stderr = string.Empty,
                Time = "0.01",
                MemoryKb = 1024,
                Token = $"fake_{tc.Id}"
            }).ToList();

            return Task.FromResult(new JudgeSubmissionResultDTO
            {
                ProblemId = request.Problem.Id,
                Language = SubmissionHelpers.ConvertIdToJudge0Language(request.LanguageId),
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
