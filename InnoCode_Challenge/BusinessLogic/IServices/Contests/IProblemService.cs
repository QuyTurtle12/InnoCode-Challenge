using Microsoft.AspNetCore.Http;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RubricDTOs;
using Repository.DTOs.RubricDTOs.Repository.DTOs.RubricDTOs;
using Repository.DTOs.TestCaseDTOs;
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
        Task<RubricTemplateDTO> CreateRubricCriterionAsync(Guid roundId, CreateRubricDTO createRubricDTO);
        Task<RubricTemplateDTO> UpdateRubricCriterionAsync(Guid roundId, UpdateRubricDTO updateRubricDTO);
        Task DeleteRubricCriterionAsync(Guid rubricId);
        Task<RubricCsvImportResultDTO> ImportRubricFromCsvAsync(IFormFile csvFile, Guid roundId);
    }
}
