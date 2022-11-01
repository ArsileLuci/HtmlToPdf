using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using HtmlToPdf.Models;

namespace HtmlToPdf.Db
{

    public class ConverterContext: DbContext 
    {
        public DbSet<ConvertedFile> Files { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=127.0.0.1:5432;Database=html_to_pdf;Username=app_user;Password=app_user");
    }
}