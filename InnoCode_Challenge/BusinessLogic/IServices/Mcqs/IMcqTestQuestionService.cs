using Repository.DTOs.McqTestQuestionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqTestQuestionService
    {
        Task<PaginatedList<GetMcqTestQuestionDTO>> GetPaginatedTestQuestionAsync(int pageNumber, int pageSize, Guid? testIdSearch, Guid? questionIdSearch);
        Task CreateTestQuestionAsync(Guid testId, Guid bankId);
        Task UpdateTestQuestionAsync(Guid id, UpdateMcqTestQuestionDTO updateTestQuestionDTO);
        Task DeleteTestQuestionAsync(Guid id);
    }
}
