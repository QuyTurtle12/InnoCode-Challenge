using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.RoundDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class RoundProfile : Profile
    {
        public RoundProfile() {
            CreateMap<CreateRoundDTO, Round>()
                .ForMember(dest => dest.IsRetakeRound, opt => opt.MapFrom(src => src.IsRetakeRound))
                .ForMember(dest => dest.MainRoundId, opt => opt.MapFrom(src => src.MainRoundId));

            CreateMap<Round, GetRoundDTO>()
                .ForMember(dest => dest.ContestName, opt => opt.MapFrom(src => src.Contest != null ? src.Contest.Name : "N/A"))
                .ForMember(dest => dest.RoundName, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.IsRetakeRound, opt => opt.MapFrom(src => src.IsRetakeRound))
                .ForMember(dest => dest.MainRoundId, opt => opt.MapFrom(src => src.MainRoundId))
                .ForMember(dest => dest.MainRoundName, opt => opt.MapFrom(src => src.MainRound != null ? src.MainRound.Name : null));
            
            CreateMap<UpdateRoundDTO, Round>().ReverseMap();
        }
    }
}
