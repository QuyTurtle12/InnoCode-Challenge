using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.TestCaseDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class TestCaseProfile : Profile
    {
        public TestCaseProfile() 
        {
            CreateMap<TestCase, GetTestCaseDTO>()
                .ForMember(dest => dest.RoundId, opt => opt.MapFrom(src => src.Problem.Round.RoundId));
            CreateMap<CreateTestCaseDTO, TestCase>().ReverseMap();
            CreateMap<UpdateTestCaseDTO, TestCase>().ReverseMap();
        }
    }
}
