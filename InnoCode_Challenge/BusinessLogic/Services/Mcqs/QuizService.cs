using System.Security.Claims;
using AutoMapper;
using BusinessLogic.IServices.Mcqs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public QuizService(IUOW unitOfWork, IMapper mapper, IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
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

                // Get test name
                string testName = mcqTest.Name ?? "Unknown Test";

                // Create result DTO
                QuizResultDTO result = new QuizResultDTO
                {
                    AttemptId = attempt.AttemptId,
                    TestId = quizSubmissionDTO.TestId,
                    TestName = testName,
                    StudentId = studentId,
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
                McqAttempt? attempt = await attemptRepo.GetByIdAsync(attemptId);

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
             int pageNumber = 1, int pageSize = 10, Guid? studentId = null, Guid? testId = null, bool IsForCurrentLoggedInStudent = false)
        {
            try
            {
                IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();

                // Start with base query
                IQueryable<McqAttempt> query = attemptRepo.Entities
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
    }
}
