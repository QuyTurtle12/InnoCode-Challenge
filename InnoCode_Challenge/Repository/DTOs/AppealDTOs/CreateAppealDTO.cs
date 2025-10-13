using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.AppealDTOs
{
    public class CreateAppealDTO : BaseAppealDTO
    {
        [Required]
        [EnumDataType(typeof(AppealStateEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AppealStateEnum State { get; set; } = AppealStateEnum.Open!;
    }
}
