using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.AppealDTOs;

namespace BusinessLogic.MappingProfiles.Appeals
{
    public class AppealProfile : Profile
    {
        public AppealProfile() 
        {
            CreateMap<CreateAppealDTO, Appeal>().ReverseMap();
            CreateMap<UpdateAppealDTO, Appeal>().ReverseMap();
            CreateMap<Appeal, GetAppealDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team.Name))
                .ForMember(dest => dest.OwnerName, opt => opt.MapFrom(src => src.Owner.Fullname));
        }
    }
}
