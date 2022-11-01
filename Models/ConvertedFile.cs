namespace HtmlToPdf.Models
{
    [Microsoft.EntityFrameworkCore.Index(nameof(OriginHash))]
    public class ConvertedFile
    {
        public Guid Id { get; set; }
        public string OriginHash { get; set; }
        public byte[]? Data { get; set; }

        public byte[] Origin { get; set; }

        public ConvertedFile(byte[] origin, string hash)
        {
            Id = new Guid();
            Origin = origin;
            OriginHash = hash;
        }

        public ConvertedFile(Guid Id, string OriginHash, byte[]? Data, byte[] Origin) {
            this.Id = Id;
            this.OriginHash = OriginHash;
            this.Data = Data;
            this.Origin = Origin;
        }
    }
}