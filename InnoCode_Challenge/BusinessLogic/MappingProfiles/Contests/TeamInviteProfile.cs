using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TeamInviteDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class TeamInviteProfile : Profile
    {
        public TeamInviteProfile()
        {
            CreateMap<TeamInvite, TeamInviteDTO>()
                .ForMember(d => d.TeamName, o => o.MapFrom(s => s.Team.Name))
                .ForMember(d => d.ContestId, o => o.MapFrom(s => s.Team.ContestId))
                .ForMember(d => d.ContestName, o => o.MapFrom(s => s.Team.Contest.Name));
        }
    }
}
