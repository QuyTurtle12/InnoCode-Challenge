using Repository.DTOs.ContestDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IContestService
    {
        Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(int pageNumber, int pageSize, Guid? idSearch, string? nameSearch, int? yearSearch, DateTime? startDate, DateTime? endDate);
        Task CreateContest(CreateContestDTO contestDTO);
        Task UpdateContest(Guid id, UpdateContestDTO contestDTO);
        Task DeleteContest(Guid id);
        Task PublishContest(Guid id);
    }
}
