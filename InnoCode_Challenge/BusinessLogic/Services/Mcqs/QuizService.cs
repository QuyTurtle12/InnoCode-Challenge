using System.Globalization;
using System.Security.Claims;
using System.Text;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Mcqs;
using CsvHelper;
using CsvHelper.Configuration;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.BankDTOs;
using Repository.DTOs.QuizDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mcqs
{
    public class QuizService : IQuizService
    {
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILeaderboardEntryService _leaderboardService;
        private readonly IMcqTestService _mcqTestService;
        private readonly IConfigService _configService;

        public QuizService(IUOW unitOfWork, IHttpContextAccessor httpContextAccessor, ILeaderboardEntryService leaderboardService, IMcqTestService mcqTestService, IConfigService configService)
        {
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
            _leaderboardService = leaderboardService;
            _mcqTestService = mcqTestService;
            _configService = configService;
        }

        public async Task<QuizResultDTO> ProcessQuizSubmissionAsync(Guid roundId, CreateQuizSubmissionDTO quizSubmissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Check round deadline before allowing quiz submission
                await ValidateRoundDeadlineAsync(roundId, "submit quiz");

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
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        $"Cannot submit. You have already finished this round.");
                }

                // Verify the MCQ test exists
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();
                McqTest? mcqTest = await mcqTestRepo
                    .Entities
                    .Where(t => t.RoundId == roundId)
                    .FirstOrDefaultAsync();

                if (mcqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test in Round ID {roundId} not found");
                }

                // Get all test questions with weights for scoring calculation
                IGenericRepository<McqTestQuestion> testQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();
                Dictionary<Guid, double> questionWeights = await testQuestionRepo.Entities
                    .Where(tq => tq.TestId == mcqTest.TestId)
                    .ToDictionaryAsync(tq => tq.QuestionId, tq => tq.Weight);

                // Create MCQ attempt record
                IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();
                McqAttempt attempt = new McqAttempt
                {
                    AttemptId = Guid.NewGuid(),
                    TestId = mcqTest.TestId,
                    RoundId = mcqTest.RoundId,
                    StudentId = studentId,
                    Start = DateTime.UtcNow,
                    End = DateTime.UtcNow,
                    Score = 0
                };

                // Insert the attempt & save to get the AttemptId
                await attemptRepo.InsertAsync(attempt);
                await _unitOfWork.SaveAsync();

                // Process each answer
                IGenericRepository<McqOption> optionRepo = _unitOfWork.GetRepository<McqOption>();
                IGenericRepository<McqAttemptItem> attemptItemRepo = _unitOfWork.GetRepository<McqAttemptItem>();

                int totalQuestions = quizSubmissionDTO.Answers.Count;
                int correctAnswers = 0;
                double totalPossibleWeight = 0;
                double earnedWeight = 0;
                List<QuizAnswerResultDTO> answerResults = new List<QuizAnswerResultDTO>();

                // Evaluate each answer
                foreach (QuizAnswerDTO answer in quizSubmissionDTO.Answers)
                {
                    // Get the selected option
                    McqOption? option = await optionRepo
                        .Entities
                        .Where(o => o.OptionId == answer.SelectedOptionId)
                        .Include(o => o.Question)
                        .FirstOrDefaultAsync();

                    // Skip invalid options
                    if (option == null)
                    {
                        continue;
                    }

                    // Get the weight for this question
                    double questionWeight = questionWeights.TryGetValue(option.QuestionId, out double weight) ? weight : 1.0;
                    totalPossibleWeight += questionWeight;

                    // Check if the answer is correct
                    bool isCorrect = option.IsCorrect;
                    if (isCorrect)
                    {
                        correctAnswers++;
                        earnedWeight += questionWeight;
                    }

                    // Create attempt item
                    McqAttemptItem attemptItem = new McqAttemptItem
                    {
                        ItemId = Guid.NewGuid(),
                        AttemptId = attempt.AttemptId,
                        TestId = mcqTest.TestId,
                        QuestionId = option.QuestionId,
                        SelectedOptionId = answer.SelectedOptionId,
                        Correct = isCorrect
                    };

                    await attemptItemRepo.InsertAsync(attemptItem);

                    // Add to result list
                    QuizAnswerResultDTO answerResult = new QuizAnswerResultDTO
                    {
                        QuestionId = option.QuestionId,
                        QuestionText = option.Question?.Text ?? string.Empty,
                        SelectedOptionId = answer.SelectedOptionId,
                        SelectedOptionText = option.Text,
                        IsCorrect = isCorrect
                    };

                    answerResults.Add(answerResult);
                }

                // Save all attempt items
                await _unitOfWork.SaveAsync();

                // Calculate weighted score
                double score = totalPossibleWeight > 0 ? earnedWeight : 0;
                score = Math.Round(score, 2);

                // Update the attempt with the final score
                attempt.Score = score;
                await attemptRepo.UpdateAsync(attempt);
                await _unitOfWork.SaveAsync();

                // Get the team ID from the student
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                Guid? teamId = await teamMemberRepo.Entities
                    .Where(tm => tm.StudentId == studentId)
                    .Select(tm => tm.TeamId)
                    .FirstOrDefaultAsync();

                // Get contest ID from the round
                Guid contestId = await _unitOfWork.GetRepository<Round>().Entities
                    .Where(r => r.RoundId == mcqTest.RoundId)
                    .Select(r => r.ContestId)
                    .FirstOrDefaultAsync();

                // Update team score in leaderboard if team exists
                if (teamId.HasValue && contestId != Guid.Empty)
                {
                    try
                    {
                        await _leaderboardService.AddScoreToTeamAsync(contestId, teamId.Value, score);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail the quiz submission if leaderboard update fails
                        Console.WriteLine($"Failed to update leaderboard: {ex.Message}");
                    }
                }

                // Get test name
                string testName = mcqTest.Name ?? "Unknown Test";

                // Get user full name
                string? userFullName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
                    ?? "Unknown User";

                // Create result DTO
                QuizResultDTO result = new QuizResultDTO
                {
                    AttemptId = attempt.AttemptId,
                    TestId = mcqTest.TestId,
                    TestName = testName,
                    StudentId = studentId,
                    StudentName = userFullName,
                    SubmittedAt = DateTime.UtcNow,
                    TotalQuestions = totalQuestions,
                    CorrectAnswers = correctAnswers,
                    Score = score,
                    AnswerResults = answerResults
                };

                // Mark as finished for the round
                await _configService.MarkFinishedSubmissionAsync(roundId, studentId);

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return result;
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
                    $"Error processing quiz submission: {ex.Message}");
            }
        }

        public async Task<QuizResultDTO> GetQuizAttemptResultAsync(Guid attemptId)
        {
            try
            {
                // Get the MCQ attempt
                IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();
                McqAttempt? attempt = await attemptRepo
                    .Entities
                    .Where(a => a.AttemptId == attemptId)
                    .Include(a => a.Student)
                        .ThenInclude(a => a.User)
                    .FirstOrDefaultAsync();

                // Validate attempt existence
                if (attempt == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Quiz attempt with ID {attemptId} not found");
                }

                // Get all attempt items
                IGenericRepository<McqAttemptItem> attemptItemRepo = _unitOfWork.GetRepository<McqAttemptItem>();
                IList<McqAttemptItem> attemptItems = await attemptItemRepo.Entities
                    .Where(ai => ai.AttemptId == attemptId)
                    .Include(ai => ai.Question)
                    .Include(ai => ai.SelectedOption)
                    .ToListAsync();

                // Get the test details
                IGenericRepository<McqTest> testRepo = _unitOfWork.GetRepository<McqTest>();
                McqTest? test = await testRepo.GetByIdAsync(attempt.TestId);

                if (test == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test with ID {attempt.TestId} not found");
                }

                // Calculate results
                int totalQuestions = attemptItems.Count;
                int correctAnswers = attemptItems.Count(ai => ai.Correct);

                // Create answer results
                List<QuizAnswerResultDTO> answerResults = attemptItems.Select(ai => new QuizAnswerResultDTO
                {
                    QuestionId = ai.QuestionId,
                    SelectedOptionId = ai.SelectedOptionId ?? Guid.Empty,
                    IsCorrect = ai.Correct,
                    QuestionText = ai.Question.Text,
                    SelectedOptionText = ai.SelectedOption?.Text ?? "No answer"
                }).ToList();

                // Create result DTO
                QuizResultDTO result = new QuizResultDTO
                {
                    AttemptId = attemptId,
                    TestId = attempt.TestId,
                    StudentId = attempt.StudentId,
                    StudentName = attempt.Student?.User?.Fullname ?? "Unknown Student",
                    SubmittedAt = attempt.End ?? attempt.Start,
                    TotalQuestions = totalQuestions,
                    CorrectAnswers = correctAnswers,
                    Score = attempt.Score ?? 0,
                    AnswerResults = answerResults,
                    TestName = test.Name ?? "Unknown Test"
                };

                return result;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving quiz attempt result: {ex.Message}");
            }
        }

        public async Task<PaginatedList<QuizAttemptSummaryDTO>> GetStudentQuizAttemptsAsync(
             int pageNumber, int pageSize, Guid? studentId, Guid? testId, Guid roundId, bool IsForCurrentLoggedInStudent)
        {
            try
            {
                IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();

                // Start with base query
                IQueryable<McqAttempt> query = attemptRepo.Entities
                    .Where(a => a.RoundId == roundId)
                    .Include(a => a.Student)
                        .ThenInclude(s => s.User)
                    .Include(a => a.Test);

                // If filtering for current logged-in student
                if (IsForCurrentLoggedInStudent)
                {
                    // Get current user's student ID
                    string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!string.IsNullOrEmpty(userId))
                    {
                        IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                        studentId = studentRepo.Entities
                            .Where(s => s.UserId.ToString() == userId)
                            .Select(s => s.StudentId)
                            .FirstOrDefault();
                    }
                }

                // Apply filters if provided
                if (studentId.HasValue)
                {
                    query = query.Where(a => a.StudentId == studentId.Value);
                }

                if (testId.HasValue)
                {
                    query = query.Where(a => a.TestId == testId.Value);
                }

                // Order by most recent first
                query = query.OrderByDescending(a => a.End ?? a.Start);

                // Get paginated data
                PaginatedList<McqAttempt> resultQuery = await attemptRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Get attempt IDs for loading related data
                List<Guid> attemptIds = resultQuery.Items.Select(a => a.AttemptId).ToList();

                // Load all attempt items for these attempts in one query
                IGenericRepository<McqAttemptItem> attemptItemRepo = _unitOfWork.GetRepository<McqAttemptItem>();
                List<McqAttemptItem> allAttemptItems = await attemptItemRepo.Entities
                    .Where(ai => attemptIds.Contains(ai.AttemptId))
                    .Include(ai => ai.Question)
                    .Include(ai => ai.SelectedOption)
                    .ToListAsync();

                // Group attempt items by attempt ID for efficient lookup
                Dictionary<Guid, List<McqAttemptItem>> attemptItemsGrouped = allAttemptItems
                    .GroupBy(ai => ai.AttemptId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Map to DTOs
                IReadOnlyCollection<QuizAttemptSummaryDTO> result = resultQuery.Items.Select(item =>
                {
                    // Get attempt items for this attempt
                    List<McqAttemptItem> attemptItems = attemptItemsGrouped.ContainsKey(item.AttemptId)
                        ? attemptItemsGrouped[item.AttemptId]
                        : new List<McqAttemptItem>();

                    // Map attempt items to answer results
                    List<QuizAnswerResultDTO> answerResults = attemptItems.Select(ai => new QuizAnswerResultDTO
                    {
                        QuestionId = ai.QuestionId,
                        SelectedOptionId = ai.SelectedOptionId ?? Guid.Empty,
                        IsCorrect = ai.Correct,
                        QuestionText = ai.Question?.Text ?? "Unknown Question",
                        SelectedOptionText = ai.SelectedOption?.Text ?? "No answer"
                    }).ToList();

                    return new QuizAttemptSummaryDTO
                    {
                        AttemptId = item.AttemptId,
                        TestId = item.TestId,
                        TestName = item.Test?.Name ?? "Unknown Test",
                        StudentId = item.StudentId,
                        StudentName = item.Student?.User?.Fullname ?? "Unknown Student",
                        StartTime = item.Start,
                        EndTime = item.End,
                        Score = item.Score ?? 0,
                        AnswerResults = answerResults
                    };
                }).ToList();

                // Create new paginated list with mapped DTOs
                return new PaginatedList<QuizAttemptSummaryDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving quiz attempts: {ex.Message}");
            }
        }

        public async Task<GetQuizDTO> GetQuizByRoundIdAsync(int pageNumber, int pageSize, Guid roundId)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Get user role from JWT token
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

                // If user is a student, validate round deadline and check if already finished
                if (userRole == RoleConstants.Student)
                {
                    await ValidateRoundDeadlineAsync(roundId, "access quiz");

                    if (await IsAlreadyFinishRound(roundId))
                    {
                        throw new ErrorException(StatusCodes.Status403Forbidden,
                            ResponseCodeConstants.FORBIDDEN,
                            $"Cannot access. You have already finished this round.");
                    }
                }

                // Create a deterministic seed based on student ID + round ID
                int shuffleSeed = 0;

                // Get the logged-in student's user ID
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (userId != null)
                {
                    // Combine userId and roundId to create a unique seed
                    shuffleSeed = userId.GetHashCode() ^ roundId.GetHashCode();
                }

                // Create Random with the seed
                Random random = new Random(shuffleSeed);

                // Get repository for Round entities
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Get the round with its MCQ test
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId)
                    .Include(r => r.McqTest)
                        .ThenInclude(t => t!.McqTestQuestions)
                            .ThenInclude(tq => tq.Question)
                                .ThenInclude(q => q.McqOptions)
                    .FirstOrDefaultAsync();

                // Validate round existence
                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Round with ID {roundId} not found");
                }

                // Validate MCQ test existence
                if (round.McqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test not found for round {roundId}");
                }

                // Get all questions and shuffle them consistently
                List<McqTestQuestion> allQuestions = round.McqTest.McqTestQuestions
                    .ToList()
                    .OrderBy(x => random.Next())
                    .ToList();

                // Get total count before pagination
                int totalQuestions = allQuestions.Count;

                // Apply pagination to shuffled questions
                List<McqTestQuestion> paginatedQuestions = allQuestions
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Map to DTOs
                List<QuestionDTO> questionDTOs = paginatedQuestions
                    .Select((q, index) => new QuestionDTO
                    {
                        QuestionId = q.QuestionId,
                        Weight = q.Weight,
                        OrderIndex = ((pageNumber - 1) * pageSize) + index + 1, // Global order index
                        Text = q.Question?.Text ?? string.Empty,
                        Options = q.Question?.McqOptions
                            .ToList()
                            .OrderBy(x => random.Next()) // Shuffle options
                            .Select(o => new OptionDTO
                            {
                                OptionId = o.OptionId,
                                Text = o.Text,
                                IsCorrect = o.IsCorrect
                            })
                            .ToList() ?? new List<OptionDTO>()
                    })
                    .ToList();

                // Create the quiz DTO
                GetQuizDTO quizDTO = new GetQuizDTO
                {
                    RoundId = round.RoundId,
                    RoundName = round.Name,
                    RoundStatus = round.Status ?? RoundStatusEnum.Closed.ToString(),
                    McqTest = new McqTestDTO
                    {
                        TestId = round.McqTest.TestId,
                        Questions = questionDTOs,
                        TotalQuestions = totalQuestions,
                        CurrentPage = pageNumber,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling(totalQuestions / (double)pageSize)
                    }
                };

                return quizDTO;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving quiz: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetBankWithQuestionsDTO>> GetPaginatedBanksAsync(
            int pageNumber, int pageSize, Guid? bankId, string? nameSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Get repository for Bank entities
                IGenericRepository<Bank> bankRepo = _unitOfWork.GetRepository<Bank>();

                // Build query
                IQueryable<Bank> query = bankRepo.Entities
                    .Where(b => b.DeletedAt == null)
                    .Include(b => b.McqQuestions.Where(q => q.DeletedAt == null))
                        .ThenInclude(q => q.McqOptions);

                // Apply filters
                if (bankId.HasValue)
                {
                    query = query.Where(b => b.BankId == bankId.Value);
                }

                if (!string.IsNullOrEmpty(nameSearch))
                {
                    query = query.Where(b => b.Name.Contains(nameSearch));
                }

                // Order by creation date
                query = query.OrderByDescending(b => b.CreatedAt);

                // Get paginated results
                PaginatedList<Bank> resultQuery = await bankRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map to DTOs
                IReadOnlyCollection<GetBankWithQuestionsDTO> result = resultQuery.Items.Select(bank => new GetBankWithQuestionsDTO
                {
                    BankId = bank.BankId,
                    Name = bank.Name,
                    CreatedAt = bank.CreatedAt,
                    TotalQuestions = bank.McqQuestions.Count,
                    Questions = bank.McqQuestions
                        .Select(q => new BankQuestionDTO
                        {
                            QuestionId = q.QuestionId,
                            Text = q.Text,
                            CreatedAt = q.CreatedAt,
                            Options = q.McqOptions
                                .Select(o => new BankOptionDTO
                                {
                                    OptionId = o.OptionId,
                                    Text = o.Text,
                                    IsCorrect = o.IsCorrect
                                })
                                .ToList()
                        })
                        .ToList()
                }).ToList();

                return new PaginatedList<GetBankWithQuestionsDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving banks: {ex.Message}");
            }
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

        public async Task ImportMcqQuestionsFromCsvAsync(IFormFile csvFile, Guid testId, BankStatusEnum status)
        {
            var result = new McqImportResultDTO();

            try
            {
                // Validate file
                CsvHelpers.ValidateCsvFile(csvFile);

                // Read CSV content
                string csvContent;
                using (var reader = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8))
                {
                    csvContent = await reader.ReadToEndAsync();
                }

                // Extract bank name from CSV
                string bankName = CsvHelpers.ExtractBankNameFromCsv(csvContent);

                if (string.IsNullOrWhiteSpace(bankName))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Bank name is required in first row. Format: BankName,<name> or BankName;<name>"
                    );
                }

                // Parse CSV rows
                List<McqCsvRowDTO> csvRows = ParseCsvRows(csvContent);

                result.TotalRows = csvRows.Count;
                result.BankName = bankName;

                if (csvRows.Count == 0)
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "CSV file contains no valid question rows."
                    );
                }

                try
                {
                    // Start transaction
                    _unitOfWork.BeginTransaction();

                    IGenericRepository<Bank> bankRepo = _unitOfWork.GetRepository<Bank>();
                    IGenericRepository<McqQuestion> questionRepo = _unitOfWork.GetRepository<McqQuestion>();
                    IGenericRepository<McqOption> optionRepo = _unitOfWork.GetRepository<McqOption>();

                    await ClearQuestionsInTest(testId);

                    // Create new bank
                    Bank bank = await CreateNewBankAsync(bankRepo, bankName);
                    result.BankId = bank.BankId;

                    // Process all questions
                    int rowNumber = 2; // Start from row 2 (row 1 is BankName)
                    foreach (var row in csvRows)
                    {
                        rowNumber++;
                        try
                        {
                            // Validate row
                            string? validationError = ValidateCsvRow(row, rowNumber);
                            if (!string.IsNullOrEmpty(validationError))
                            {
                                result.Errors.Add(validationError);
                                result.ErrorCount++;
                                continue;
                            }

                            // Create question
                            McqQuestion? question = new McqQuestion
                            {
                                QuestionId = Guid.NewGuid(),
                                BankId = bank.BankId,
                                Text = row.QuestionText.Trim(),
                                CreatedAt = DateTime.UtcNow,
                                DeletedAt = null
                            };

                            await questionRepo.InsertAsync(question);
                            await _unitOfWork.SaveAsync();

                            // Create options (2-4)
                            await CreateOptionsForQuestion(optionRepo, question.QuestionId, row);
                            await _unitOfWork.SaveAsync();

                            result.ImportedQuestionIds.Add(question.QuestionId);
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

                }
                catch (Exception)
                {
                    // Roll back transaction on error
                    _unitOfWork.RollBack();
                    throw;
                }

                await _mcqTestService.AddQuestionsToTestAsync(testId, result.BankId);

            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                    throw;

                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error importing MCQ questions: {ex.Message}"
                );
            }
        }

        private static List<McqCsvRowDTO> ParseCsvRows(string csvContent)
        {
            // Skip first row (BankName metadata)
            string[]? lines = csvContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string? dataContent = string.Join(Environment.NewLine, lines.Skip(1));

            // Detect delimiter from header row
            char delimiter = CsvHelpers.DetectDelimiter(dataContent);

            // Parse CSV using CsvHelper with detected delimiter
            using StringReader? reader = new StringReader(dataContent);
            using CsvReader? csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
                Delimiter = delimiter.ToString()
            });

            List<McqCsvRowDTO> result = new List<McqCsvRowDTO>();

            // Read all records and filter out empty rows
            foreach (var row in csv.GetRecords<McqCsvRowDTO>())
            {
                if (IsEmptyRow(row))
                    break;

                result.Add(row);
            }

            return result;
        }

        private static async Task<Bank> CreateNewBankAsync(
            IGenericRepository<Bank> bankRepo,
            string bankName)
        {
            Bank bank = new Bank
            {
                BankId = Guid.NewGuid(),
                Name = bankName,
                CreatedAt = DateTime.UtcNow,
                DeletedAt = null
            };

            await bankRepo.InsertAsync(bank);
            await bankRepo.SaveAsync();

            return bank;
        }

        private static async Task CreateOptionsForQuestion(
            IGenericRepository<McqOption> optionRepo,
            Guid questionId,
            McqCsvRowDTO row)
        {
            // Add Option A and B (required)
            IList<(string, bool)> options = new List<(string text, bool isCorrect)>
            {
                (row.OptionA.Trim(), row.CorrectAnswer.ToUpper() == "A"),
                (row.OptionB.Trim(), row.CorrectAnswer.ToUpper() == "B")
            };

            // Add Option C if present
            if (!string.IsNullOrWhiteSpace(row.OptionC))
                options.Add((row.OptionC.Trim(), row.CorrectAnswer.ToUpper() == "C"));

            // Add Option D if present
            if (!string.IsNullOrWhiteSpace(row.OptionD))
                options.Add((row.OptionD.Trim(), row.CorrectAnswer.ToUpper() == "D"));

            // Insert options into repository
            foreach (var (text, isCorrect) in options)
            {
                McqOption option = new McqOption
                {
                    OptionId = Guid.NewGuid(),
                    QuestionId = questionId,
                    Text = text,
                    IsCorrect = isCorrect
                };
                await optionRepo.InsertAsync(option);
            }
        }

        private static string? ValidateCsvRow(McqCsvRowDTO row, int rowNumber)
        {
            // Validate question text
            if (string.IsNullOrWhiteSpace(row.QuestionText))
                return $"Row {rowNumber}: Question text is required.";

            // Validate question text length
            if (row.QuestionText.Length > 1000)
                return $"Row {rowNumber}: Question text exceeds 1000 characters.";

            // Validate options A and B
            if (string.IsNullOrWhiteSpace(row.OptionA) || string.IsNullOrWhiteSpace(row.OptionB))
                return $"Row {rowNumber}: At least 2 options (A and B) are required.";

            // Validate correct answer
            string? correctAnswer = row.CorrectAnswer?.ToUpper().Trim();
            if (string.IsNullOrEmpty(correctAnswer) || !"ABCD".Contains(correctAnswer))
                return $"Row {rowNumber}: Correct answer must be A, B, C, or D.";

            // Validate presence of options C and D if they are the correct answer
            bool hasOptionC = !string.IsNullOrWhiteSpace(row.OptionC);
            bool hasOptionD = !string.IsNullOrWhiteSpace(row.OptionD);

            // Check if correct answer is C or D but option is missing
            if (correctAnswer == "C" && !hasOptionC)
                return $"Row {rowNumber}: Correct answer is C but Option C is empty.";

            if (correctAnswer == "D" && !hasOptionD)
                return $"Row {rowNumber}: Correct answer is D but Option D is empty.";

            return null;
        }

        private static bool IsEmptyRow(McqCsvRowDTO row)
        {
            return string.IsNullOrWhiteSpace(row.QuestionText) &&
                   string.IsNullOrWhiteSpace(row.OptionA) &&
                   string.IsNullOrWhiteSpace(row.OptionB) &&
                   string.IsNullOrWhiteSpace(row.OptionC) &&
                   string.IsNullOrWhiteSpace(row.OptionD) &&
                   string.IsNullOrWhiteSpace(row.CorrectAnswer);
        }

        private async Task ClearQuestionsInTest(Guid testId)
        {
            IGenericRepository<McqTest> testRepo = _unitOfWork.GetRepository<McqTest>();
            IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();

            // Get the test
            McqTest? existingMcqTest = await testRepo
                .Entities
                .Where(t => t.TestId == testId)
                .Include(t => t.McqTestQuestions)
                .FirstOrDefaultAsync();

            // Validate test existence
            if (existingMcqTest == null)
            {
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"MCQ Test with ID {testId} not found."
                );
            }

            // Remove existing questions from the test
            if (existingMcqTest.McqTestQuestions != null && existingMcqTest.McqTestQuestions.Any())
            {
                // Get all existing questions for the test
                List<McqTestQuestion> questionsToDelete = existingMcqTest.McqTestQuestions.ToList();

                // Delete existing questions
                foreach (McqTestQuestion question in questionsToDelete)
                {
                    mcqTestQuestionRepo.Delete(question);
                }

                // Save changes after deletions
                await _unitOfWork.SaveAsync();
            }
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
    }
}
