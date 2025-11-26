using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.CertificateDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.MappingProfiles.Certificates
{
    public class CertificateProfile : Profile
    {
        public CertificateProfile()
        {
            CreateMap<Certificate, CertificateDTO>()
                .ForMember(d => d.CertificateId, opt => opt.MapFrom(s => s.CertificateId))
                .ForMember(d => d.TemplateId, opt => opt.MapFrom(s => s.TemplateId))
                .ForMember(d => d.TemplateName, opt => opt.MapFrom(s => s.Template.Name))
                .ForMember(d => d.ContestId, opt => opt.MapFrom(s => s.Template.ContestId))
                .ForMember(d => d.TeamId, opt => opt.MapFrom(s => s.TeamId))
                .ForMember(d => d.TeamName, opt => opt.MapFrom(s => s.Team != null ? s.Team.Name : null))
                .ForMember(d => d.StudentId, opt => opt.MapFrom(s => s.StudentId))
                .ForMember(d => d.StudentName, opt => opt.MapFrom(s => s.Student != null ? s.Student.User.Fullname : null))
                .ForMember(d => d.FileUrl, opt => opt.MapFrom(s => s.FileUrl))
                .ForMember(d => d.IssuedAt, opt => opt.MapFrom(s => s.IssuedAt));
        }
    }
}
