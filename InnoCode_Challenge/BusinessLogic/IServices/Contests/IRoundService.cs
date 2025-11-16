using Repository.DTOs.RoundDTOs;
using Repository.DTOs.SubmissionDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IRoundService
    {
        Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate);
        Task<GetRoundDTO> GetRoundByIdAsync(Guid id);
        Task CreateRoundAsync(Guid contestId, CreateRoundDTO roundDTO);
        Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO);
        Task DeleteRoundAsync(Guid id);

        Task DistributeSubmissionsToJudgesAsync(Guid roundId);
        Task<PaginatedList<SubmissionDistributionDTO>> GetManualTypeSubmissionsByRoundId(int pageNumber, int pageSize, Guid roundId, SubmissionStatusEnum? statusFilter = null);
        Task<int?> GetRoundTimeLimitSecondsAsync(Guid roundId);

        Task MarkFinishFinishRoundAsync(Guid roundId);
    }
}
