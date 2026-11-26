using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CryoUtils.Models;

namespace CryoUtils.Services
{
    [Flags]
    public enum WorkflowAction
    {
        None = 0,
        Save = 1,
        CopyImage = 2,
        OcrCopy = 4,
        Upload = 8,
        Resize = 16
    }

    public sealed class WorkflowRecipe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string AppFilter { get; set; } = "";
        public WorkflowAction Actions { get; set; } = WorkflowAction.Save;
        public int ResizePercent { get; set; } = 100;
        public string FileNameTemplate { get; set; } = "CryoUtils_{date}_{time}";

        public string ActionDisplay
        {
            get
            {
                var names = new List<string>();
                if (Actions.HasFlag(WorkflowAction.Resize)) names.Add(LocalizationService.Get("Workflow_Resize"));
                if (Actions.HasFlag(WorkflowAction.OcrCopy)) names.Add(LocalizationService.Get("Workflow_OcrCopy"));
                if (Actions.HasFlag(WorkflowAction.CopyImage)) names.Add(LocalizationService.Get("Workflow_CopyImage"));
                if (Actions.HasFlag(WorkflowAction.Save)) names.Add(LocalizationService.Get("Workflow_Save"));
                if (Actions.HasFlag(WorkflowAction.Upload)) names.Add(LocalizationService.Get("Workflow_Upload"));
                return string.Join(" → ", names);
            }
        }

