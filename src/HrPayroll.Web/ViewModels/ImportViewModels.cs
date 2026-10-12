namespace HrPayroll.Web.ViewModels;

public class ImportPreviewViewModel
{
    public string Token { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FormatName { get; set; } = string.Empty;
    public bool UsedFallback { get; set; }
    public bool HasMapping { get; set; }
    public List<string> Headers { get; set; } = new();
    public HrPayroll.Infrastructure.Import.ImportAnalysisResult Analysis { get; set; } = new();
}
