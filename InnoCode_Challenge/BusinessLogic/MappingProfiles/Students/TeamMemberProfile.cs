using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TeamMemberDTOs;

namespace BusinessLogic.MappingProfiles.Students
{
    public class TeamMemberProfile : Profile
    {
        public TeamMemberProfile()
        {
            CreateMap<TeamMember, TeamMemberDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team.Name))
                .ForMember(dest => dest.StudentFullname, opt => opt.MapFrom(src => src.Student.User.Fullname))
                .ForMember(dest => dest.StudentEmail, opt => opt.MapFrom(src => src.Student.User.Email));
        }
    }
}
