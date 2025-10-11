using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ContestDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class ContestProfile : Profile
    {
        public ContestProfile()
        {
            CreateMap<CreateContestDTO, Contest>().ReverseMap();
            CreateMap<UpdateContestDTO, Contest>().ReverseMap();
            CreateMap<Contest, GetContestDTO>().ReverseMap();
        }
    }
}
