using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.LeaderboardEntryDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class LeaderboardEntryProfile : Profile
    {
        public LeaderboardEntryProfile()
        {
            CreateMap<LeaderboardEntry, GetLeaderboardEntryDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest.Name));
            CreateMap<CreateLeaderboardEntryDTO, LeaderboardEntry>().ReverseMap();
            CreateMap<UpdateLeaderboardEntryDTO, LeaderboardEntry>().ReverseMap();
        }
    }
}
