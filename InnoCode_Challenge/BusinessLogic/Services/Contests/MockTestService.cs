using BusinessLogic.IServices.Contests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.MockTestDTOs;
using System.Text.Json;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;

namespace BusinessLogic.Services.Contests
{
    public class MockTestService : IMockTestService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MockTestService> _logger;
        private readonly IJudge0Service _judge0Service;

        private const int PYTHON_LANGUAGE_ID = 71;
        private const int DEFAULT_EXECUTION_TIME_LIMIT_SECONDS = 20;
        private const int DEFAULT_EXECUTION_MEMORY_LIMIT = 1024;

        private const string MOCK_TEST_SUCCESS_VALUE = "success";
        private const string MOCK_TEST_ERROR_VALUE = "error";
        private const string MOCK_TEST_UNKNOW_VALUE = "unknown";

        public MockTestService(
            HttpClient httpClient,
            ILogger<MockTestService> logger,
            IJudge0Service judge0Service)
        {
            _httpClient = httpClient;
            _logger = logger;
            _judge0Service = judge0Service;
        }

        public async Task<JudgeSubmissionResultDTO> ExecuteMockTestAsync(
            string userCode,
            string mockTestUrl,
            int timeLimitSec = DEFAULT_EXECUTION_TIME_LIMIT_SECONDS,
            int memoryLimitMb = DEFAULT_EXECUTION_MEMORY_LIMIT)
        {
            // Download mock test code from the provided URL
            string mockTestCode = await DownloadMockTestAsync(mockTestUrl);

            // Build the complete test script
            string combinedScript = BuildTestScript(userCode, mockTestCode);

            _logger?.LogDebug("Executing test script (length: {Length})", combinedScript.Length);

            // Log the script for debugging
            _logger?.LogTrace("Test script:\n{Script}", combinedScript);

            // Execute the combined script using Piston service
            JudgeSubmissionResultDTO executionResult = await ExecuteViaJudge0Async(
                combinedScript,
                timeLimitSec,
                memoryLimitMb);

            return executionResult;
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
            sb.AppendLine("import io");
            sb.AppendLine("import traceback");
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

            // Add custom assertion interceptor
            sb.AppendLine("# ===== ASSERTION INTERCEPTOR =====");
            sb.AppendLine("class AssertionCapture:");
            sb.AppendLine("    def __init__(self):");
            sb.AppendLine("        self.expected = None");
            sb.AppendLine("        self.actual = None");
            sb.AppendLine();
            sb.AppendLine("    def capture_assertEqual(self, expected, actual, msg=None):");
            sb.AppendLine("        self.expected = expected");
            sb.AppendLine("        self.actual = actual");
            sb.AppendLine("        if expected != actual:");
            sb.AppendLine("            raise AssertionError(f'{msg or \"\"} Expected: {expected!r}, Actual: {actual!r}')");
            sb.AppendLine();
            sb.AppendLine("    def capture_assertTrue(self, value, msg=None):");
            sb.AppendLine("        self.expected = True");
            sb.AppendLine("        self.actual = value");
            sb.AppendLine("        if not value:");
            sb.AppendLine("            raise AssertionError(msg or f'Expected True, got {value!r}')");
            sb.AppendLine();
            sb.AppendLine("    def capture_assertFalse(self, value, msg=None):");
            sb.AppendLine("        self.expected = False");
            sb.AppendLine("        self.actual = value");
            sb.AppendLine("        if value:");
            sb.AppendLine("            raise AssertionError(msg or f'Expected False, got {value!r}')");
            sb.AppendLine();

            // Monkey-patch unittest.TestCase to use interceptor
            sb.AppendLine("# Monkey-patch unittest.TestCase");
            sb.AppendLine("_original_setUp = unittest.TestCase.setUp");
            sb.AppendLine("def _patched_setUp(self):");
            sb.AppendLine("    self._capture = AssertionCapture()");
            sb.AppendLine("    self._original_assertEqual = self.assertEqual");
            sb.AppendLine("    self._original_assertTrue = self.assertTrue");
            sb.AppendLine("    self._original_assertFalse = self.assertFalse");
            sb.AppendLine("    self.assertEqual = lambda *args, **kwargs: self._capture.capture_assertEqual(*args, **kwargs)");
            sb.AppendLine("    self.assertTrue = lambda *args, **kwargs: self._capture.capture_assertTrue(*args, **kwargs)");
            sb.AppendLine("    self.assertFalse = lambda *args, **kwargs: self._capture.capture_assertFalse(*args, **kwargs)");
            sb.AppendLine("    if hasattr(_original_setUp, '__func__'):");
            sb.AppendLine("        _original_setUp.__func__(self)");
            sb.AppendLine("unittest.TestCase.setUp = _patched_setUp");
            sb.AppendLine();

            // Add test runner code
            sb.AppendLine("if __name__ == '__main__':");
            sb.AppendLine("    class DetailedTestResult(unittest.TextTestResult):");
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
            sb.AppendLine("            # ✅ Capture expected/actual from our interceptor");
            sb.AppendLine("            expected = getattr(test._capture, 'expected', None)");
            sb.AppendLine("            actual = getattr(test._capture, 'actual', None)");
            sb.AppendLine("            self.test_results.append({");
            sb.AppendLine("                'test': test,");
            sb.AppendLine("                'status': 'success',");
            sb.AppendLine("                'expected': repr(expected) if expected is not None else '',");
            sb.AppendLine("                'actual': repr(actual) if actual is not None else '',");
            sb.AppendLine("                'stderr': None,");
            sb.AppendLine("                'error': None");
            sb.AppendLine("            })");
            sb.AppendLine();
            sb.AppendLine("        def addError(self, test, err):");
            sb.AppendLine("            super().addError(test, err)");
            sb.AppendLine("            stderr = ''.join(traceback.format_exception(*err))");
            sb.AppendLine("            self.test_results.append({");
            sb.AppendLine("                'test': test,");
            sb.AppendLine("                'status': 'error',");
            sb.AppendLine("                'expected': '',");
            sb.AppendLine("                'actual': '',");
            sb.AppendLine("                'stderr': stderr[:500],");
            sb.AppendLine("                'error': err");
            sb.AppendLine("            })");
            sb.AppendLine();
            sb.AppendLine("        def addFailure(self, test, err):");
            sb.AppendLine("            super().addFailure(test, err)");
            sb.AppendLine("            # ✅ Capture expected/actual from our interceptor");
            sb.AppendLine("            expected = getattr(test._capture, 'expected', None)");
            sb.AppendLine("            actual = getattr(test._capture, 'actual', None)");
            sb.AppendLine("            stderr = ''.join(traceback.format_exception(*err))");
            sb.AppendLine("            last_line = stderr.split('\\n')[-2] if '\\n' in stderr else stderr");
            sb.AppendLine("            self.test_results.append({");
            sb.AppendLine("                'test': test,");
            sb.AppendLine("                'status': 'failed',");
            sb.AppendLine("                'expected': repr(expected) if expected is not None else '',");
            sb.AppendLine("                'actual': repr(actual) if actual is not None else '',");
            sb.AppendLine("                'stderr': last_line[:200],");
            sb.AppendLine("                'error': err");
            sb.AppendLine("            })");
            sb.AppendLine();

            // Test execution
            sb.AppendLine("    loader = unittest.TestLoader()");
            sb.AppendLine("    suite = loader.loadTestsFromModule(sys.modules[__name__])");
            sb.AppendLine("    runner = unittest.TextTestRunner(resultclass=DetailedTestResult, stream=sys.stderr, verbosity=0)");
            sb.AppendLine("    result = runner.run(suite)");
            sb.AppendLine();

            // Build detailed results
            sb.AppendLine("    details = []");
            sb.AppendLine("    for i, test_result in enumerate(result.test_results, 1):");
            sb.AppendLine("        test_name = f'test {i}'");
            sb.AppendLine("        status = test_result['status']");
            sb.AppendLine("        expected = test_result.get('expected', '')");
            sb.AppendLine("        actual = test_result.get('actual', '')");
            sb.AppendLine("        stderr = test_result.get('stderr', '')");
            sb.AppendLine();
            sb.AppendLine("        details.append({");
            sb.AppendLine("            'test': test_name,");
            sb.AppendLine("            'status': status,");
            sb.AppendLine("            'expected': expected if expected else None,");
            sb.AppendLine("            'actual': actual if actual else None,");
            sb.AppendLine("            'stderr': stderr if stderr else None");
            sb.AppendLine("        })");
            sb.AppendLine();

            // Output results
            sb.AppendLine("    results = {");
            sb.AppendLine("        'total': result.testsRun,");
            sb.AppendLine("        'passed': len([t for t in result.test_results if t['status'] == 'success']),");
            sb.AppendLine("        'failed': len(result.failures) + len(result.errors),");
            sb.AppendLine("        'success': result.wasSuccessful(),");
            sb.AppendLine("        'details': details");
            sb.AppendLine("    }");
            sb.AppendLine();
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

        private async Task<JudgeSubmissionResultDTO> ExecuteViaJudge0Async(
            string script,
            int timeLimitSec,
            int memoryLimitMb)
        {
            try
            {
                _logger?.LogInformation("Executing mock test via Judge0");

                JudgeSubmissionRequestDTO request = new JudgeSubmissionRequestDTO
                {
                    LanguageId = PYTHON_LANGUAGE_ID,
                    Code = script,
                    Problem = new JudgeProblemDTO
                    {
                        Id = "mock-test",
                        Title = "Mock Test Execution"
                    },
                    TestCases = new List<JudgeTestCaseDTO>
                    {
                        new JudgeTestCaseDTO
                        {
                            Id = "mock-test-case",
                            Stdin = "",
                            ExpectedOutput = ""
                        }
                    },
                    TimeLimitSec = timeLimitSec,
                    MemoryLimitKb = memoryLimitMb * 1024
                };

                // Execute and return Judge0 result directly
                JudgeSubmissionResultDTO result = await _judge0Service.AutoEvaluateSubmissionAsync(request);

                // Parse the JSON from stdout and update the result
                return ParseJudge0Result(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Judge0 execution failed");
                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Judge0 execution failed: {ex.Message}");
            }
        }

        private JudgeSubmissionResultDTO ParseJudge0Result(JudgeSubmissionResultDTO judge0Result)
        {
            try
            {
                var firstCase = judge0Result.Cases.FirstOrDefault();
                if (firstCase == null)
                {
                    throw new InvalidOperationException("No test case results returned from Judge0");
                }

                // ✅ Use Stdout (added in previous step)
                string output = firstCase.Stdout ?? string.Empty;
                string stderr = firstCase.Stderr ?? string.Empty;

                // Look for JSON marker
                int markerIndex = output.IndexOf("===MOCK_TEST_RESULTS===");

                if (markerIndex < 0)
                {
                    _logger?.LogWarning("Failed to find mock test results marker");

                    // Return error case with Judge0 structure
                    judge0Result.Summary.Total = 1;
                    judge0Result.Summary.Passed = 0;
                    judge0Result.Summary.Failed = 1;
                    judge0Result.Summary.rawScore = 0;
                    judge0Result.Summary.penaltyScore = 0;

                    firstCase.Id = "mock-test-error";
                    firstCase.Status = MOCK_TEST_ERROR_VALUE;
                    firstCase.Expected = "";
                    firstCase.Actual = "Execution failed";
                    firstCase.Stderr = string.IsNullOrEmpty(stderr) ? "Failed to find test results" : stderr;

                    return judge0Result;
                }

                // Extract JSON
                string jsonPart = output.Substring(markerIndex + "===MOCK_TEST_RESULTS===".Length).Trim();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Deserialize JSON
                var rawResult = JsonSerializer.Deserialize<MockTestRawResultJson>(jsonPart, options);

                if (rawResult == null)
                {
                    throw new JsonException("Failed to parse mock test results");
                }

                // Update summary
                judge0Result.Summary.Total = rawResult.Total;
                judge0Result.Summary.Passed = rawResult.Passed;
                judge0Result.Summary.Failed = rawResult.Failed;

                // Replace single case with multiple mock test cases
                judge0Result.Cases.Clear();

                foreach (var detail in rawResult.Details ?? new List<MockTestDetailJson>())
                {
                    judge0Result.Cases.Add(new JudgeCaseResultDTO
                    {
                        Id = detail.Test ?? MOCK_TEST_UNKNOW_VALUE,
                        Status = detail.Status ?? MOCK_TEST_ERROR_VALUE,
                        Judge0StatusId = detail.Status == MOCK_TEST_SUCCESS_VALUE ? (int) Judge0StatusEnum.Accepted : (int) Judge0StatusEnum.Error,
                        Judge0Status = detail.Status == MOCK_TEST_SUCCESS_VALUE ? Judge0StatusEnum.Accepted.ToString() : Judge0StatusEnum.Error.ToString(),
                        Expected = detail.Expected ?? "",
                        Actual = detail.Actual ?? "",
                        Stdout = detail.Actual,
                        Stderr = detail.Stderr,
                        CompileOutput = null,
                        Time = firstCase.Time,
                        MemoryKb = firstCase.MemoryKb,
                        Token = firstCase.Token
                    });
                }

                return judge0Result;
            }
            catch (JsonException ex)
            {
                _logger?.LogError(ex, "Failed to parse mock test JSON");

                // Return error case
                judge0Result.Summary.Total = 1;
                judge0Result.Summary.Passed = 0;
                judge0Result.Summary.Failed = 1;
                judge0Result.Summary.rawScore = 0;
                judge0Result.Summary.penaltyScore = 0;

                judge0Result.Cases.Clear();
                judge0Result.Cases.Add(new JudgeCaseResultDTO
                {
                    Id = "parse-error",
                    Status = MOCK_TEST_ERROR_VALUE,
                    Judge0StatusId = 4,
                    Judge0Status = Judge0StatusEnum.Error.ToString(),
                    Expected = "",
                    Actual = "",
                    Stderr = $"JSON parse error: {ex.Message}",
                    Time = null,
                    MemoryKb = null,
                    Token = string.Empty
                });

                return judge0Result;
            }
        }
    }
}
