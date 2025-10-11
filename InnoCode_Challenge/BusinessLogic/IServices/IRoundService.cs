using Repository.DTOs.RoundDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IRoundService
    {
        Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate);
        Task CreateRound(CreateRoundDTO roundDTO);
        Task UpdateRound(Guid id, UpdateRoundDTO roundDTO);
        Task DeleteRound(Guid id);
    }
}
