using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.TestCaseDTOs
{
    public class UpdateTestCaseDTO : BaseTestCaseDTO
    {
        [EnumDataType(typeof(TestCaseTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TestCaseTypeEnum Type { get; set; } = TestCaseTypeEnum.TestCase;
    }
}
