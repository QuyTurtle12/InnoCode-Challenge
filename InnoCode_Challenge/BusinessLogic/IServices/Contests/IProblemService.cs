using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RubricDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface IProblemService
    {
        Task<PaginatedList<GetProblemDTO>> GetPaginatedProblemAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? roundIdsearch, string? roundNameSearch);
        Task CreateProblemAsync(Guid roundId, CreateProblemDTO problemDTO);
        Task UpdateProblemAsync(Guid id, UpdateProblemDTO problemDTO);
        Task DeleteProblemAsync(Guid id);

        Task<RubricTemplateDTO> GetRubricTemplateAsync(Guid roundId);
        Task<RubricTemplateDTO> CreateRubricAsync(Guid roundId, CreateRubricDTO createRubricDTO);
        Task<RubricTemplateDTO> UpdateRubricAsync(Guid roundId, UpdateRubricDTO updateRubricDTO);

        
    }
}
