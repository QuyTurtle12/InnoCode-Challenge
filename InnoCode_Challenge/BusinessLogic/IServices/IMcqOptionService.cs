using Repository.DTOs.McqOptionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMcqOptionService
    {
        Task<PaginatedList<GetMcqOptionDTO>> GetPaginatedMcqOptionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? questionIdSearch);
        Task CreateMcqOption(CreateMcqOptionDTO mcqOptionDTO);
        Task UpdateMcqOption(Guid id, UpdateMcqOptionDTO mcqOptionDTO);
        Task DeleteMcqOption(Guid id);
    }
}
