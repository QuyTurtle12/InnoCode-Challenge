using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqTestQuestionDTOs;

namespace BusinessLogic.MappingProfiles.Mcqs
{
    public class McqTestQuestionProfile : Profile
    {
        public McqTestQuestionProfile()
        {
            CreateMap<GetMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
            CreateMap<CreateMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
            CreateMap<UpdateMcqTestQuestionDTO, McqTestQuestion>().ReverseMap();
        }
    }
}
