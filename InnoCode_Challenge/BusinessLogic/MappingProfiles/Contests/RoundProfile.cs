using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.RoundDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class RoundProfile : Profile
    {
        public RoundProfile() {
            CreateMap<GetRoundDTO, Round>().ReverseMap();
            CreateMap<CreateRoundDTO, Round>().ReverseMap();
            CreateMap<UpdateRoundDTO, Round>().ReverseMap();
        }
    }
}
