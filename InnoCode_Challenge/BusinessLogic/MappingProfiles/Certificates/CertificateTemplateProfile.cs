using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.CertificateTemplateDTOs;

namespace BusinessLogic.MappingProfiles.Certificates
{
    public class CertificateTemplateProfile : Profile
    {
        public CertificateTemplateProfile()
        {
            CreateMap<CertificateTemplate, GetCertificateTemplateDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest.Name));
            CreateMap<CertificateTemplate, CreateCertificateTemplateDTO>().ReverseMap();
            CreateMap<CertificateTemplate, UpdateCertificateTemplateDTO>().ReverseMap();
        }
    }
}
