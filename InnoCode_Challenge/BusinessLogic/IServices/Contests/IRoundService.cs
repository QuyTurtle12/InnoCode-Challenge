using Repository.DTOs.RoundDTOs;
using Repository.DTOs.SubmissionDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IRoundService
    {
        Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate);
        Task<GetRoundDTO> GetRoundByIdAsync(Guid id, string? openCode);
        Task CreateRoundAsync(Guid contestId, CreateRoundDTO roundDTO);
        Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO);
        Task DeleteRoundAsync(Guid id);
        Task DistributeSubmissionsToJudgesAsync(Guid roundId);
        Task<int?> GetRoundTimeLimitSecondsAsync(Guid roundId);
        Task MarkFinishFinishRoundAsync(Guid roundId);
        Task<string> GenerateOpenCode(Guid roundId);
        Task ValidateOpenCode(Guid roundId, string openCode);
        Task<string> GetOpenCode(Guid roundId);
        Task<GetRoundDTO> StartRoundNowAsync(Guid roundId);
        Task<GetRoundDTO> EndRoundNowAsync(Guid roundId);
        Task<DateTime> GetFinalizeNotBeforeAsync(Guid roundId);
        Task<RoundTimelineDTO> GetRoundTimelineAsync(Guid roundId);
        Task FastForwardAppealSubmitDeadlineAsync(Guid roundId);
        Task FastForwardAppealReviewDeadlineAsync(Guid roundId);
        Task FastForwardJudgeDeadlineAsync(Guid roundId);
        Task TryFinalizeRoundAsync(Guid roundId);
        Task RegenerateOpenCodeAsync(Guid roundId);
        Task<string?> GetOrganizerMockTestTemplateUrl();
        Task<string?> GetStudentMockTestTemplateUrl();

    }
}
