using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.ProblemDTOs
{
    public class CreateProblemDTO : BaseProblemDTO
    {
        [Required]
        [EnumDataType(typeof(ProblemTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProblemTypeEnum Type { get; set; } = ProblemTypeEnum.Manual;

        [EnumDataType(typeof(TestTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TestTypeEnum? TestType { get; set; }

        public IFormFile? TemplateFile { get; set; }

        public double? MockTestWeight { get; set; }
    }
}