        public string ScopeDisplay => string.IsNullOrWhiteSpace(AppFilter)
            ? LocalizationService.Get("Workflow_AllApps")
            : AppFilter;
    }

    public sealed class WorkflowRunRecord
    {
        public string RecipeName { get; init; } = "";
        public string SourceApp { get; init; } = "";
        public DateTime StartedAt { get; init; } = DateTime.Now;
        public bool Success { get; init; }
        public string Detail { get; init; } = "";

        public string StatusDisplay => Success
            ? LocalizationService.Get("Workbench_RunOk")
            : LocalizationService.Get("Workbench_RunFailed");

        public string DisplayLine => $"{RecipeName} · {StatusDisplay}" +
            (string.IsNullOrWhiteSpace(Detail) ? "" : $" · {Detail}");
    }

    public sealed class WorkflowService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string RecipesFile = Path.Combine(DataFolder, "workflows.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static WorkflowService? _instance;
        public static WorkflowService Instance => _instance ??= new WorkflowService();

        private readonly object _lock = new();
        private List<WorkflowRecipe> _recipes;
        private bool _running;

        public event Action? Changed;
        public event Action? RunsChanged;
        public ObservableCollection<WorkflowRunRecord> Runs { get; } = new();

        private WorkflowService()
        {
            _recipes = Load();
        }

        public IReadOnlyList<WorkflowRecipe> Recipes
        {
            get
            {
                lock (_lock)
                {
                    return _recipes.ToList();
                }
            }
        }

        public void SaveRecipe(WorkflowRecipe recipe)
        {
            lock (_lock)
            {
                var existing = _recipes.FirstOrDefault(item => item.Id == recipe.Id);
                if (existing != null)
                {
                    _recipes.Remove(existing);
                }
                _recipes.Add(recipe);
                Save();
            }
            Changed?.Invoke();
        }

        public void DeleteRecipe(string id)
        {
            lock (_lock)
            {
                _recipes.RemoveAll(recipe => recipe.Id == id);
                Save();
            }
            Changed?.Invoke();
        }

        public async Task RunPostCaptureAsync(
            BitmapSource bitmap,
            string sourceApp,
            string sourceWindow,
            bool skipSaveAction = false)
        {
            if (!SettingsService.Instance.Current.WorkflowsEnabled || _running)
            {
                return;
            }

            List<WorkflowRecipe> matches;
            lock (_lock)
            {
                matches = _recipes
                    .Where(recipe => recipe.Enabled && Matches(recipe, sourceApp, sourceWindow))
                    .ToList();
            }

            if (matches.Count == 0)
            {
                return;
            }

            _running = true;
            try
            {
                foreach (var recipe in matches)
                {
                    await ExecuteAsync(bitmap, recipe, sourceApp, sourceWindow, skipSaveAction);
                }
            }
            finally
            {
                _running = false;
            }
        }

        private static bool Matches(WorkflowRecipe recipe, string sourceApp, string sourceWindow)
        {
            string filter = recipe.AppFilter?.Trim() ?? "";
            if (filter.Length == 0)
            {
                return true;
            }

            return filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Any(value =>
                    sourceApp.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                    sourceWindow.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        }

        private async Task ExecuteAsync(
            BitmapSource original,
            WorkflowRecipe recipe,
            string sourceApp,
            string sourceWindow,
            bool skipSaveAction)
        {
            var app = Application.Current as App;
            BitmapSource bitmap = original;
            try
            {
                if (recipe.Actions.HasFlag(WorkflowAction.Resize))
                {
                    double scale = Math.Clamp(recipe.ResizePercent, 10, 100) / 100.0;
                    if (scale < 0.999)
                    {
                        bitmap = ImageIO.Resize(bitmap, scale);
                    }
                }

                if (recipe.Actions.HasFlag(WorkflowAction.OcrCopy))
                {
                    string text = await OcrService.RecognizeAsync(bitmap);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        TryCopyText(text);
                        app?.ShowToast(LocalizationService.Format("Workflow_OcrDone", recipe.Name));
                    }
                }

                if (recipe.Actions.HasFlag(WorkflowAction.CopyImage))
                {
                    try
                    {
                        Clipboard.SetImage(bitmap);
                    }
                    catch
                    {
                    }
                }

                if (recipe.Actions.HasFlag(WorkflowAction.Save) && !skipSaveAction)
                {
                    string path = BuildOutputPath(recipe, sourceApp, sourceWindow);
                    ImageIO.SaveBitmap(bitmap, path);
                    CaptureLibraryService.Instance.Record(
                        path,
                        sourceApp,
                        sourceWindow,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight);
                    RecentCaptureService.Instance.Add(
                        bitmap,
                        path,
                        LocalizationService.Get("Workflow_Title"));
                }

                if (recipe.Actions.HasFlag(WorkflowAction.Upload))
                {
                    if (!UploadConsentService.EnsureUploadAllowed())
                    {
                        return;
                    }

                    string url = await UploadProviderService.UploadImageAsync(bitmap);
                    TryCopyText(url);
                    RecentCaptureService.Instance.Add(
                        bitmap,
                        null,
                        $"{LocalizationService.Get("Workflow_Upload")}: {url}");
                    app?.ShowToast(LocalizationService.Format("Workflow_UploadDone", url));
                }

                RecordRun(new WorkflowRunRecord
                {
                    RecipeName = recipe.Name,
                    SourceApp = sourceApp,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                DiagnosticsService.Log($"Workflow '{recipe.Name}' failed", ex);
                app?.ShowToast(LocalizationService.Format("Workflow_Failed", recipe.Name, ex.Message));
                RecordRun(new WorkflowRunRecord
                {
                    RecipeName = recipe.Name,
                    SourceApp = sourceApp,
                    Success = false,
                    Detail = ex.Message
                });
            }
        }

        private void RecordRun(WorkflowRunRecord record)
        {
            void Add()
            {
                Runs.Insert(0, record);
                while (Runs.Count > 20)
                {
                    Runs.RemoveAt(Runs.Count - 1);
                }
                RunsChanged?.Invoke();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Add();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(Add));
            }
        }

        private static string BuildOutputPath(
            WorkflowRecipe recipe,
            string sourceApp,
            string sourceWindow)
        {
            string extension = ImageIO.ExtensionForFormat(SettingsService.Instance.Current.ImageFormat);
            string template = string.IsNullOrWhiteSpace(recipe.FileNameTemplate)
                ? "CryoUtils_{date}_{time}"
                : recipe.FileNameTemplate;
            string safeApp = Sanitize(sourceApp);
            string safeTitle = Sanitize(sourceWindow);
            string name = template
                .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", DateTime.Now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
                .Replace("{app}", safeApp, StringComparison.OrdinalIgnoreCase)
                .Replace("{title}", safeTitle, StringComparison.OrdinalIgnoreCase)
                .Replace("{name}", "CryoUtils", StringComparison.OrdinalIgnoreCase);
            name = Sanitize(name);
            if (name.Length == 0)
            {
                name = "CryoUtils";
            }

            string folder = SettingsService.Instance.EffectiveSaveFolder;
            Directory.CreateDirectory(folder);
            string candidate = Path.Combine(folder, name + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            for (int index = 2; index < 1000; index++)
            {
                candidate = Path.Combine(folder, $"{name}_{index}{extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(folder, $"CryoUtils_{Guid.NewGuid():N}{extension}");
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string safe = string.Join("_", value.Split(Path.GetInvalidFileNameChars()));
            safe = safe.Trim().Trim('.');
            return safe.Length > 48 ? safe[..48] : safe;
        }

        private static void TryCopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
            }
        }

        private static List<WorkflowRecipe> Load()
        {
            try
            {
                if (File.Exists(RecipesFile))
                {
                    string json = File.ReadAllText(RecipesFile);
                    var loaded = JsonSerializer.Deserialize<List<WorkflowRecipe>>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
            }
            return new List<WorkflowRecipe>();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                string json = JsonSerializer.Serialize(_recipes, JsonOptions);
                string tempPath = RecipesFile + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, RecipesFile, overwrite: true);
            }
            catch
            {
            }
        }
    }
}
