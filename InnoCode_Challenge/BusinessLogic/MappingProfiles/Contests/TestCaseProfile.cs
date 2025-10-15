using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TestCaseDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class TestCaseProfile : Profile
    {
        public TestCaseProfile() 
        {
            CreateMap<GetTestCaseDTO, TestCase>().ReverseMap();
            CreateMap<CreateTestCaseDTO, TestCase>().ReverseMap();
            CreateMap<UpdateTestCaseDTO, TestCase>().ReverseMap();
        }
    }
}
