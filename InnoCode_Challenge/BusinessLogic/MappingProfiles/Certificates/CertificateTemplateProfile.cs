using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.CertificateTemplateDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.MappingProfiles.Certificates
{
    public class CertificateTemplateProfile : Profile
    {
        public CertificateTemplateProfile()
        {
            CreateMap<CertificateTemplate, CertificateTemplateDTO>()
            .ForMember(d => d.TemplateId, opt => opt.MapFrom(s => s.TemplateId))
            .ForMember(d => d.ContestId, opt => opt.MapFrom(s => s.ContestId))
            .ForMember(d => d.Name, opt => opt.MapFrom(s => s.Name))
            .ForMember(d => d.FileUrl, opt => opt.MapFrom(s => s.FileUrl));
        }
    }
}