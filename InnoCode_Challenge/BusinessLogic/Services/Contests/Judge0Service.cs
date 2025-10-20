using System.Net.Http.Json;
using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Configuration;
using Repository.DTOs.JudgeDTOs;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;

namespace BusinessLogic.Services.Contests
{
    public class Judge0Service : IJudge0Service
    {
        private readonly HttpClient _httpClient;
        private readonly string _judge0BaseUrl;
        private readonly string _apiKey;
        private const int POLLING_INTERVAL_MS = 1000;
        private const int MAX_ATTEMPT = 10;

        public Judge0Service(IConfiguration configuration, HttpClient httpClient)
        {
            _httpClient = httpClient;
            _judge0BaseUrl = configuration["Judge0:BaseUrl"] ?? "https://judge0-ce.p.rapidapi.com";
            _apiKey = configuration["Judge0:ApiKey"] ?? "";

            _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Host", "judge0-ce.p.rapidapi.com");
        }

        public async Task<JudgeSubmissionResultDTO> AutoEvaluateSubmissionAsync(JudgeSubmissionRequestDTO request)
        {
            JudgeSubmissionResultDTO result = new JudgeSubmissionResultDTO
            {
                ProblemId = request.Problem.Id,
                Language = SubmissionHelpers.ConvertIdToJudge0Language(request.LanguageId),
                Summary = new JudgeSummaryDTO(),
                Cases = new List<JudgeCaseResultDTO>()
            };

            // Process each test case
            foreach (JudgeTestCaseDTO testCase in request.TestCases)
            {
                // Create submission for this test case
                JudgeSubmissionTokenDTO submissionResponse = await CreateSubmission(
                    request.LanguageId,
                    request.Code,
                    testCase.Stdin,
                    testCase.ExpectedOutput,
                    request.TimeLimitSec,
                    request.MemoryLimitKb);

                // Wait for results and check status
                JudgeCaseResultDTO judgeCaseResult = await PollSubmissionResult(submissionResponse.Token, testCase);
                result.Cases.Add(judgeCaseResult);
            }

            // Update summary
            result.Summary.Total = result.Cases.Count;
            result.Summary.Passed = result.Cases.Count(c => c.Status == "success");
            result.Summary.Failed = result.Summary.Total - result.Summary.Passed;

            return result;
        }

        private async Task<JudgeSubmissionTokenDTO> CreateSubmission(
            int languageId,
            string code,
            string stdin,
            string expectedOutput,
            double timeLimitSec,
            int memoryLimitKb)
        {
            var submissionRequest = new
            {
                source_code = code,
                language_id = languageId,
                stdin = stdin,
                expected_output = expectedOutput,
                cpu_time_limit = timeLimitSec,
                memory_limit = memoryLimitKb
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"{_judge0BaseUrl}/submissions?base64_encoded=false&wait=false", submissionRequest);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<JudgeSubmissionTokenDTO>()
                ?? throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Failed to deserialize Judge0 submission response");
        }

        private async Task<JudgeCaseResultDTO> PollSubmissionResult(string token, JudgeTestCaseDTO testCase)
        {
            // Poll for results (with timeout)
            int attempts = 0;

            while (attempts < MAX_ATTEMPT)
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_judge0BaseUrl}/submissions/{token}?base64_encoded=false");

                if (response.IsSuccessStatusCode)
                {
                    JudgeResponseDTO? result = await response.Content.ReadFromJsonAsync<JudgeResponseDTO>();

                    // If processing is done
                    if (result?.Status?.Id != 1 && result?.Status?.Id != 2)
                    {
                        return new JudgeCaseResultDTO
                        {
                            Id = testCase.Id,
                            Status = result?.Status?.Id == 3 ? "success" : "failed",
                            Judge0StatusId = result?.Status?.Id ?? 0,
                            Judge0Status = Judge0Helpers.ConvertToJudge0StatusString(result?.Status?.Id),
                            Expected = testCase.ExpectedOutput,
                            Actual = result?.Stdout ?? "",
                            Stderr = result?.Stderr,
                            CompileOutput = result?.CompileOutput,
                            Time = result?.Time,
                            MemoryKb = result?.Memory,
                            Token = token
                        };
                    }
                }

                attempts++;
                await Task.Delay(POLLING_INTERVAL_MS);
            }

            // If we reach here, the submission timed out
            return new JudgeCaseResultDTO
            {
                Id = testCase.Id,
                Status = "error",
                Judge0StatusId = 4,
                Judge0Status = Judge0StatusEnum.Error.ToString(),
                Expected = testCase.ExpectedOutput,
                Actual = "",
                Stderr = "Submission timed out",
                Token = token
            };
        }

    }
}
