using System.Security.Claims;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.BankDTOs;
using Repository.DTOs.QuizDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mcqs
{
    public class QuizService : IQuizService
    {
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILeaderboardEntryService _leaderboardService;

        public QuizService(IUOW unitOfWork, IHttpContextAccessor httpContextAccessor, ILeaderboardEntryService leaderboardService)
        {
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
            _leaderboardService = leaderboardService;
        }

        public async Task<QuizResultDTO> ProcessQuizSubmissionAsync(CreateQuizSubmissionDTO quizSubmissionDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

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

                // Verify the MCQ test exists
                IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();
                McqTest? mcqTest = await mcqTestRepo.GetByIdAsync(quizSubmissionDTO.TestId);

                if (mcqTest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"MCQ Test with ID {quizSubmissionDTO.TestId} not found");
                }

                // Create MCQ attempt record
                IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();
                McqAttempt attempt = new McqAttempt
                {
                    AttemptId = Guid.NewGuid(),
                    TestId = quizSubmissionDTO.TestId,
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

                    if (option == null)
                    {
                        continue; // Skip invalid options
                    }

                    // Check if the answer is correct
                    bool isCorrect = option.IsCorrect;
                    if (isCorrect)
                    {
                        correctAnswers++;
                    }

                    // Create attempt item
                    McqAttemptItem attemptItem = new McqAttemptItem
                    {
                        ItemId = Guid.NewGuid(),
                        AttemptId = attempt.AttemptId,
                        TestId = quizSubmissionDTO.TestId,
                        QuestionId = answer.QuestionId,
                        SelectedOptionId = answer.SelectedOptionId,
                        Correct = isCorrect
                    };

                    await attemptItemRepo.InsertAsync(attemptItem);

                    // Add to result list
                    QuizAnswerResultDTO answerResult = new QuizAnswerResultDTO
                    {
                        QuestionId = answer.QuestionId,
                        QuestionText = option.Question?.Text ?? string.Empty,
                        SelectedOptionId = answer.SelectedOptionId,
                        SelectedOptionText = option.Text,
                        IsCorrect = isCorrect
                    };

                    answerResults.Add(answerResult);
                }

                // Save all attempt items
                await _unitOfWork.SaveAsync();

                // Calculate the score (percentage of correct answers)
                double score = totalQuestions > 0 ? ((double)correctAnswers / totalQuestions) * 100 : 0;

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
                    TestId = quizSubmissionDTO.TestId,
                    TestName = testName,
                    StudentId = studentId,
                    StudentName = userFullName,
                    SubmittedAt = DateTime.UtcNow,
                    TotalQuestions = totalQuestions,
                    CorrectAnswers = correctAnswers,
                    Score = score,
                    AnswerResults = answerResults
                };

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

                // Map to DTOs
                IReadOnlyCollection<QuizAttemptSummaryDTO> result = resultQuery.Items.Select(item => new QuizAttemptSummaryDTO
                {
                    AttemptId = item.AttemptId,
                    TestId = item.TestId,
                    TestName = item.Test?.Name ?? "Unknown Test",
                    StudentId = item.StudentId,
                    StudentName = item.Student?.User?.Fullname ?? "Unknown Student",
                    StartTime = item.Start,
                    EndTime = item.End,
                    Score = item.Score ?? 0
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

        public async Task<PaginatedList<GetQuizDTO>> GetQuizByRoundIdAsync(int pageNumber, int pageSize, Guid roundId)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
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

                IQueryable<Round> query = roundRepo.Entities
                    .Where(r => r.RoundId == roundId)
                    .Include(r => r.McqTests)
                        .ThenInclude(t => t.McqTestQuestions)
                            .ThenInclude(tq => tq.Question)
                                .ThenInclude(q => q.McqOptions)
                    .OrderByDescending(t => t.Name);

                // Get paginated results
                PaginatedList<Round> resultQuery = await roundRepo.GetPagingAsync(query, pageNumber, pageSize);

                IReadOnlyCollection<GetQuizDTO> result = resultQuery.Items.Select(item =>
                {
                    GetQuizDTO quizDTO = new GetQuizDTO
                    {
                        RoundId = item.RoundId,
                        McqTests = item.McqTests.Select(t => new McqTestDTO
                        {
                            TestId = t.TestId,
                            Questions = t.McqTestQuestions
                                    .OrderBy(x => random.Next()) // shuffle question per student
                                    .Select((q, index) => new QuestionDTO
                                    {
                                        QuestionId = q.QuestionId,
                                        Weight = q.Weight,
                                        OrderIndex = index + 1,
                                        Text = q.Question?.Text ?? string.Empty,
                                        Options = q.Question?.McqOptions
                                            .OrderBy(x => random.Next()) // shuffle options
                                            .Select(o => new OptionDTO
                                            {
                                                OptionId = o.OptionId,
                                                Text = o.Text,
                                                IsCorrect = o.IsCorrect
                                            })
                                            .ToList() ?? new List<OptionDTO>()
                                    }).ToList()
                        }).ToList()
                    };
                    return quizDTO;
                }).ToList();

                return new PaginatedList<GetQuizDTO>(
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
    }
}
