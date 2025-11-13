using Microsoft.AspNetCore.Http;
using Repository.DTOs.BankDTOs;
using Repository.DTOs.QuizDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IQuizService
    {
        Task<QuizResultDTO> ProcessQuizSubmissionAsync(Guid roundId, CreateQuizSubmissionDTO quizSubmissionDTO);
        Task<QuizResultDTO> GetQuizAttemptResultAsync(Guid attemptId);
        Task<PaginatedList<QuizAttemptSummaryDTO>> GetStudentQuizAttemptsAsync(int pageNumber, int pageSize, Guid? studentId, Guid? testId, Guid roundId, bool IsForCurrentLoggedInStudent = false);
        Task<PaginatedList<GetBankWithQuestionsDTO>> GetPaginatedBanksAsync(int pageNumber, int pageSize, Guid? bankId, string? nameSearch);
        Task<GetQuizDTO> GetQuizByRoundIdAsync(int pageNumber, int pageSize, Guid roundId);
        Task ImportMcqQuestionsFromCsvAsync(IFormFile csvFile, Guid TestId, BankStatusEnum bankStatus);
        Task<string> DownloadMcqImportTemplate();
    }
}
