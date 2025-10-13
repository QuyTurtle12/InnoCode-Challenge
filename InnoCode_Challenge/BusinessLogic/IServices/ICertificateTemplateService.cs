using Repository.DTOs.CertificateTemplateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ICertificateTemplateService
    {
        Task<PaginatedList<GetCertificateTemplateDTO>> GetPaginatedCertificateTemplateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? templateNameSearch, string? contestNameSearch);
        Task CreateCertificateTemplateAsync(CreateCertificateTemplateDTO templateDTO);
        Task UpdateCertificateTemplateAsync(Guid id, UpdateCertificateTemplateDTO templateDTO);
        Task DeleteCertificateTemplateAsync(Guid id);
    }
}
