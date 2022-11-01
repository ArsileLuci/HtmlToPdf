using System.Threading.Channels;

using PuppeteerSharp;

using HtmlToPdf.Db;

namespace HtmlToPdf.Services
{
    public class ConvertFileTask
    {
        public Guid Id;
        IServiceScope scope;

        public ConvertFileTask(Guid id, IServiceScope scope)
        {
            this.Id = id;
            this.scope = scope;
        }

        /// <summary>
        /// Executes this `ConvertFileTask` in asynchronous manner.
        /// </summary>
        public async ValueTask Handle(CancellationToken token)
        {
            var dbContext = this.scope.ServiceProvider.GetService<ConverterContext>();
            var logger = scope.ServiceProvider.GetService<ILogger<ConvertFileTask>>();

            var entity = dbContext?.Files
                .Where(x => x.Id == this.Id)
                .SingleOrDefault();

            // We either already deleted this file or already processed it.
            // So we just skip it.
            if (entity == null || entity.Data != null)
            {
                logger?.LogInformation("Entity was not found or already processed skipping");
                return;
            }

            try
            {
                logger?.LogInformation("Starting converting HTML file to PDF");
                using var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true
                });
                using var page = await browser.NewPageAsync();
                var text = System.Text.Encoding.Unicode.GetString(entity.Origin);
                await page.SetContentAsync(text);
                var converted = await page.PdfDataAsync();

                entity.Data = converted;
            }
            catch(Exception e)
            {
                logger?.LogWarning(e, "Failed to convert Html to Pdf Database Id: {0}", entity.Id);
                dbContext.Files.Remove(entity);
            }
            finally
            {
                await dbContext.SaveChangesAsync();
                await this.scope.ServiceProvider.GetService<IBackgroundConvertTaskQueue>()
                    .NotifyCompletedAsync(this.Id, token);
                this.scope.Dispose();
            }
        }
    }

    public enum FileConvertStatus
    {
        Queued,
        Executing,
        Completed,
    }

    /// <summary>
    /// Service responsible for conversion of Html Files to Pdf Files.
    /// </summary>
    public class ConverterService : BackgroundService
    {
        private readonly ILogger<ConverterService> _logger;

        private int _poolSize { get; }

        public ConverterService(IBackgroundConvertTaskQueue taskQueue, 
            ILogger<ConverterService> logger)
        {
            TaskQueue = taskQueue;
            _logger = logger;
            _poolSize = 16;
        }

        public IBackgroundConvertTaskQueue TaskQueue { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var pool = new Task[this._poolSize];
            for (int i = 0; i < this._poolSize; i++)
            {
                // Start multiple asyncronous workers that will pull tasks from
                // queue and execute them.
                pool[i] = this.RunConverterThread(stoppingToken, i);
            }

            // Waitin for all of sub tasks to finish.
            await Task.WhenAll(pool);
        }

        
        /// <summary>
        /// Represents single thread that responsible dor execution of converter tasks.
        /// </summary>
        private async Task RunConverterThread(CancellationToken stoppingToken, int workerId)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await this.TaskQueue.DequeueAsync(stoppingToken);

                try
                {
                    this._logger.LogInformation("Executing new task on Worker({0})", workerId);
                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing {WorkItem}.", nameof(workItem));
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }

    public interface IBackgroundConvertTaskQueue
    {
        ValueTask QueueBackgroundWorkItemAsync(ConvertFileTask workItem);

        ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
            CancellationToken cancellationToken);

        ValueTask NotifyCompletedAsync(Guid id, CancellationToken cancellationToken);

        ValueTask<FileConvertStatus?> CheckItemState(Guid id);
    }

    public class BackgroundTaskQueue : IBackgroundConvertTaskQueue
    {
        private readonly Channel<ConvertFileTask> _queue;

        private SemaphoreSlim _locker;
        private Dictionary<Guid, FileConvertStatus> _queueState;

        public BackgroundTaskQueue(int capacity)
        {
            // Capacity should be set based on the expected application load and
            // number of concurrent threads accessing the queue.            
            // BoundedChannelFullMode.Wait will cause calls to WriteAsync() to return a task,
            // which completes only when space became available. This leads to backpressure,
            // in case too many publishers/calls start accumulating.
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<ConvertFileTask>(options);
            _locker = new SemaphoreSlim(1, 1);
            _queueState = new Dictionary<Guid, FileConvertStatus>();
        }

        public async ValueTask QueueBackgroundWorkItemAsync(ConvertFileTask workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            await this._locker.WaitAsync();
            // If the given item already present in the queue then we're
            // ignoring it.
            if (this._queueState.ContainsKey(workItem.Id))
            {
                this._locker.Release();
                return;
            }
            else
            {
                this._queueState.Add(workItem.Id, FileConvertStatus.Queued);
                this._locker.Release();
            }

            await _queue.Writer.WriteAsync(workItem);
        }

        public async ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
            CancellationToken cancellationToken)
        {
            var workItem = await _queue.Reader.ReadAsync(cancellationToken);

            await this._locker.WaitAsync();
            this._queueState[workItem.Id] = FileConvertStatus.Executing;
            this._locker.Release();

            return workItem.Handle;
        }

        public async ValueTask NotifyCompletedAsync(Guid id, CancellationToken stoppingToken)
        {
            await this._locker.WaitAsync();
            this._queueState.Remove(id);
            this._locker.Release();

            return;
        }

        public async ValueTask<FileConvertStatus?> CheckItemState(Guid id)
        {
            await this._locker.WaitAsync();
            FileConvertStatus? status;
            if (this._queueState.TryGetValue(id, out var s))
                status = s;
            else
                status = null;
            this._locker.Release();

            return status;
        }
    }
}