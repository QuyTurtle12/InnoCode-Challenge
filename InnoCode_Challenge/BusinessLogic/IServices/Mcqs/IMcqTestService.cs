using Repository.DTOs.McqTestDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqTestService
    {
        Task<PaginatedList<GetMcqTestDTO>> GetPaginatedMcqTestAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch);
        Task CreateMcqTestAsync(CreateMcqTestDTO mcqTestDTO);
        Task UpdateMcqTestAsync(Guid id, UpdateMcqTestDTO mcqTestDTO);
        Task DeleteMcqTestAsync(Guid id);
    }
}
