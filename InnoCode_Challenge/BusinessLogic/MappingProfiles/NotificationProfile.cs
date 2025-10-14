using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.NotificationDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class NotificationProfile : Profile
    {
        public NotificationProfile()
        {
            CreateMap<CreateGeneralNotificationDTO, Notification>().ReverseMap();
            CreateMap<BaseNotificationDTO, Notification>().ReverseMap();
            CreateMap<GetNotificationDTO, Notification>().ReverseMap();
        }
    }
}
