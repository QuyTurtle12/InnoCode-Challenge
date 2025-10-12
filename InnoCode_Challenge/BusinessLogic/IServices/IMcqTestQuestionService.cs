using Repository.DTOs.McqTestQuestionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMcqTestQuestionService
    {
        Task<PaginatedList<GetMcqTestQuestionDTO>> GetPaginatedTestQuestionAsync(int pageNumber, int pageSize, Guid? testIdSearch, Guid? questionIdSearch);
        Task CreateTestQuestionAsync(CreateMcqTestQuestionDTO createTestQuestionDTO);
        Task UpdateTestQuestionAsync(Guid id, UpdateMcqTestQuestionDTO updateTestQuestionDTO);
        Task DeleteTestQuestionAsync(Guid id);
    }
}
