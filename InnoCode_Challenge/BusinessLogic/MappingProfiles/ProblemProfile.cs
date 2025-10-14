using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ProblemDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class ProblemProfile : Profile
    {
        public ProblemProfile()
        {
            CreateMap<CreateProblemDTO, Problem>().ReverseMap();
            CreateMap<UpdateProblemDTO, Problem>().ReverseMap();
            CreateMap<Problem, GetProblemDTO>()
                .ForMember(dest => dest.RoundName, opt => opt.MapFrom(src => src.Round.Name));
        }
    }
}
