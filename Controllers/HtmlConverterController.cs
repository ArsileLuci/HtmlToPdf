using Microsoft.AspNetCore.Mvc;

using HtmlToPdf.Db;
using HtmlToPdf.Services;
using HtmlToPdf.Models;

namespace HtmlToPdf.Controllers;

[ApiController]
[Route("HtmlConverter")]
public class HtmlConverterController : ControllerBase
{
    IBackgroundConvertTaskQueue bgQueue { get; }

    IServiceProvider provider { get; }
    ConverterContext dbContext { get; }

    public HtmlConverterController(IBackgroundConvertTaskQueue bgQueue,
         ConverterContext dbContext,
         IServiceProvider provider)
    {
        this.bgQueue = bgQueue;
        this.provider = provider;
        this.dbContext = dbContext;
    }

    private static string GetDocHash(byte[] data)
    {
        using (var sha1 = System.Security.Cryptography.SHA1.Create())
        {
            return string.Concat(sha1.ComputeHash(data).Select(x => x.ToString("X2")));
        }
    }

    [HttpGet]
    public async Task<ActionResult> Index() {
        var file = await System.IO.File.ReadAllTextAsync("interface.html");
        return this.Content(file, "text/html");
    }

    [HttpPost("/{controller}/Convert")]
    public async Task<ActionResult> Convert(IFormFile uploadData)
    {
        using var stream = uploadData.OpenReadStream();
        var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        var bytes = System.Text.Encoding.Unicode.GetBytes(text);

        // Check whether we already converted this file so conversion can be
        // avoided.
        var hash = GetDocHash(bytes);
        var existingEntity = this.dbContext.Files.Where(x => x.OriginHash == hash)
            .ToList()
            .Where(x => x.Origin.SequenceEqual(bytes))
            .SingleOrDefault();

        if (existingEntity != null)
            return Ok(new { id = existingEntity.Id });

        var entity = new ConvertedFile(bytes, hash);

        await dbContext.Files.AddAsync(entity);
        await dbContext.SaveChangesAsync();

        var task = new ConvertFileTask(entity.Id, this.provider.CreateScope());

        await bgQueue.QueueBackgroundWorkItemAsync(task);

        return Ok(new { id = entity.Id });
    }

    [HttpGet("/{controller}/TryGetConvertedFile/{id:guid}")]
    public async Task<ActionResult> TryGetConvertedFile(Guid id)
    {
        var file = this.dbContext.Files.Where(x => x.Id == id).SingleOrDefault();

        if (file == null)
        {
            return NotFound();
        }

        // If `file.Data` is null this means that either the Task is not yet
        // completed or has been dropped due to server issues so we're
        // trying to restart it.
        // It's safe to do this and won't cause any significant overheads
        // because before converting we're checking that it's necessary.
        if (file.Data == null)
        {
            var task = new ConvertFileTask(file.Id, this.provider.CreateScope());

            await bgQueue.QueueBackgroundWorkItemAsync(task);

            return Ok();
        }

        return File(file.Data, "application/pdf", "result.pdf");
    }

    [HttpGet("/{controller}/ConversionStatus/{id:guid}")]
    public async Task<ActionResult> ConversionStatus(Guid id)
    {
        FileConvertStatus? status = await this.bgQueue.CheckItemState(id);
        if (status == null)
        {
            var file = dbContext.Files.Where(x => x.Id == id).SingleOrDefault();
            if (file == null)
            {
                return NotFound();
            }

            // If `file.Data` is null this means that either the Task is not yet
            // completed or has been dropped due to server issues so we're
            // trying to restart it.
            // It's safe to do this and won't cause any significant overheads
            // because before converting we're checking that it's necessary.
            if (file.Data == null)
            {
                var task = new ConvertFileTask(file.Id, this.provider.CreateScope());

                await bgQueue.QueueBackgroundWorkItemAsync(task);

                return Ok(new { status = FileConvertStatus.Queued.ToString() });
            }
            else
                return Ok(new { status = FileConvertStatus.Completed.ToString() });
        }

        return Ok(new { status = status.ToString() });
    }
}
