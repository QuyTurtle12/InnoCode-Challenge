using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.CertificateDTOs;

namespace BusinessLogic.MappingProfiles.Certificates
{
    public class CertificateProfile : Profile
    {
        public CertificateProfile() 
        {
            // Base mapping
            CreateMap<Certificate, GetCertificateDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team != null ? src.Team.Name : string.Empty))
                .ForMember(dest => dest.TemplateName, opt => opt.MapFrom(src => src.Template != null ? src.Template.Name : string.Empty));

            // Derived mapping for GetMyCertificateDTO
            CreateMap<Certificate, GetMyCertificateDTO>()
                .IncludeBase<Certificate, GetCertificateDTO>()
                .ForMember(dest => dest.CertificateId, opt => opt.MapFrom(src => src.CertificateId));

            CreateMap<CreateCertificateDTO, Certificate>().ReverseMap();
        }
    }
}
