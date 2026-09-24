using System.Windows;

namespace KsyxisTweaks
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // URUCHAMIAMY OCHRONIARZA NA SAMYM POCZĄTKU
            AxeDefender.StartWatcher();
        }
    }
}
