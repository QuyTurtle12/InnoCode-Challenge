using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ActivityLogDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class ActivityLogProfile : Profile
    {
        public ActivityLogProfile()
        {
            CreateMap<ActivityLog, ActivityLogDTO>().ReverseMap();

            CreateMap<CreateActivityLogDTO, ActivityLog>()
                .ForMember(d => d.At, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.DeletedAt, o => o.Ignore());
        }
    }
}
