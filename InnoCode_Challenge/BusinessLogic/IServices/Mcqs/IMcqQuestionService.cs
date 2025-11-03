using Repository.DTOs.McqQuestionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqQuestionService
    {
        Task<PaginatedList<GetMcqQuestionDTO>> GetPaginatedMcqQuestionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid bankId);
        Task CreateMcqQuestionAsync(CreateMcqQuestionDTO mcqQuestionDTO);
        Task UpdateMcqQuestionAsync(Guid id, UpdateMcqQuestionDTO mcqQuestionDTO);
        Task DeleteMcqQuestionAsync(Guid id);
    }
}
