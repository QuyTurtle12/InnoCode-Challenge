using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqTestDTOs;

namespace BusinessLogic.MappingProfiles.Mcqs
{
    public class McqTestProfile : Profile
    {
        public McqTestProfile()
        {
            CreateMap<McqTest, GetMcqTestDTO>().ReverseMap();
            CreateMap<McqTest, CreateMcqTestDTO>().ReverseMap();
            CreateMap<McqTest, UpdateMcqTestDTO>().ReverseMap();
        }
    }
}
