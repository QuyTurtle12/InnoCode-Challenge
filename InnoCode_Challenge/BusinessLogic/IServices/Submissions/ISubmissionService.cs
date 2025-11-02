using Microsoft.AspNetCore.Http;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.SubmissionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Submissions
{
    public interface ISubmissionService
    {
        Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName);
        Task CreateSubmissionAsync(CreateSubmissionDTO SubmissionDTO);
        Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO SubmissionDTO);
        Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(CreateSubmissionDTO submissionDTO);
        Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result, int previousSubmissionsCount, double? penaltyRate);
        Task<Guid> CreateFileSubmissionAsync(CreateFileSubmissionDTO submissionDTO, IFormFile file);
        Task<string> GetFileSubmissionDownloadUrlAsync(Guid submissionId);
        Task<bool> UpdateFileSubmissionScoreAsync(Guid submissionId, double score, string feedback);
        Task<GetSubmissionDTO> GetSubmissionResultOfLoggedInStudentAsync(Guid problemId);
    }
}
