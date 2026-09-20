using System.Windows;
using SniPro.Core;

namespace SniPro.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StageText.Text = BootstrapInfo.Stage;
    }
}
