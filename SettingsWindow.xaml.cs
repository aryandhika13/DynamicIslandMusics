using System.Windows;
using DynamicIslandDesktop.Models;

namespace DynamicIslandDesktop
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            DataContext = settings;
        }
    }
}