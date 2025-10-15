using Repository.DTOs.McqOptionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqOptionService
    {
        Task<PaginatedList<GetMcqOptionDTO>> GetPaginatedMcqOptionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? questionIdSearch);
        Task CreateMcqOptionAsync(CreateMcqOptionDTO mcqOptionDTO);
        Task UpdateMcqOptionAsync(Guid id, UpdateMcqOptionDTO mcqOptionDTO);
        Task DeleteMcqOptionAsync(Guid id);
    }
}
