using Microsoft.AspNetCore.Http;
using Repository.DTOs.CertificateTemplateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Certificates
{
    public interface ICertificateTemplateService
    {
        Task<PaginatedList<GetCertificateTemplateDTO>> GetPaginatedCertificateTemplateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? templateNameSearch, string? contestNameSearch);
        Task CreateCertificateTemplateAsync(IFormFile file, CreateCertificateTemplateDTO templateDTO);
        Task UpdateCertificateTemplateAsync(Guid id, IFormFile? file, UpdateCertificateTemplateDTO templateDTO);
        Task DeleteCertificateTemplateAsync(Guid id);
    }
}
