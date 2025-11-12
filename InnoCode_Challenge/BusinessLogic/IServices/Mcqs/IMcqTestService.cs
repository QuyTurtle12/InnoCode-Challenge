using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.McqTestQuestionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqTestService
    {
        Task<PaginatedList<GetMcqTestDTO>> GetPaginatedMcqTestAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdSearch);
        Task CreateMcqTestAsync(Guid roundId, CreateMcqTestDTO mcqTestDTO);
        Task UpdateMcqTestAsync(Guid id, UpdateMcqTestDTO mcqTestDTO);
        Task DeleteMcqTestAsync(Guid id);
        Task AddQuestionsToTestAsync(Guid testId, Guid bankId);
        Task BulkUpdateQuestionWeightsAsync(Guid testId, BulkUpdateQuestionWeightsDTO dto);

    }
}
