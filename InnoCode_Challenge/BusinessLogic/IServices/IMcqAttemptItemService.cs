using Repository.DTOs.McqAttemptItemDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMcqAttemptItemService
    {
        Task<PaginatedList<GetMcqAttemptItemDTO>> GetPaginatedMcqAttemptItemAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? testIdSearch, Guid? questionIdSearch, string? testName, string? questionText);
        Task CreateMcqAttemptItemAsync(CreateMcqAttemptItemDTO mcqAttemptItemDTO);
    }
}
