using Repository.DTOs.ContestDTOs;
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
            string? nameSearch,
            int? yearSearch,
            DateTime? startDate,
            DateTime? endDate,
            bool isMyParticipatedContest = false,
            bool isMyContest = false
            );
        Task<GetContestDTO> UpdateContestAsync(Guid id, UpdateContestDTO contestDTO);
        Task DeleteContestAsync(Guid id);
        Task<ContestCreatedDTO> CreateContestWithPolicyAsync(CreateContestAdvancedDTO dto);
        Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId);
        Task PublishIfReadyAsync(Guid contestId);
        Task CancelledContest(Guid contestId);

        Task<IReadOnlyList<ContestPolicyDTO>> GetContestPoliciesAsync(Guid contestId);
        Task SetContestPoliciesAsync(Guid contestId, IList<ContestPolicyDTO> policies);
        Task DeleteContestPolicyAsync(Guid contestId, string policyKey);

    }
}
