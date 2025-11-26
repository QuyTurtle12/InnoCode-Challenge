using Repository.DTOs.CertificateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Certificates
{
    public interface ICertificateService
    {
        Task<IReadOnlyList<IssuedCertificateDTO>> IssueAsync(IssueCertificatesDTO dto);
        Task<CertificateDTO> GetByIdAsync(Guid id);
        Task<PaginatedList<CertificateDTO>> GetAsync(Guid? contestId, Guid? templateId, Guid? teamId, Guid? studentId, int page, int pageSize, string? sortBy, bool desc);
    }
}
