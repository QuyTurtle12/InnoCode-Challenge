using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.JudgeInviteDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class JudgeInviteProfile : Profile
    {
        public JudgeInviteProfile()
        {
            CreateMap<JudgeInvite, JudgeInviteDTO>()
                .ForMember(dest => dest.JudgeName, opt => opt.MapFrom(src => src.Judge.Fullname))
                .ForMember(dest => dest.JudgeEmail, opt => opt.MapFrom(src => src.Judge.Email))
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest.Name));
        }
    }
}
