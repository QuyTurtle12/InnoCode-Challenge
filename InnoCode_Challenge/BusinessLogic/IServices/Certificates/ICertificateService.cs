using Repository.DTOs.CertificateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Certificates
{
    public interface ICertificateService
    {
        Task<PaginatedList<GetCertificateDTO>> GetPaginatedCertificateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? teamIdSearch, Guid? studentIdSearch, string? certificateNameSearch, string? teamName, string? studentNameSearch);
        Task CreateCertificateAsync(CreateCertificateDTO DTO);
        Task DeleteCertificateAsync(Guid id);
        Task AwardCertificateAsync(AwardCertificateDTO dto);
    }
}
