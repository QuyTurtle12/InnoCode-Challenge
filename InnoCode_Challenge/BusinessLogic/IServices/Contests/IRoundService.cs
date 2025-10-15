using Repository.DTOs.RoundDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IRoundService
    {
        Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? roundNameSearch, string? contestNameSearch, DateTime? startDate, DateTime? endDate);
        Task CreateRoundAsync(CreateRoundDTO roundDTO);
        Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO);
        Task DeleteRoundAsync(Guid id);
    }
}
