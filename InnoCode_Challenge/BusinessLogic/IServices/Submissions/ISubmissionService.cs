using Microsoft.AspNetCore.Http;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Submissions
{
    public interface ISubmissionService
    {
        Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName);
        Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO SubmissionDTO);
        Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(Guid roundId, CreateSubmissionDTO submissionDTO);
        Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result, int previousSubmissionsCount, double? penaltyRate);
        Task<Guid> CreateFileSubmissionAsync(Guid roundId, IFormFile file);
        Task<string> GetFileSubmissionDownloadUrlAsync(Guid submissionId);
        Task<bool> UpdateFileSubmissionScoreAsync(Guid submissionId, double score, string feedback);
        Task<GetSubmissionDTO> GetSubmissionResultOfLoggedInStudentAsync(Guid roundId);
        Task AddScoreToTeamInLeaderboardAsync(Guid submissionId);
        Task<RubricEvaluationResultDTO> SubmitRubricEvaluationAsync(Guid submissionId, SubmitRubricScoreDTO rubricScoreDTO);
        Task<RubricEvaluationResultDTO> GetMyManualTestResultAsync(Guid roundId);
        Task<PaginatedList<RubricEvaluationResultDTO>> GetAllManualTestResultsByRoundAsync(Guid roundId, int pageNumber, int pageSize, Guid? studentIdSearch, Guid? teamIdSearch, string? studentNameSearch, string? teamNameSearch);
    }
}
