using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.MentorDTOs;

namespace BusinessLogic.MappingProfiles.Mentors
{
    public class MentorProfile : Profile
    {
        public MentorProfile()
        {
            CreateMap<Mentor, MentorDTO>()
                .ForMember(dest => dest.UserFullname, opt => opt.MapFrom(src => src.User.Fullname))
                .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User.Email))
                .ForMember(dest => dest.SchoolName, opt => opt.MapFrom(src => src.School.Name));
        }
    }
}
