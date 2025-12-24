using Microsoft.AspNetCore.Http;
using Repository.DTOs.JudgeDTOs;
using Repository.DTOs.MockTestDTOs;
using Repository.DTOs.PlagiarismDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.SubmissionDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Submissions
{
    public interface ISubmissionService
    {
        Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO SubmissionDTO);
        Task<JudgeSubmissionResultDTO> EvaluateSubmissionAsync(Guid roundId, CreateSubmissionDTO submissionDTO, TestCaseEvaluationTypeEnum evaluationType);
        Task SaveSubmissionResultAsync(Guid submissionId, JudgeSubmissionResultDTO result, int previousSubmissionsCount, double? penaltyRate);
        Task<Guid> CreateFileSubmissionAsync(Guid roundId, IFormFile file);
        Task<string> GetFileSubmissionDownloadUrlAsync(Guid submissionId);
        Task AcceptResultAsync(Guid submissionId);
        Task<RubricEvaluationResultDTO> SubmitRubricEvaluationAsync(Guid submissionId, SubmitRubricScoreDTO rubricScoreDTO);
        Task<RubricEvaluationResultDTO> GetMyManualTestResultAsync(Guid roundId);
        Task<PaginatedList<RubricEvaluationResultDTO>> GetAllManualTestResultsByRoundAsync(Guid roundId, int pageNumber, int pageSize, Guid? studentIdSearch, Guid? teamIdSearch, string? studentNameSearch, string? teamNameSearch);
        Task<GetSubmissionDTO> GetMyAutoTestResultAsync(Guid roundId);
        Task<PaginatedList<GetSubmissionDTO>> GetAllAutoTestResultsByRoundAsync(Guid roundId, int pageNumber, int pageSize, Guid? studentIdSearch, Guid? teamIdSearch, string? studentNameSearch, string? teamNameSearch);
        Task<PaginatedList<SubmissionDistributionDTO>> GetSubmissionsByJudgeByAsync(int pageNumber, int pageSize, Guid? contestIdSearch, string? contestName, Guid? roundIdSearch, string? roundName, Guid? teamIdSearch, string? teamName, Guid? studentIdSearch, string? studentName, SubmissionStatusEnum? statusFilter = null);
        Task<SubmissionDistributionDTO> GetSubmissionByIdAsync(Guid submissionId);
        Task<PaginatedList<PlagiarismQueueItemDTO>> GetPlagiarismQueueAsync(
            int pageNumber,
            int pageSize,
            Guid? contestId,
            Guid? roundId,
            string? studentName,
            string? teamName);

        Task<PlagiarismSubmissionDetailDTO> GetPlagiarismSubmissionDetailAsync(Guid submissionId);
        Task ResolvePlagiarismSubmissionAsync(Guid submissionId, ResolvePlagiarismDTO dto);
        Task<JudgeSubmissionResultDTO> CreateNullAutoSubmissionAsync(Guid roundId);
        Task<Guid> CreateNullManualSubmissionAsync(Guid roundId);
        Task<MockTestResultDTO> EvaluateMockTestSubmissionAsync(
            Guid roundId,
            CreateSubmissionDTO submissionDTO,
            TestCaseEvaluationTypeEnum evaluationType);

    }
}
