using Avalonia.Controls;
using SimpleTarkovManager.ViewModels;

namespace SimpleTarkovManager.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // This event is fired when the DataContext is set.
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, System.EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OnViewChanged += HandleViewChange;
                // Set the initial size
                HandleViewChange(vm.CurrentViewModel is MainViewModel);
            }
        }

        private void HandleViewChange(bool isMainView)
        {
            if (isMainView)
            {
                // Set a larger size for the main view
                this.Width = 800;
                this.Height = 750;
            }
            else
            {
                // Set a smaller size for the login view
                this.Width = 400;
                this.Height = 450;
            }
        }
    }
}