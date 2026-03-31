using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotExportService
{
    byte[] BuildPlanPackPdf(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel result,
        bool useDarkTheme);

    string BuildChecklistText(AislePilotPlanResultViewModel result);
}
