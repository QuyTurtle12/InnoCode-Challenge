using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.MentorRegistrationDTOs;

namespace BusinessLogic.MappingProfiles.Mentors
{
    public class MentorRegistrationProfile : Profile
    {
        public MentorRegistrationProfile()
        {
            CreateMap<MentorRegistration, MentorRegistrationDTO>()
                .ForMember(d => d.RegistrationId, o => o.MapFrom(s => s.RegistrationId));
        }
    }
}
