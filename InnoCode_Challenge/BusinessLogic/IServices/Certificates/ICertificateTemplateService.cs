using Microsoft.AspNetCore.Http;
using Repository.DTOs.CertificateTemplateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Certificates
{
    public interface ICertificateTemplateService
    {
        Task<CertificateTemplateDTO> CreateAsync(CreateCertificateTemplateDTO dto);
        Task<CertificateTemplateDTO?> GetByIdAsync(Guid id);
        Task<PaginatedList<CertificateTemplateDTO>> GetAsync(Guid? contestId, string? search, int page, int pageSize, string? sortBy, bool desc);
    }
}
