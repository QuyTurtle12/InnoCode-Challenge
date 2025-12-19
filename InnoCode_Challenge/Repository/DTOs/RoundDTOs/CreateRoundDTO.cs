using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Utility.Enums;

namespace Repository.DTOs.RoundDTOs
{
    public class CreateRoundDTO : BaseRoundDTO
    {
        public bool IsRetakeRound { get; set; } = false;

        public Guid? MainRoundId { get; set; }

        [Required]
        [EnumDataType(typeof(ProblemTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProblemTypeEnum ProblemType { get; set; }

        public CreateMcqTestDTO? McqTestConfig { get; set; }

        public CreateProblemDTO? ProblemConfig { get; set; }
    }
}
