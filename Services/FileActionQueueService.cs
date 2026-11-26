using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services
{
    public enum FileActionKind
    {
        Convert,
        Compress,
        Recycle
    }

    public enum OverwritePolicy
    {
        Skip,
        Overwrite,
        Rename
    }

    public enum FileActionStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled,
        Preview
    }

    public sealed class FileActionJob : INotifyPropertyChanged
    {
        private FileActionStatus _status;
        private int _progress;
        private string _message = "";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public List<string> InputPaths { get; set; } = new();
        public FileActionKind Kind { get; set; }
        public string TargetFormat { get; set; } = "PNG";
        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;
        public bool DryRun { get; set; }
        public List<string> OutputPaths { get; } = new();

        public FileActionStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusDisplay));
                    OnPropertyChanged(nameof(DisplayLine));
                }
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChanged(nameof(Message));
                    OnPropertyChanged(nameof(DisplayLine));
                }
            }
        }

        public string DisplayName => InputPaths.Count == 1
            ? Path.GetFileName(InputPaths[0])
            : LocalizationService.Format("FileActions_FilesFmt", InputPaths.Count);

        public string OperationDisplay => Kind switch
        {
            FileActionKind.Convert => LocalizationService.Format("FileActions_ConvertFmt", TargetFormat),
            FileActionKind.Compress => LocalizationService.Get("FileActions_Compress"),
            _ => LocalizationService.Get("FileActions_Recycle")
        };

        public string DisplayLine => $"{DisplayName}\n{OperationDisplay} · {StatusDisplay}" +
            (string.IsNullOrWhiteSpace(Message) ? "" : $"\n{Message}");

        public string StatusDisplay => Status switch
        {
            FileActionStatus.Pending => LocalizationService.Get("FileActions_Pending"),
            FileActionStatus.Running => LocalizationService.Get("FileActions_Running"),
            FileActionStatus.Completed => LocalizationService.Get("FileActions_Completed"),
            FileActionStatus.Failed => LocalizationService.Get("FileActions_Failed"),
            FileActionStatus.Cancelled => LocalizationService.Get("FileActions_Cancelled"),
            _ => LocalizationService.Get("FileActions_Preview")
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class FileActionQueueService
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".jfif"
        };

        private static FileActionQueueService? _instance;
        public static FileActionQueueService Instance => _instance ??= new FileActionQueueService();

        private CancellationTokenSource? _cts;
        private bool _running;

        public ObservableCollection<FileActionJob> Jobs { get; } = new();
        public bool IsRunning => _running;
        public event Action? Changed;

        private FileActionQueueService()
        {
        }

        public FileActionJob Add(
            IEnumerable<string> paths,
            FileActionKind kind,
            string targetFormat,
            OverwritePolicy overwritePolicy,
            bool dryRun)
        {
            var files = paths
                .Where(File.Exists)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var job = new FileActionJob
            {
                InputPaths = files,
                Kind = kind,
                TargetFormat = targetFormat,
                OverwritePolicy = overwritePolicy,
                DryRun = dryRun,
                Status = dryRun ? FileActionStatus.Preview : FileActionStatus.Pending
            };
            if (files.Count == 0)
            {
                job.Status = FileActionStatus.Failed;
                job.Message = LocalizationService.Get("FileActions_NoImages");
            }
            Jobs.Add(job);
            Changed?.Invoke();
            return job;
        }

        public void Remove(FileActionJob job)
        {
            if (!_running)
            {
                Jobs.Remove(job);
                Changed?.Invoke();
            }
        }

        public void ClearCompleted()
        {
            foreach (var job in Jobs
                         .Where(job => job.Status is FileActionStatus.Completed or FileActionStatus.Cancelled or FileActionStatus.Failed or FileActionStatus.Preview)
                         .ToList())
            {
                Jobs.Remove(job);
            }
            Changed?.Invoke();
        }

        public async Task RunAsync()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _cts = new CancellationTokenSource();
            try
            {
                foreach (var job in Jobs.Where(job =>
                             job.Status is FileActionStatus.Pending or FileActionStatus.Failed ||
                             (job.DryRun && job.Status == FileActionStatus.Preview && job.OutputPaths.Count == 0))
                         .ToList())
                {
                    if (_cts.IsCancellationRequested)
                    {
                        break;
                    }
                    await RunJobAsync(job, _cts.Token);
                }
            }
            finally
            {
                _running = false;
                _cts.Dispose();
                _cts = null;
                Changed?.Invoke();
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
        }

        public async Task RetryAsync(FileActionJob job)
        {
            if (_running)
            {
                return;
            }

            job.Status = job.DryRun ? FileActionStatus.Preview : FileActionStatus.Pending;
            job.Progress = 0;
            job.Message = "";
            _running = true;
            _cts = new CancellationTokenSource();
            try
            {
                await RunJobAsync(job, _cts.Token);
            }
            finally
            {
                _running = false;
                _cts.Dispose();
                _cts = null;
                Changed?.Invoke();
            }
        }

        private static async Task RunJobAsync(FileActionJob job, CancellationToken cancellationToken)
        {
            if (job.InputPaths.Count == 0)
            {
                job.Status = FileActionStatus.Failed;
                job.Message = LocalizationService.Get("FileActions_NoImages");
                return;
            }

            job.Status = FileActionStatus.Running;
            job.Progress = 0;
            job.OutputPaths.Clear();
            int completed = 0;
            try
            {
                for (int index = 0; index < job.InputPaths.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string source = job.InputPaths[index];
                    string? output = BuildOutputPath(source, job, index);
                    if (output == null)
                    {
                        completed++;
                        job.Progress = (int)Math.Round(completed * 100.0 / job.InputPaths.Count);
                        continue;
                    }

                    job.OutputPaths.Add(output);
                    if (!job.DryRun)
                    {
                        if (File.Exists(output))
                        {
                            if (job.OverwritePolicy == OverwritePolicy.Skip)
                            {
                                completed++;
                                job.Progress = (int)Math.Round(completed * 100.0 / job.InputPaths.Count);
                                continue;
                            }
                            if (job.OverwritePolicy == OverwritePolicy.Rename)
                            {
                                output = UniqueOutputPath(output);
                                job.OutputPaths[^1] = output;
                            }
                            else
                            {
                                File.Delete(output);
                            }
                        }

                        switch (job.Kind)
                        {
                            case FileActionKind.Convert:
                                await Task.Run(
                                    () => ImageProcessingService.ConvertFormat(source, output, job.TargetFormat),
                                    cancellationToken);
                                break;
                            case FileActionKind.Compress:
                                Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
                                var result = await CompressionService.CompressAsync(
                                    source,
                                    SettingsService.Instance.Current.CompressorPreset,
                                    SettingsService.Instance.Current.CompressorQuality,
                                    SettingsService.Instance.Current.CompressorScale);
                                await File.WriteAllBytesAsync(output, result.Data, cancellationToken);
                                break;
                            case FileActionKind.Recycle:
                                if (!NativeMethods.MoveToRecycleBin(source))
                                {
                                    throw new IOException(LocalizationService.Get("FileActions_RecycleFailed"));
                                }
                                CaptureLibraryService.Instance.DeleteToRecycleBin(new[] { source });
                                break;
                        }
                    }

                    completed++;
                    job.Progress = (int)Math.Round(completed * 100.0 / job.InputPaths.Count);
                }

                job.Status = job.DryRun ? FileActionStatus.Preview : FileActionStatus.Completed;
                job.Message = job.DryRun
                    ? LocalizationService.Format("FileActions_DryRunDone", job.OutputPaths.Count)
                    : LocalizationService.Format("FileActions_DoneFmt", completed);
            }
            catch (OperationCanceledException)
            {
                job.Status = FileActionStatus.Cancelled;
                job.Message = LocalizationService.Get("FileActions_Cancelled");
            }
            catch (Exception ex)
            {
                job.Status = FileActionStatus.Failed;
                job.Message = ex.Message;
                DiagnosticsService.Log("File action failed", ex);
            }
        }

        private static string? BuildOutputPath(string source, FileActionJob job, int index)
        {
            if (job.Kind == FileActionKind.Recycle)
            {
                return source;
            }

            string directory = Path.GetDirectoryName(source) ?? SettingsService.Instance.EffectiveSaveFolder;
            string name = Path.GetFileNameWithoutExtension(source);
            if (job.Kind == FileActionKind.Compress)
            {
                directory = Path.Combine(directory, "_compressed");
                string extension = Path.GetExtension(source);
                return Path.Combine(directory, name + extension);
            }

            string target = ImageIO.ExtensionForFormat(job.TargetFormat);
            return Path.Combine(directory, $"{name}_converted{target}");
        }

        private static string UniqueOutputPath(string path)
            => FileToolsService.CreateUniquePath(path);
    }
}
