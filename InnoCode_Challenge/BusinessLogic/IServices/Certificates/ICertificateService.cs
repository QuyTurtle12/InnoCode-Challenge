using Repository.DTOs.CertificateDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Certificates
{
    public interface ICertificateService
    {
        Task<PaginatedList<GetAllTeamCertificateDTO>> GetPaginatedCertificateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, Guid? teamIdSearch, Guid? studentIdSearch, string? certificateNameSearch, string? teamName, string? studentNameSearch);
        Task<PaginatedList<GetMyCertificateDTO>> GetMyPaginatedCertificateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? contestNameSearch);
        Task CreateCertificateAsync(CreateCertificateDTO DTO);
        Task DeleteCertificateAsync(Guid id);
        Task AwardCertificateAsync(AwardCertificateDTO dto);
    }
}
