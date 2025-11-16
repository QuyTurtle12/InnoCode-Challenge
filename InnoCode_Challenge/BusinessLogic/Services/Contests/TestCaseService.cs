using System.Globalization;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using CsvHelper;
using CsvHelper.Configuration;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TestCaseDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class TestCaseService : ITestCaseService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfigService _configService;

        // Constructor
        public TestCaseService(IMapper mapper, IUOW unitOfWork, IHttpContextAccessor httpContextAccessor, IConfigService configService)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
            _configService = configService;
        }

        public async Task CreateTestCaseAsync(Guid roundId, CreateTestCaseDTO testCaseDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (testCaseDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case data cannot be null.");
                }

                // Validate round and get problem
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases can only be created for AutoEvaluation problems.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Map DTO to Entity
                TestCase testCase = _mapper.Map<TestCase>(testCaseDTO);
                testCase.TestCaseId = Guid.NewGuid();
                testCase.ProblemId = round.Problem.ProblemId;
                testCase.Type = TestCaseTypeEnum.TestCase.ToString();

                // Insert new test case
                await testCaseRepo.InsertAsync(testCase);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating test case: {ex.Message}");
            }
        }

        public async Task BulkUpdateTestCasesAsync(Guid roundId, IList<BulkUpdateTestCaseDTO> testCaseDTOs)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (testCaseDTOs == null || !testCaseDTOs.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case list cannot be null or empty.");
                }

                // Validate round and get problem with test cases
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .Include(r => r.Problem)
                        .ThenInclude(p => p!.TestCases)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases can only be updated for AutoEvaluation problems.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Get all test case IDs from DTOs
                List<Guid> testCaseIds = testCaseDTOs.Select(dto => dto.TestCaseId).ToList();

                // Check for duplicates in the request
                if (testCaseIds.Count != testCaseIds.Distinct().Count())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Duplicate test case IDs found in the request.");
                }

                // Fetch all existing test cases that match the IDs and belong to this problem
                List<TestCase> existingTestCases = await testCaseRepo.Entities
                    .Where(tc => testCaseIds.Contains(tc.TestCaseId)
                        && tc.ProblemId == round.Problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString()
                        && !tc.DeleteAt.HasValue)
                    .ToListAsync();

                // Validate all test cases exist
                if (existingTestCases.Count != testCaseIds.Count)
                {
                    List<Guid> foundIds = existingTestCases.Select(tc => tc.TestCaseId).ToList();
                    List<Guid> notFoundIds = testCaseIds.Except(foundIds).ToList();

                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"The following test case IDs were not found or do not belong to this round: {string.Join(", ", notFoundIds)}");
                }

                // Create a dictionary for quick lookup
                Dictionary<Guid, TestCase> testCaseDict = existingTestCases.ToDictionary(tc => tc.TestCaseId);

                // Update each test case
                foreach (BulkUpdateTestCaseDTO dto in testCaseDTOs)
                {
                    TestCase testCase = testCaseDict[dto.TestCaseId];

                    // Map properties from DTO to entity
                    testCase.Description = dto.Description;
                    testCase.Weight = dto.Weight;
                    testCase.TimeLimitMs = dto.TimeLimitMs;
                    testCase.MemoryKb = dto.MemoryKb;
                    testCase.Input = dto.Input;
                    testCase.ExpectedOutput = dto.ExpectedOutput;

                    // Update the test case
                    await testCaseRepo.UpdateAsync(testCase);
                }

                // Save all changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error bulk updating test cases: {ex.Message}");
            }
        }

        public async Task DeleteTestCaseAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate input
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test case ID cannot be empty.");
                }

                // Get TestCase Repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Find test case by id
                TestCase? testCase = await testCaseRepo.Entities
                    .Where(tc => tc.TestCaseId == id 
                        && !tc.DeleteAt.HasValue)
                    .Include(tc => tc.Problem)
                    .FirstOrDefaultAsync();

                // Check if test case is not exists and the related problem is already deleted
                if (testCase == null || testCase.Problem.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Test case not found.");
                }

                // Validate that the test case type is TestCase (not Manual)
                if (testCase.Type != TestCaseTypeEnum.TestCase.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Only test cases of type 'TestCase' can be deleted.");
                }

                // Delete the test case
                testCase.DeleteAt = DateTime.UtcNow;

                await testCaseRepo.UpdateAsync(testCase);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Roll back transaction on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting test case: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetTestCaseDTO>> GetTestCasesByRoundIdAsync(Guid roundId, int pageNumber, int pageSize)
        {
            try
            {
                // Validate pagination parameters
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Validate round exists
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.Problem == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Problem not found for this round.");
                }

                // Validate problem type is AutoEvaluation
                if (round.Problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Test cases are only available for AutoEvaluation problems.");
                }

                // Get user role from JWT token
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

                // If user is a student, validate round deadline and check if already finished
                if (userRole == RoleConstants.Student)
                {
                    await ValidateRoundDeadlineAsync(roundId, "access auto test round");

                    if (await IsAlreadyFinishRound(roundId))
                    {
                        throw new ErrorException(StatusCodes.Status403Forbidden,
                            ResponseCodeConstants.FORBIDDEN,
                            $"Cannot access. You have already finished this round.");
                    }
                }

                // Get test case repository
                IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                // Build query for test cases
                IQueryable<TestCase> query = testCaseRepo.Entities
                    .Where(tc => tc.ProblemId == round.Problem.ProblemId
                        && tc.Type == TestCaseTypeEnum.TestCase.ToString()
                        && !tc.DeleteAt.HasValue)
                    .OrderBy(tc => tc.TestCaseId);

                // Get paginated results
                PaginatedList<TestCase> paginatedTestCases = await testCaseRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map to DTOs
                IReadOnlyCollection<GetTestCaseDTO> testCaseDTOs = paginatedTestCases.Items
                    .Select(tc => _mapper.Map<GetTestCaseDTO>(tc))
                    .ToList();

                // Return paginated list with DTOs
                return new PaginatedList<GetTestCaseDTO>(
                    testCaseDTOs,
                    paginatedTestCases.TotalCount,
                    paginatedTestCases.PageNumber,
                    paginatedTestCases.PageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving test cases: {ex.Message}");
            }
        }

        public async Task<TestCaseImportResultDTO> ImportTestCasesFromCsvAsync(IFormFile csvFile, Guid roundId)
        {
            try
            {
                TestCaseImportResultDTO result = new TestCaseImportResultDTO { RoundId = roundId };

                // Validate file
                CsvHelpers.ValidateCsvFile(csvFile);

                // Read CSV content
                string csvContent;
                using (var reader = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8))
                {
                    csvContent = await reader.ReadToEndAsync();
                }

                // Parse CSV rows
                List<TestCaseCsvRowDTO> csvRows = ParseTestCaseCsvRows(csvContent);

                result.TotalRows = csvRows.Count;

                if (csvRows.Count == 0)
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "CSV file contains no valid test case rows."
                    );
                }

                try
                {
                    // Start transaction
                    _unitOfWork.BeginTransaction();

                    // Get repositories
                    IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                    IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
                    IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

                    // Validate round exists
                    Round? round = await roundRepo.Entities
                        .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

                    if (round == null)
                    {
                        throw new ErrorException(
                            StatusCodes.Status404NotFound,
                            ResponseCodeConstants.NOT_FOUND,
                            $"Round with ID {roundId} not found."
                        );
                    }

                    result.RoundName = round.Name;

                    // Get problem for this round
                    Problem? problem = await problemRepo.Entities
                        .FirstOrDefaultAsync(p => p.RoundId == roundId && !p.DeletedAt.HasValue);

                    if (problem == null)
                    {
                        throw new ErrorException(
                            StatusCodes.Status404NotFound,
                            ResponseCodeConstants.NOT_FOUND,
                            $"Problem not found for round {roundId}."
                        );
                    }

                    // Verify this is an auto-evaluation problem
                    if (problem.Type != ProblemTypeEnum.AutoEvaluation.ToString())
                    {
                        throw new ErrorException(
                            StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Test cases can only be imported for auto-evaluation problem types."
                        );
                    }

                    result.ProblemId = problem.ProblemId;

                    // Remove existing test cases =====
                    List<TestCase> existingTestCases = await testCaseRepo.Entities
                        .Where(tc => tc.ProblemId == problem.ProblemId
                            && tc.Type == TestCaseTypeEnum.TestCase.ToString()
                            && !tc.DeleteAt.HasValue)
                        .ToListAsync();

                    if (existingTestCases.Any())
                    {
                        foreach (TestCase existingTestCase in existingTestCases)
                        {
                            existingTestCase.DeleteAt = DateTime.UtcNow;
                            await testCaseRepo.UpdateAsync(existingTestCase);
                        }

                        await _unitOfWork.SaveAsync();
                    }

                    // Process all test cases
                    int rowNumber = 1;
                    foreach (TestCaseCsvRowDTO row in csvRows)
                    {
                        rowNumber++;
                        try
                        {
                            // Validate row
                            string? validationError = ValidateTestCaseRow(row, rowNumber);
                            if (!string.IsNullOrEmpty(validationError))
                            {
                                result.Errors.Add(validationError);
                                result.ErrorCount++;
                                continue;
                            }

                            // Parse numeric values
                            if (!double.TryParse(row.Weight, out double weight))
                            {
                                result.Errors.Add($"Row {rowNumber}: Invalid weight value '{row.Weight}'.");
                                result.ErrorCount++;
                                continue;
                            }

                            int? timeLimitMs = null;
                            if (!string.IsNullOrWhiteSpace(row.TimeLimitMs))
                            {
                                if (int.TryParse(row.TimeLimitMs, out int parsedTime))
                                {
                                    timeLimitMs = parsedTime;
                                }
                                else
                                {
                                    result.Errors.Add($"Row {rowNumber}: Invalid time limit value '{row.TimeLimitMs}'.");
                                    result.ErrorCount++;
                                    continue;
                                }
                            }

                            int? memoryKb = null;
                            if (!string.IsNullOrWhiteSpace(row.MemoryKb))
                            {
                                if (int.TryParse(row.MemoryKb, out int parsedMemory))
                                {
                                    memoryKb = parsedMemory;
                                }
                                else
                                {
                                    result.Errors.Add($"Row {rowNumber}: Invalid memory limit value '{row.MemoryKb}'.");
                                    result.ErrorCount++;
                                    continue;
                                }
                            }

                            // Create test case
                            TestCase testCase = new TestCase
                            {
                                TestCaseId = Guid.NewGuid(),
                                ProblemId = problem.ProblemId,
                                Description = row.Description?.Trim(),
                                Type = TestCaseTypeEnum.TestCase.ToString(),
                                Weight = weight,
                                TimeLimitMs = timeLimitMs,
                                MemoryKb = memoryKb,
                                Input = row.Input?.Trim(),
                                ExpectedOutput = row.ExpectedOutput.Trim()
                            };

                            await testCaseRepo.InsertAsync(testCase);
                            await _unitOfWork.SaveAsync();

                            result.ImportedTestCaseIds.Add(testCase.TestCaseId);
                            result.SuccessCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Row {rowNumber}: {ex.Message}");
                            result.ErrorCount++;
                        }
                    }

                    // Commit transaction
                    _unitOfWork.CommitTransaction();
                    return result;
                }
                catch (Exception)
                {
                    // Roll back transaction on error
                    _unitOfWork.RollBack();
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error importing test cases: {ex.Message}"
                );
            }
        }

        private static List<TestCaseCsvRowDTO> ParseTestCaseCsvRows(string csvContent)
        {
            // Detect delimiter
            char delimiter = CsvHelpers.DetectDelimiter(csvContent);

            // Parse CSV using CsvHelper
            using StringReader reader = new StringReader(csvContent);
            using CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
                Delimiter = delimiter.ToString()
            });

            var result = new List<TestCaseCsvRowDTO>();

            // Read records until first empty row
            foreach (var row in csv.GetRecords<TestCaseCsvRowDTO>())
            {
                // Stop at first completely empty row
                if (IsEmptyTestCaseRow(row))
                    break;

                result.Add(row);
            }

            return result;
        }

        private static bool IsEmptyTestCaseRow(TestCaseCsvRowDTO row)
        {
            return string.IsNullOrWhiteSpace(row.Description) &&
                   string.IsNullOrWhiteSpace(row.Weight) &&
                   string.IsNullOrWhiteSpace(row.TimeLimitMs) &&
                   string.IsNullOrWhiteSpace(row.MemoryKb) &&
                   string.IsNullOrWhiteSpace(row.Input) &&
                   string.IsNullOrWhiteSpace(row.ExpectedOutput);
        }

        private static string? ValidateTestCaseRow(TestCaseCsvRowDTO row, int rowNumber)
        {
            // Validate weight
            if (string.IsNullOrWhiteSpace(row.Weight))
                return $"Row {rowNumber}: Weight is required.";

            if (!double.TryParse(row.Weight, out double weight) || weight <= 0)
                return $"Row {rowNumber}: Weight must be a positive number.";

            // Validate expected output
            if (string.IsNullOrWhiteSpace(row.ExpectedOutput))
                return $"Row {rowNumber}: Expected output is required.";

            // Validate time limit if provided
            if (!string.IsNullOrWhiteSpace(row.TimeLimitMs))
            {
                if (!int.TryParse(row.TimeLimitMs, out int timeLimit) || timeLimit <= 0)
                    return $"Row {rowNumber}: Time limit must be a positive integer.";
            }

            // Validate memory limit if provided
            if (!string.IsNullOrWhiteSpace(row.MemoryKb))
            {
                if (!int.TryParse(row.MemoryKb, out int memory) || memory <= 0)
                    return $"Row {rowNumber}: Memory limit must be a positive integer.";
            }

            return null;
        }

        private async Task<bool> IsAlreadyFinishRound(Guid roundId)
        {
            // Get user ID from JWT token
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Null User Id");

            // Get student ID from user ID
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Guid studentId = studentRepo.Entities.Where(s => s.UserId.ToString() == userId)
                .Select(s => s.StudentId)
                .FirstOrDefault();

            // Check if student has already finished this round
            bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

            if (IsAlreadyFinishedRound)
            {
                return true;
            }

            return false;
        }

        private async Task ValidateRoundDeadlineAsync(Guid roundId, string operationName)
        {
            // Get the round details
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                .FirstOrDefaultAsync();

            // Validate round existence
            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Round with ID {roundId} not found");
            }

            // Check if current time is within the round's start and end time
            DateTime now = DateTime.UtcNow;

            if (now < round.Start)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot {operationName}. Round {round.Name} has not started yet. Start time: {round.Start:yyyy-MM-dd HH:mm:ss} UTC");
            }

            if (now > round.End)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot {operationName}. Round {round.Name} has already ended. End time: {round.End:yyyy-MM-dd HH:mm:ss} UTC");
            }
        }
    }
}
