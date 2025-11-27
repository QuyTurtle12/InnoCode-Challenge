using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.AppealDTOs;
using Repository.DTOs.AppealEvidenceDTOs;

namespace BusinessLogic.MappingProfiles.Appeals
{
    public class AppealProfile : Profile
    {
        public AppealProfile()
        {
            CreateMap<Appeal, GetAppealDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team.Name))
                .ForMember(dest => dest.OwnerName, opt => opt.MapFrom(src => src.Owner.Fullname))
                .ForMember(dest => dest.RoundId, opt => opt.MapFrom(src => src.TargetId))
                .ForMember(dest => dest.RoundName, opt => opt.MapFrom(src => src.Target.Name))
                .ForMember(dest => dest.Evidences, opt => opt.MapFrom(src => src.AppealEvidences));

            CreateMap<AppealEvidence, AppealEvidenceDTO>();
        }
    }
}
