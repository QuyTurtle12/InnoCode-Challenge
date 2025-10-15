using Repository.DTOs.ContestDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IContestService
    {
        Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(int pageNumber, int pageSize, Guid? idSearch, string? nameSearch, int? yearSearch, DateTime? startDate, DateTime? endDate);
        Task CreateContestAsync(CreateContestDTO contestDTO);
        Task UpdateContestAsync(Guid id, UpdateContestDTO contestDTO);
        Task DeleteContestAsync(Guid id);
        Task PublishContestAsync(Guid id);
    }
}
