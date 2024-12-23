using Microsoft.UI.Xaml.Controls;

using GalgameManager.ViewModels;    


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GalgameManager.Views;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AnnualReportSubPage2 : Page
{   
    public ReportSubPage2ViewModel ViewModel
    {
        get;
    }

    public AnnualReportSubPage2()
    {
        ViewModel = App.GetService<ReportSubPage2ViewModel>();
        this.InitializeComponent();
        ViewModel.WordCloudCanvas = CloudCanvas;
    }
}
