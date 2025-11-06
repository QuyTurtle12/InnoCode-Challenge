using Repository.DTOs.ContestDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IContestService
    {
        Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(int pageNumber, int pageSize, Guid? idSearch, string? nameSearch, int? yearSearch, DateTime? startDate, DateTime? endDate);
        //Task CreateContestAsync(CreateContestDTO contestDTO);
        Task<GetContestDTO> UpdateContestAsync(Guid id, UpdateContestDTO contestDTO);
        Task DeleteContestAsync(Guid id);
        //Task PublishContestAsync(Guid id);
        Task<ContestCreatedDTO> CreateContestWithPolicyAsync(CreateContestAdvancedDTO dto);
        Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId);
        Task PublishIfReadyAsync(Guid contestId);

        Task<IReadOnlyList<ContestPolicyDTO>> GetContestPoliciesAsync(Guid contestId);
        Task SetContestPoliciesAsync(Guid contestId, IList<ContestPolicyDTO> policies);
        Task DeleteContestPolicyAsync(Guid contestId, string policyKey);

    }
}
