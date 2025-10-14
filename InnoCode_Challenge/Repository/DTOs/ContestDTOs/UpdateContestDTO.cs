using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.ContestDTOs
{
    public class UpdateContestDTO : BaseContestDTO
    {
        [EnumDataType(typeof(ContestStatusEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ContestStatusEnum Status { get; set; } = ContestStatusEnum.Draft;
    }
}
