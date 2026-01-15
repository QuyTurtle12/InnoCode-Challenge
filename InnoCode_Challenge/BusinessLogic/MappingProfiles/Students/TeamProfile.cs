using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TeamDTOs;
using Repository.DTOs.TeamMemberDTOs;
using Utility.Enums;


namespace BusinessLogic.MappingProfiles.Students
{
    public class TeamProfile : Profile
    {
        public TeamProfile()
        {
            // Team -> TeamDTO
            CreateMap<Team, TeamDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest != null ? src.Contest.Name : "N/A"))
                .ForMember(dest => dest.SchoolName, opt => opt.MapFrom(src => src.School != null ? src.School.Name : "N/A"))
                .ForMember(dest => dest.MentorName, opt => opt.MapFrom(src => src.Mentor != null && src.Mentor.User != null ? src.Mentor.User.Fullname : "N/A"));

            // Team -> TeamWithMembersDTO
            CreateMap<Team, TeamWithMembersDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest != null ? src.Contest.Name : "N/A"))
                .ForMember(dest => dest.SchoolName, opt => opt.MapFrom(src => src.School != null ? src.School.Name : "N/A"))
                .ForMember(dest => dest.MentorName, opt => opt.MapFrom(src => src.Mentor != null && src.Mentor.User != null ? src.Mentor.User.Fullname : "N/A"))
                .ForMember(dest => dest.Members, opt => opt.MapFrom(src => src.TeamMembers));

            // TeamMember -> TeamMemberDTO
            CreateMap<TeamMember, TeamMemberDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team != null ? src.Team.Name : "N/A"))
                .ForMember(dest => dest.StudentFullname, opt => opt.MapFrom(src => src.Student != null && src.Student.User != null ? src.Student.User.Fullname : "N/A"))
                .ForMember(dest => dest.StudentEmail, opt => opt.MapFrom(src => src.Student != null && src.Student.User != null ? src.Student.User.Email : "N/A"))
                .ForMember(dest => dest.MemberRole, opt => opt.MapFrom(src =>
                    src.MemberRole != null && src.MemberRole.Equals(MemberRoleEnum.Member.ToString())
                        ? MemberRoleEnum.Member
                        : MemberRoleEnum.Leader));
        }
    }
}
