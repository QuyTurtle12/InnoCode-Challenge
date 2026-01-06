using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.MockTestDTOs;
using Repository.IRepositories;
using System.Net.Http.Json;
using System.Text.Json;
using Utility.Constant;
using Utility.ExceptionCustom;

namespace BusinessLogic.Services.Contests
{
    public class MockTestService : IMockTestService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MockTestService> _logger;
        private readonly IUOW _unitOfWork;

        private const string PISTON_URL = "https://emkc.org/api/v2/piston/execute";
        private const string PYTHON_VERSION = "3.10.0";
        private const string PROGRAMMING_LANGUAGE = "python3";

        public MockTestService(
            HttpClient httpClient,
            ILogger<MockTestService> logger,
            IUOW unitOfWork)
        {
            _httpClient = httpClient;
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public async Task<MockTestResultDTO> ExecuteMockTestAsync(
            string studentCode,
            string mockTestUrl,
            int timeLimitSec = 30,
            int memoryLimitMb = 512)
        {
            try
            {
                // Download mock test file
                string mockTestCode = await DownloadMockTestAsync(mockTestUrl);

                // Combine student code with mock tests
                string combinedScript = BuildTestScript(studentCode, mockTestCode);

                // Execute via Piston
                MockTestResultDTO result = await ExecuteViaPistonAsync(combinedScript, timeLimitSec, memoryLimitMb);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing mock test via Piston for URL: {Url}", mockTestUrl);
                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Mock test execution failed: {ex.Message}");
            }
        }

        private async Task<string> DownloadMockTestAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download mock test from {Url}", url);
                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Failed to download mock test file: {ex.Message}");
            }
        }

        private string BuildTestScript(string studentCode, string mockTestCode)
        {
            var sb = new System.Text.StringBuilder();

            // Add required imports
            sb.AppendLine("import unittest");
            sb.AppendLine("from unittest.mock import Mock, patch, MagicMock");
            sb.AppendLine("import json");
            sb.AppendLine("import sys");
            sb.AppendLine("from io import StringIO");
            sb.AppendLine();

            // Add student code
            sb.AppendLine("# ===== STUDENT CODE =====");
            sb.AppendLine(studentCode);
            sb.AppendLine();

            // Add mock test code
            sb.AppendLine("# ===== MOCK TEST CODE =====");
            sb.AppendLine(mockTestCode);
            sb.AppendLine();

            // Add test runner that outputs JSON
            sb.AppendLine(@"
            if __name__ == '__main__':
                # Redirect output to capture results
                test_output = StringIO()
                runner = unittest.TextTestRunner(stream=test_output, verbosity=2)
    
                # Load and run tests
                loader = unittest.TestLoader()
                suite = loader.loadTestsFromModule(sys.modules[__name__])
                result = runner.run(suite)
    
                # Parse test results
                results = {
                    'total': result.testsRun,
                    'passed': result.testsRun - len(result.failures) - len(result.errors),
                    'failed': len(result.failures),
                    'errors': len(result.errors),
                    'success': result.wasSuccessful(),
                    'details': []
                }
    
                # Add failure details
                for test, traceback in result.failures + result.errors:
                    results['details'].append({
                        'test': str(test),
                        'status': 'failed',
                        'message': traceback.split('\\n')[-2] if '\\n' in traceback else traceback[:100]
                    })
    
                # Add passed test details
                passed_count = result.testsRun - len(result.failures) - len(result.errors)
                for i in range(passed_count):
                    results['details'].append({
                        'test': 'test_' + str(i + 1),
                        'status': 'passed',
                        'message': ''
                    })
    
                # Output JSON marker and results
                print('===MOCK_TEST_RESULTS===')
                print(json.dumps(results))
            ");

            return sb.ToString();
        }

        private async Task<MockTestResultDTO> ExecuteViaPistonAsync(
            string script,
            int timeLimitSec,
            int memoryLimitMb)
        {
            try
            {
                // Prepare Piston request
                var pistonRequest = new PistonExecuteRequest
                {
                    Language = PROGRAMMING_LANGUAGE,
                    Version = PYTHON_VERSION,
                    Files = new[]
                    {
                        new PistonFile { Content = script }
                    },
                    CompileTimeout = 10000, // 10 seconds
                    RunTimeout = timeLimitSec * 1000, // Convert to milliseconds
                    CompileMemoryLimit = memoryLimitMb * 1024 * 1024, // Convert to bytes
                    RunMemoryLimit = memoryLimitMb * 1024 * 1024
                };

                // Send request to Piston
                var response = await _httpClient.PostAsJsonAsync(PISTON_URL, pistonRequest);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Piston API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    throw new ErrorException(
                        StatusCodes.Status502BadGateway,
                        ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        $"Piston API failed with status {response.StatusCode}");
                }

                var pistonResult = await response.Content.ReadFromJsonAsync<PistonExecuteResponse>();

                if (pistonResult == null)
                {
                    throw new ErrorException(
                        StatusCodes.Status500InternalServerError,
                        ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        "Failed to parse Piston response");
                }

                // Parse and return results
                return ParsePistonResult(pistonResult);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error calling Piston API");
                throw new ErrorException(
                    StatusCodes.Status503ServiceUnavailable,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "Code execution service is temporarily unavailable");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Piston API request timeout");
                throw new ErrorException(
                    StatusCodes.Status408RequestTimeout,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    "Code execution timed out");
            }
        }

        private MockTestResultDTO ParsePistonResult(PistonExecuteResponse pistonResult)
        {
            string output = pistonResult.Run?.Output ?? string.Empty;
            string stderr = pistonResult.Run?.Stderr ?? string.Empty;

            // Look for JSON results marker
            int markerIndex = output.IndexOf("===MOCK_TEST_RESULTS===");

            if (markerIndex < 0)
            {
                _logger.LogWarning("Failed to find test results marker in output. Output: {Output}", output);
                return new MockTestResultDTO
                {
                    Id = Guid.NewGuid(),
                    ProblemId = string.Empty,
                    Summary = new MockTestSummaryDTO
                    {
                        Total = 0,
                        Passed = 0,
                        Failed = 0,
                        rawScore = 0,
                        penaltyScore = 0
                    },
                    Language = PROGRAMMING_LANGUAGE,
                    ErrorMessage = "Failed to parse test results. " + (string.IsNullOrEmpty(stderr) ? output : stderr),
                    Details = new List<MockTestCaseDetail>()
                };
            }

            // Extract JSON part
            string jsonPart = output.Substring(markerIndex + "===MOCK_TEST_RESULTS===".Length).Trim();

            try
            {
                // Configure JsonSerializer to be case-insensitive
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                MockTestRawResult? rawResult = JsonSerializer.Deserialize<MockTestRawResult>(jsonPart, options);

                if (rawResult == null)
                {
                    throw new JsonException("Deserialization returned null");
                }

                return new MockTestResultDTO
                {
                    Id = Guid.NewGuid(),
                    ProblemId = string.Empty,
                    Summary = new MockTestSummaryDTO
                    {
                        Total = rawResult.Total,
                        Passed = rawResult.Passed,
                        Failed = rawResult.Failed + rawResult.Errors,
                        rawScore = 0,
                        penaltyScore = 0
                    },
                    Language = PROGRAMMING_LANGUAGE,
                    ErrorMessage = string.IsNullOrEmpty(stderr) ? null : stderr,
                    Details = rawResult.Details?.Select(d => new MockTestCaseDetail
                    {
                        TestName = d.Test ?? "Unknown",
                        Status = d.Status ?? "error",
                        Message = d.Message ?? ""
                    }).ToList() ?? new List<MockTestCaseDetail>()
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON results: {Json}", jsonPart);
                return new MockTestResultDTO
                {
                    Id = Guid.NewGuid(),
                    ProblemId = string.Empty,
                    Summary = new MockTestSummaryDTO
                    {
                        Total = 0,
                        Passed = 0,
                        Failed = 0,
                        rawScore = 0,
                        penaltyScore = 0
                    },
                    Language = PROGRAMMING_LANGUAGE,
                    ErrorMessage = $"JSON parse error: {ex.Message}",
                    Details = new List<MockTestCaseDetail>()
                };
            }
        }
    }
}
