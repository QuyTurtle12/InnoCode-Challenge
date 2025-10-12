using Repository.DTOs.McqQuestionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMcqQuestionService
    {
        Task<PaginatedList<GetMcqQuestionDTO>> GetPaginatedMcqQuestionAsync(int pageNumber, int pageSize, Guid? idSearch);
        Task CreateMcqQuestion(CreateMcqQuestionDTO mcqQuestionDTO);
        Task UpdateMcqQuestion(Guid id, UpdateMcqQuestionDTO mcqQuestionDTO);
        Task DeleteMcqQuestion(Guid id);
    }
}
