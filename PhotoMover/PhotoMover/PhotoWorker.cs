using MetadataExtractor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoMover
{
    public class PhotoWorker : BackgroundService
    {
        private readonly ILogger<PhotoWorker> _logger;
        private readonly IConfiguration _config;
        private readonly CustomOutputLocation _customOutputLocationBulder;
        private readonly string _mediaSourceLocation;
        private readonly SHA256 _mediaHash;
        private int totalProcessed = 0;

        public PhotoWorker(ILogger<PhotoWorker> logger, IConfiguration config, CustomOutputLocation builder)
        {
            _logger = logger;
            _config = config;
            _customOutputLocationBulder = builder;
            _mediaSourceLocation = _config["SourceLocation"];
            _mediaHash = SHA256.Create();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

            await ProcessDirectory(_mediaSourceLocation, stoppingToken);

            await Task.Delay(1000, stoppingToken);
        }

        private async Task ProcessDirectory(string dir, CancellationToken ct)
        {
            // Use ParallelOptions instance to store the CancellationToken
            ParallelOptions po = new ParallelOptions();
            po.CancellationToken = ct;
            po.MaxDegreeOfParallelism = 1; // System.Environment.ProcessorCount;

            try
            {
                Parallel.ForEach(System.IO.Directory.EnumerateFiles(dir), po, async (filepath) =>
                {
                    await ProcessFile(filepath);
                    po.CancellationToken.ThrowIfCancellationRequested();
                });
            }
            catch (OperationCanceledException e)
            {
                _logger.LogError($"Exception somewhere: {e.Message}");
            }

            foreach(var subDir in System.IO.Directory.EnumerateDirectories(dir))
            {
                await ProcessDirectory(subDir, ct);
            }
        }

        private async Task ProcessFile(string filepath)
        {
            FileInfo fileInfo = new FileInfo(filepath);

            var destFileLocationPath = _customOutputLocationBulder.BuildOutputPath(fileInfo);
            if (string.IsNullOrEmpty(destFileLocationPath)) return;

            FileInfo destFileInfo = new FileInfo(destFileLocationPath);

            await MoveFile(fileInfo, destFileInfo);
        }

        private async Task MoveFile(FileInfo sourceFI, FileInfo destFI, int iteration = 0)
        {
            // first check if the file already exists
            if (destFI.Exists)
            {
                FileStream fsSource = new FileStream(sourceFI.FullName, FileMode.Open);
                string sourcehash = BitConverter
                    .ToString(_mediaHash.ComputeHash(fsSource));
                FileStream fsDest = new FileStream(destFI.FullName, FileMode.Open);
                string destHash = BitConverter
                    .ToString(_mediaHash.ComputeHash(fsDest));
                fsSource.Close();
                fsDest.Close();

                if (sourcehash.Equals(destHash))
                {
                    // if the hashes are the same, just delete the source file
                    _logger.LogError($"DELETE: duplicate file, {sourceFI.FullName} deleted");
                    // sourceFI.Delete();
                }
                else
                {
                    // if the hashes are different, alter the filename and try again
                    ++iteration;
                    string newName = string.Format("{0}_{1}{2}",
                        Path.GetFileNameWithoutExtension(destFI.FullName), iteration, destFI.Extension);
                    string newPath = Path.Combine(destFI.DirectoryName, newName);
                    FileInfo newFI = new FileInfo(newPath);
                    await MoveFile(sourceFI, newFI, iteration);
                }
            }
            else
            {
                if (!destFI.Directory.Exists)
                {
                    destFI.Directory.Create();
                }
                System.IO.File.Move(sourceFI.FullName, destFI.FullName);
                totalProcessed++;
                _logger.LogInformation($"{totalProcessed}--->Move File to {destFI.FullName}");
            }
        }
    }
}
