using Repository.DTOs.McqTestDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMcqTestService
    {
        Task<PaginatedList<GetMcqTestDTO>> GetPaginatedMcqTestAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch);
        Task CreateMcqTest(CreateMcqTestDTO mcqTestDTO);
        Task UpdateMcqTest(Guid id, UpdateMcqTestDTO mcqTestDTO);
        Task DeleteMcqTest(Guid id);
    }
}
