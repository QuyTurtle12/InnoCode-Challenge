using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TeamDTOs;


namespace BusinessLogic.MappingProfiles
{
    public class TeamProfile : Profile
    {
        public TeamProfile()
        {
            CreateMap<Team, TeamDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest.Name))
                .ForMember(dest => dest.SchoolName, opt => opt.MapFrom(src => src.School.Name))
                .ForMember(dest => dest.MentorName, opt => opt.MapFrom(src => src.Mentor.User.Fullname));
        }
    }
}
