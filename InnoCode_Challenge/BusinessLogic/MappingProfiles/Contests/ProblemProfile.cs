using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ProblemDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class ProblemProfile : Profile
    {
        public ProblemProfile()
        {
            CreateMap<CreateProblemDTO, Problem>().ReverseMap();
            CreateMap<UpdateProblemDTO, Problem>().ReverseMap();
            CreateMap<Problem, GetProblemDTO>().ReverseMap();
        }
    }
}
