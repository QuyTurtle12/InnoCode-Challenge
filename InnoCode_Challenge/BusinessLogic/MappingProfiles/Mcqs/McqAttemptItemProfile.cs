using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.McqAttemptItemDTOs;

namespace BusinessLogic.MappingProfiles.Mcqs
{
    public class McqAttemptItemProfile : Profile
    {
        public McqAttemptItemProfile() 
        {
            CreateMap<McqAttemptItem, GetMcqAttemptItemDTO>().ReverseMap();
            CreateMap<McqAttemptItem, CreateMcqAttemptItemDTO>().ReverseMap();
        }
    }
}
