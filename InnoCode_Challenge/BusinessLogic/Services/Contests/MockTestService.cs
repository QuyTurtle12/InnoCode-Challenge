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
        private const int DEFAULT_EXECUTION_TIME_LIMIT_SECONDS = 30;
        private const int DEFAULT_EXECUTION_MEMORY_LIMIT = 2048;

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
            string userCode,
            string mockTestUrl,
            int timeLimitSec = DEFAULT_EXECUTION_TIME_LIMIT_SECONDS,
            int memoryLimitMb = DEFAULT_EXECUTION_MEMORY_LIMIT)
        {
            try
            {
                // Download mock test code from the provided URL
                string mockTestCode = await DownloadMockTestAsync(mockTestUrl);

                // Build the complete test script using the helper method
                string combinedScript = BuildTestScript(userCode, mockTestCode);

                _logger?.LogDebug("Executing test script (length: {Length})", combinedScript.Length);

                // Log the script for debugging
                _logger?.LogTrace("Test script:\n{Script}", combinedScript);

                // Execute the combined script using Piston service
                MockTestResultDTO executionResult = await ExecuteViaPistonAsync(
                    combinedScript,
                    timeLimitSec,
                    memoryLimitMb);

                return executionResult;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Mock test execution failed for URL: {Url}", mockTestUrl);

                return new MockTestResultDTO
                {
                    ErrorMessage = $"Failed to execute mock test: {ex.Message}",
                    Summary = new MockTestSummaryDTO
                    {
                        Total = 0,
                        Passed = 0,
                        Failed = 0,
                        rawScore = 0,
                        penaltyScore = 0
                    },
                    Details = new List<MockTestDetail>()
                };
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
            sb.AppendLine("import json");
            sb.AppendLine("import sys");
            sb.AppendLine();

            // Add student code
            sb.AppendLine("# ===== STUDENT CODE =====");
            string cleanedStudentCode = string.Join("\n",
                studentCode.Split('\n').Select(line => line.TrimEnd()));
            sb.AppendLine(cleanedStudentCode);
            sb.AppendLine();

            // Add mock test code
            sb.AppendLine("# ===== MOCK TEST CODE =====");
            string cleanedMockTestCode = DedentCode(mockTestCode);
            sb.AppendLine(cleanedMockTestCode);
            sb.AppendLine();

            // Add test runner and result capturing
            sb.AppendLine("if __name__ == '__main__':");
            sb.AppendLine("    # Custom result class to capture test names");
            sb.AppendLine("    class JsonTestResult(unittest.TextTestResult):");
            sb.AppendLine("        def __init__(self, stream, descriptions, verbosity):");
            sb.AppendLine("            super().__init__(stream, descriptions, verbosity)");
            sb.AppendLine("            self.test_results = []");
            sb.AppendLine();
            sb.AppendLine("        def startTest(self, test):");
            sb.AppendLine("            super().startTest(test)");
            sb.AppendLine("            self.current_test = test");
            sb.AppendLine();
            sb.AppendLine("        def addSuccess(self, test):");
            sb.AppendLine("            super().addSuccess(test)");
            sb.AppendLine("            self.test_results.append({'test': test, 'status': 'passed', 'error': None})");
            sb.AppendLine();
            sb.AppendLine("        def addError(self, test, err):");
            sb.AppendLine("            super().addError(test, err)");
            sb.AppendLine("            self.test_results.append({'test': test, 'status': 'error', 'error': err})");
            sb.AppendLine();
            sb.AppendLine("        def addFailure(self, test, err):");
            sb.AppendLine("            super().addFailure(test, err)");
            sb.AppendLine("            self.test_results.append({'test': test, 'status': 'failed', 'error': err})");
            sb.AppendLine();
            sb.AppendLine("    # Run tests with custom result");
            sb.AppendLine("    loader = unittest.TestLoader()");
            sb.AppendLine("    suite = loader.loadTestsFromModule(sys.modules[__name__])");
            sb.AppendLine("    runner = unittest.TextTestRunner(resultclass=JsonTestResult, stream=sys.stderr, verbosity=0)");
            sb.AppendLine("    result = runner.run(suite)");
            sb.AppendLine();
            sb.AppendLine("    # Build details from captured results");
            sb.AppendLine("    details = []");
            sb.AppendLine("    for i, test_result in enumerate(result.test_results, 1):");
            sb.AppendLine("        test_name = 'test ' + str(i)");
            sb.AppendLine("        status = test_result['status']");
            sb.AppendLine("        error = test_result['error']");
            sb.AppendLine();
            sb.AppendLine("        if error:");
            sb.AppendLine("            import traceback");
            sb.AppendLine("            error_msg = ''.join(traceback.format_exception(*error))");
            sb.AppendLine("            lines = error_msg.split('\\n')");
            sb.AppendLine("            last_line = ''");
            sb.AppendLine("            for line in reversed(lines):");
            sb.AppendLine("                if line.strip():");
            sb.AppendLine("                    last_line = line.strip()");
            sb.AppendLine("                    break");
            sb.AppendLine("            details.append({'test': test_name, 'status': status, 'message': last_line[:200]})");
            sb.AppendLine("        else:");
            sb.AppendLine("            details.append({'test': test_name, 'status': status, 'message': ''})");
            sb.AppendLine();
            sb.AppendLine("    # Build results");
            sb.AppendLine("    results = {");
            sb.AppendLine("        'total': result.testsRun,");
            sb.AppendLine("        'passed': len([t for t in result.test_results if t['status'] == 'passed']),");
            sb.AppendLine("        'failed': len(result.failures),");
            sb.AppendLine("        'errors': len(result.errors),");
            sb.AppendLine("        'success': result.wasSuccessful(),");
            sb.AppendLine("        'details': details");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    # Output JSON marker and results");
            sb.AppendLine("    print('===MOCK_TEST_RESULTS===')");
            sb.AppendLine("    print(json.dumps(results))");

            return sb.ToString();
        }

        /// <summary>
        /// Removes common leading indentation from code
        /// </summary>
        private string DedentCode(string code)
        {
            var lines = code.Split('\n')
                .Select(line => line.TrimEnd())
                .SkipWhile(line => string.IsNullOrWhiteSpace(line))
                .ToList();

            if (!lines.Any())
                return string.Empty;

            // Find minimum indentation
            var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (!nonEmptyLines.Any())
                return string.Join("\n", lines);

            int minIndent = nonEmptyLines
                .Select(line => line.Length - line.TrimStart().Length)
                .Min();

            // Remove minimum indentation from all lines
            var dedented = lines.Select(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return string.Empty;
                return line.Length >= minIndent ? line.Substring(minIndent) : line;
            });

            return string.Join("\n", dedented);
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
                    SubmissionId = Guid.NewGuid(),
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
                    Details = new List<MockTestDetail>()
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

                var details = rawResult.Details?.Select(d => new MockTestDetail
                {
                    TestName = d.Test ?? "Unknown",
                    Status = d.Status ?? "error",
                    Message = d.Message ?? ""
                }).ToList() ?? new List<MockTestDetail>();

                return new MockTestResultDTO
                {
                    SubmissionId = Guid.NewGuid(),
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
                    ErrorMessage = (rawResult.Failed + rawResult.Errors) > 0 ? stderr : null,
                    Details = details
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON results: {Json}", jsonPart);
                return new MockTestResultDTO
                {
                    SubmissionId = Guid.NewGuid(),
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
                    Details = new List<MockTestDetail>()
                };
            }
        }
    }
}
