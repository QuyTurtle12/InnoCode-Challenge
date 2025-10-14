using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqTestQuestionDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class TestQuestionProfile : Profile
    {
        public TestQuestionProfile()
        {
            CreateMap<GetMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
            CreateMap<CreateMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
            CreateMap<UpdateMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
        }
    }
}
