using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqOptionDTOs;

namespace BusinessLogic.MappingProfiles.Mcqs
{
    public class McqOptionProfile : Profile
    {
        public McqOptionProfile()
        {
            CreateMap<McqOption, GetMcqOptionDTO>().ReverseMap();
            CreateMap<McqOption, CreateMcqOptionDTO>().ReverseMap();
            CreateMap<McqOption, UpdateMcqOptionDTO>().ReverseMap();
        }
    }
}
