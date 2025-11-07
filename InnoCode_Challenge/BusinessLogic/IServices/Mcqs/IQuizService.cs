using Repository.DTOs.QuizDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IQuizService
    {
        Task<PaginatedList<GetQuizDTO>> GetQuizByRoundIdAsync(int pageNumber, int pageSize, Guid roundId);
        Task<QuizResultDTO> ProcessQuizSubmissionAsync(CreateQuizSubmissionDTO quizSubmissionDTO);
        Task<QuizResultDTO> GetQuizAttemptResultAsync(Guid attemptId);
        Task<PaginatedList<QuizAttemptSummaryDTO>> GetStudentQuizAttemptsAsync(int pageNumber, int pageSize, Guid? studentId, Guid? testId, Guid roundId, bool IsForCurrentLoggedInStudent = false);
    }
}
