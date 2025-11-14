using System.Net.Http.Json;
using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Http;
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
        private const int POLLING_INTERVAL_MS = 2000;
        private const int MAX_ATTEMPT = 15;
        private const int BATCH_SIZE = 20;

        public Judge0Service(IConfiguration configuration, HttpClient httpClient)
        {
            _httpClient = httpClient;
            _judge0BaseUrl = configuration["Judge0:BaseUrl"] ?? "https://judge0-ce.p.rapidapi.com";
            _apiKey = configuration["Judge0:ApiKey"] ?? "";

            _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Host", "judge0-ce.p.rapidapi.com");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
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

            try
            {
                // Use batch submission if there are multiple test cases
                if (request.TestCases.Count > 1)
                {
                    await ProcessBatchSubmissions(request, result);
                }
                else
                {
                    // Single submission for one test case
                    await ProcessSingleSubmissions(request, result);
                }

                // Update summary
                result.Summary.Total = result.Cases.Count;
                result.Summary.Passed = result.Cases.Count(c => c.Status == "success");
                result.Summary.Failed = result.Summary.Total - result.Summary.Passed;

                return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new ErrorException(
                    StatusCodes.Status429TooManyRequests,
                    ResponseCodeConstants.BADREQUEST,
                    "Rate limit exceeded. Please try again in a few moments."
                );
            }
        }

        private async Task ProcessBatchSubmissions(JudgeSubmissionRequestDTO request, JudgeSubmissionResultDTO result)
        {
            // Split test cases into batches
            for (int i = 0; i < request.TestCases.Count; i += BATCH_SIZE)
            {
                var batch = request.TestCases.Skip(i).Take(BATCH_SIZE).ToList();

                // Create batch submission
                var batchTokens = await CreateBatchSubmission(
                    request.LanguageId,
                    request.Code,
                    batch,
                    request.TimeLimitSec,
                    request.MemoryLimitKb);

                // Add delay between batches to avoid rate limiting
                if (i + BATCH_SIZE < request.TestCases.Count)
                {
                    await Task.Delay(1000);
                }

                // Poll for batch results
                var batchResults = await PollBatchSubmissionResults(batchTokens, batch);
                result.Cases.AddRange(batchResults);
            }
        }

        private async Task ProcessSingleSubmissions(JudgeSubmissionRequestDTO request, JudgeSubmissionResultDTO result)
        {
            // Process each test case with delay
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

                // Add delay between submissions to avoid rate limiting
                if (request.TestCases.IndexOf(testCase) < request.TestCases.Count - 1)
                {
                    await Task.Delay(500); // 500ms delay between submissions
                }
            }
        }

        private async Task<List<string>> CreateBatchSubmission(
            int languageId,
            string code,
            List<JudgeTestCaseDTO> testCases,
            double timeLimitSec,
            int memoryLimitKb)
        {
            var submissions = testCases.Select(tc => new
            {
                source_code = code,
                language_id = languageId,
                stdin = tc.Stdin,
                expected_output = tc.ExpectedOutput,
                cpu_time_limit = timeLimitSec,
                memory_limit = memoryLimitKb
            }).ToArray();

            var batchRequest = new
            {
                submissions = submissions
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                $"{_judge0BaseUrl}/submissions/batch?base64_encoded=false",
                batchRequest);

            response.EnsureSuccessStatusCode();

            var batchResponse = await response.Content.ReadFromJsonAsync<List<JudgeSubmissionTokenDTO>>()
                ?? throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "Failed to deserialize Judge0 batch submission response");

            return batchResponse.Select(r => r.Token).ToList();
        }

        private async Task<List<JudgeCaseResultDTO>> PollBatchSubmissionResults(
            List<string> tokens,
            List<JudgeTestCaseDTO> testCases)
        {
            var results = new List<JudgeCaseResultDTO>();
            int attempts = 0;

            // Create a map of remaining tokens to poll
            var pendingTokens = new Dictionary<string, JudgeTestCaseDTO>();
            for (int i = 0; i < tokens.Count; i++)
            {
                pendingTokens[tokens[i]] = testCases[i];
            }

            while (pendingTokens.Any() && attempts < MAX_ATTEMPT)
            {
                var tokensParam = string.Join(",", pendingTokens.Keys);

                HttpResponseMessage response = await _httpClient.GetAsync(
                    $"{_judge0BaseUrl}/submissions/batch?tokens={tokensParam}&base64_encoded=false");

                if (response.IsSuccessStatusCode)
                {
                    var batchResults = await response.Content.ReadFromJsonAsync<BatchSubmissionsResponse>()
                        ?? throw new ErrorException(StatusCodes.Status500InternalServerError,
                            ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                            "Failed to deserialize Judge0 batch response");

                    var completedTokens = new List<string>();

                    foreach (var submission in batchResults.Submissions)
                    {
                        // If processing is done
                        if (submission.Status?.Id != 1 && submission.Status?.Id != 2)
                        {
                            var testCase = pendingTokens[submission.Token];

                            results.Add(new JudgeCaseResultDTO
                            {
                                Id = testCase.Id,
                                Status = submission.Status?.Id == 3 ? "success" : "failed",
                                Judge0StatusId = submission.Status?.Id ?? 0,
                                Judge0Status = Judge0Helpers.ConvertToJudge0StatusString(submission.Status?.Id),
                                Expected = testCase.ExpectedOutput.Trim(),
                                Actual = (submission.Stdout ?? "").Trim(),
                                Stderr = submission.Stderr?.Trim(),
                                CompileOutput = submission.CompileOutput?.Trim(),
                                Time = submission.Time,
                                MemoryKb = submission.Memory,
                                Token = submission.Token
                            });

                            completedTokens.Add(submission.Token);
                        }
                    }

                    // Remove completed submissions
                    foreach (var token in completedTokens)
                    {
                        pendingTokens.Remove(token);
                    }
                }

                if (pendingTokens.Any())
                {
                    attempts++;
                    await Task.Delay(POLLING_INTERVAL_MS);
                }
            }

            // Handle any remaining timed-out submissions
            foreach (var kvp in pendingTokens)
            {
                results.Add(new JudgeCaseResultDTO
                {
                    Id = kvp.Value.Id,
                    Status = "error",
                    Judge0StatusId = 4,
                    Judge0Status = Judge0StatusEnum.Error.ToString(),
                    Expected = kvp.Value.ExpectedOutput,
                    Actual = "",
                    Stderr = "Submission timed out",
                    Token = kvp.Key
                });
            }

            return results;
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

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                $"{_judge0BaseUrl}/submissions?base64_encoded=false&wait=false",
                submissionRequest);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<JudgeSubmissionTokenDTO>()
                ?? throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "Failed to deserialize Judge0 submission response");
        }

        private async Task<JudgeCaseResultDTO> PollSubmissionResult(string token, JudgeTestCaseDTO testCase)
        {
            int attempts = 0;

            while (attempts < MAX_ATTEMPT)
            {
                HttpResponseMessage response = await _httpClient.GetAsync(
                    $"{_judge0BaseUrl}/submissions/{token}?base64_encoded=false");

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
                            Expected = testCase.ExpectedOutput.Trim(),
                            Actual = (result?.Stdout ?? "").Trim(),
                            Stderr = result?.Stderr?.Trim(),
                            CompileOutput = result?.CompileOutput?.Trim(),
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

        // Helper class for batch response deserialization
        private class BatchSubmissionsResponse
        {
            public List<JudgeResponseDTO> Submissions { get; set; } = new();
        }

    }
}
