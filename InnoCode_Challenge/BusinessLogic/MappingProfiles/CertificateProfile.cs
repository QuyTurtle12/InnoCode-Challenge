using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.CertificateDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class CertificateProfile : Profile
    {
        public CertificateProfile() 
        {
            CreateMap<Certificate, GetCertificateDTO>()
                .ForMember(dest => dest.CertificateName, opt => opt.MapFrom(src => src.Template.Name))
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team != null ? src.Team.Name : string.Empty));
            CreateMap<CreateCertificateDTO, Certificate>().ReverseMap();
        }
    }
}
