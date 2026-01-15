namespace Repository.DTOs.SubmissionDetailDTOs
{
    public class GetSubmissionDetailDTO : BaseSubmissionDetailDTO
    {
        public Guid DetailsId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
