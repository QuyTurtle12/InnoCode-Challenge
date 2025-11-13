using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Utility.Enums;

namespace Repository.DTOs.RoundDTOs
{
    public class UpdateRoundDTO : BaseRoundDTO
    {
        [Required]
        [EnumDataType(typeof(ProblemTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProblemTypeEnum ProblemType { get; set; }

        public UpdateMcqTestDTO? McqTestConfig { get; set; }

        public UpdateProblemDTO? ProblemConfig { get; set; }
        public int? TimeLimitSeconds { get; set; }
    }
}
