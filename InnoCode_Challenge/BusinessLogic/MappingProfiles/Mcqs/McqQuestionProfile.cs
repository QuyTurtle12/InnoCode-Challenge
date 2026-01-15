using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqQuestionDTOs;

namespace BusinessLogic.MappingProfiles.Mcqs
{
    public class McqQuestionProfile : Profile
    {
        public McqQuestionProfile()
        {
            CreateMap<McqQuestion, GetMcqQuestionDTO>().ReverseMap();
            CreateMap<McqQuestion, CreateMcqQuestionDTO>().ReverseMap();
            CreateMap<McqQuestion, UpdateMcqQuestionDTO>().ReverseMap();
        }
    }
}
