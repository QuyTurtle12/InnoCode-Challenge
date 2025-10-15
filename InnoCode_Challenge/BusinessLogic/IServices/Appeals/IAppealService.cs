using Repository.DTOs.AppealDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Appeals
{
    public interface IAppealService
    {
        Task<PaginatedList<GetAppealDTO>> GetPaginatedAppealAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? teamIdSearch, Guid? ownerIdSearch, string? teamNameSearch, string? ownerNameSearch);
        Task CreateAppealAsync(CreateAppealDTO AppealDTO);
        Task UpdateAppealAsync(Guid id, UpdateAppealDTO AppealDTO);
        Task DeleteAppealAsync(Guid id);
    }
}
