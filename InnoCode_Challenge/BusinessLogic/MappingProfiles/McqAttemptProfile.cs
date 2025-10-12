using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqAttemptDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class McqAttemptProfile : Profile
    {
        public McqAttemptProfile() 
        {
            CreateMap<McqAttempt, GetMcqAttemptDTO>().ReverseMap();
            CreateMap<McqAttempt, CreateMcqAttemptDTO>().ReverseMap();
        }
    }
}
