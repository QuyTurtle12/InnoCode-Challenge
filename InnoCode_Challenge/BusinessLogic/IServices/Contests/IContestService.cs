using Microsoft.AspNetCore.Http;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.RoundDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IContestService
    {
        Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(
            int pageNumber,
            int pageSize,
            Guid? idSearch,
            Guid? creatorIdSearch,
            Guid? roundIdSearch,
            string? nameSearch,
            int? yearSearch,
            DateTime? startDate,
            DateTime? endDate,
            bool isMyParticipatedContest = false,
            bool isMyContest = false
            );

        Task<GetContestDTO> GetContestByIdAsync(Guid id);
        Task<GetContestDTO> UpdateContestAsync(Guid id, UpdateContestDTO contestDTO);
        Task DeleteContestAsync(Guid id);
        Task<ContestCreatedDTO> CreateContestAsync(CreateContestAdvancedDTO dto);
        Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId);
        Task PublishIfReadyAsync(Guid contestId);
        Task CancelContestAsync(Guid contestId);
        Task<ContestTimelineDTO> GetContestTimelineAsync(Guid contestId);

        Task<IReadOnlyList<ContestPolicyDTO>> GetContestPoliciesAsync(Guid contestId);
        Task SetContestPoliciesAsync(Guid contestId, IList<ContestPolicyDTO> policies);
        Task DeleteContestPolicyAsync(Guid contestId, string policyKey);
        Task<GetContestDTO> StartContestNowAsync(Guid contestId);
        Task<GetContestDTO> EndContestNowAsync(Guid contestId);
        Task<string> DownloadContestReportZipAsync(Guid contestId);
        Task<string> DownloadMentorContestReportAsync(Guid contestId);

    }
}
